using System.Data;
using System.Globalization;
using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    private async Task LockOutgoingWorkflowAsync(CancellationToken cancellationToken)
    {
        // Transaction-owned SQL lock serializes reservations across receipts sharing a panel.
        await _db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = N'IRIS.OutgoingRemittance',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51000, 'Could not acquire outgoing remittance transaction lock.', 1;
            """, cancellationToken);
    }

    private async Task GeneratePanelRemittancesAsync(BankReceipt receipt, string user, CancellationToken ct)
    {
        var currency = await _db.Currencies.SingleAsync(x => x.IdCurrency == receipt.IdCurrency, ct);
        var precision = PanelRemittanceCalculator.Precision(currency.DecimalPlaces);
        var allocations = await _db.ReceiptAllocations.AsNoTrackingWithIdentityResolution()
            .Include(x => x.IdInvoiceNavigation).ThenInclude(x => x!.PanelAllocations)
            .Where(x => x.IdBankReceipt == receipt.IdBankReceipt && x.IsActive)
            .OrderBy(x => x.IdReceiptAllocation).ToListAsync(ct);
        if (!PanelRemittanceCalculator.IsFullyPaired(receipt.AmountReceived,
                allocations.Sum(x => x.AllocatedAmount ?? 0m), precision)) return;

        foreach (var allocation in allocations)
        {
            // Legacy contract allocations retain their origin; current allocations resolve via the invoice.
            var contractId = allocation.IdInvoiceNavigation?.IdContract ?? allocation.IdContract;
            if (!contractId.HasValue) continue;
            var panels = await _db.ContractPanels.Include(x => x.IdCounterpartyNavigation)
                .Where(x => x.IdContract == contractId && x.Role.Trim().ToLower() == "reinsurer"
                    && (x.Status.Trim().ToLower() == "confirmed" || x.Status.Trim().ToLower() == "bound")
                    && x.IdCounterpartyNavigation.IsActive
                    && x.IdCounterpartyNavigation.TypeAssignments.Any(t => t.Type.Trim().ToLower() == "reinsurer"))
                .OrderBy(x => x.IdPanel).ToListAsync(ct);
            var saved = allocation.IdInvoiceNavigation?.PanelAllocations.ToList() ?? [];
            if (saved.Count > 0)
            {
                var eligibleIds = panels.Select(x => x.IdPanel).ToHashSet();
                if (saved.Any(x => !eligibleIds.Contains(x.IdPanel)))
                    throw new OutgoingRemittanceValidationException("An invoice panel distribution references an ineligible panel.");
                panels = panels.Where(x => saved.Any(s => s.IdPanel == x.IdPanel)).ToList();
            }
            if (panels.Count == 0)
                throw new OutgoingRemittanceValidationException("The allocated invoice has no eligible reinsurer panels.");
            var ids = panels.Select(x => x.IdPanel).ToArray();
            var existing = await _db.RemittanceRefs.Where(x => x.IdReceiptAllocation == allocation.IdReceiptAllocation).ToListAsync(ct);
            if (existing.Any(x => !x.IdPanel.HasValue || !ids.Contains(x.IdPanel.Value)))
                throw new OutgoingRemittanceValidationException("Existing remittances do not match the allocation's eligible panels.");
            var reserved = await _db.RemittanceRefs.Where(x => x.IsActive && x.IdReceiptAllocation != allocation.IdReceiptAllocation
                    && x.IdPanel.HasValue && ids.Contains(x.IdPanel.Value))
                .GroupBy(x => x.IdPanel!.Value)
                .Select(g => new { Id = g.Key, Amount = g.Sum(x => x.NetPayableAmount ?? 0m) })
                .ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
            var weights = panels.Select(p => saved.Count > 0
                ? saved.Single(s => s.IdPanel == p.IdPanel).NetAmount : p.NetPremium ?? 0m).ToArray();
            var capacities = panels.Select(p =>
            {
                var pending = _db.RemittanceRefs.Local.Where(r => _db.Entry(r).State == EntityState.Added && r.IdPanel == p.IdPanel)
                    .Sum(r => r.NetPayableAmount ?? 0m);
                return (p.NetPremium ?? 0m) - reserved.GetValueOrDefault(p.IdPanel) - pending;
            }).ToArray();
            // Saved invoice distributions also cap reservations for that invoice and panel.
            if (saved.Count > 0)
            {
                var savedIds = saved.Select(x => x.IdInvoiceAllocation).ToArray();
                var invoiceReserved = await _db.RemittanceRefs.Where(x => x.IsActive && x.IdReceiptAllocation != allocation.IdReceiptAllocation && x.IdInvoiceAllocation.HasValue
                        && savedIds.Contains(x.IdInvoiceAllocation.Value))
                    .GroupBy(x => x.IdInvoiceAllocation!.Value)
                    .Select(g => new { Id = g.Key, Amount = g.Sum(x => x.NetPayableAmount ?? 0m) })
                    .ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
                for (var i = 0; i < panels.Count; i++)
                {
                    var distribution = saved.Single(s => s.IdPanel == panels[i].IdPanel);
                    var pending = _db.RemittanceRefs.Local.Where(r => _db.Entry(r).State == EntityState.Added
                        && r.IdInvoiceAllocation == distribution.IdInvoiceAllocation).Sum(r => r.NetPayableAmount ?? 0m);
                    capacities[i] = Math.Min(capacities[i], distribution.NetAmount
                        - invoiceReserved.GetValueOrDefault(distribution.IdInvoiceAllocation) - pending);
                }
            }
            var invoice = allocation.IdInvoiceNavigation;
            var grossBasis = invoice?.GrossPremium ?? allocation.AllocatedAmount ?? 0m;
            var netBasis = saved.Count > 0 ? saved.Sum(x => x.NetAmount) : invoice?.NetPremium ?? grossBasis;
            if (grossBasis <= 0m) throw new OutgoingRemittanceValidationException("The allocated invoice has no positive gross premium basis.");
            // Incoming allocations settle the gross invoice; outgoing lines pay its persisted net distribution.
            var payable = decimal.Round((allocation.AllocatedAmount ?? 0m) * netBasis / grossBasis, precision, MidpointRounding.AwayFromZero);
            if (payable == 0m) continue;
            var amounts = PanelRemittanceCalculator.Distribute(payable, weights, capacities, precision);
            var grossWeights = panels.Select(p => saved.Count > 0
                ? saved.Single(s => s.IdPanel == p.IdPanel).AllocationRatio : p.NetPremium ?? 0m).ToArray();
            var grossAmount = decimal.Round(allocation.AllocatedAmount ?? 0m, precision, MidpointRounding.AwayFromZero);
            var grossAmounts = PanelRemittanceCalculator.Distribute(grossAmount, grossWeights,
                Enumerable.Repeat(grossAmount, panels.Count).ToArray(), precision);
            for (var i = 0; i < panels.Count; i++)
            {
                var panel = panels[i];
                var prior = existing.SingleOrDefault(x => x.IdPanel == panel.IdPanel);
                if (prior is not null)
                {
                    if (prior.NetPayableAmount != amounts[i] || prior.IdCurrency != receipt.IdCurrency || prior.IdCounterparty != panel.IdCounterparty)
                        throw new OutgoingRemittanceValidationException($"Remittance {prior.RemittanceRef1} already exists for allocation {allocation.IdReceiptAllocation}, panel {panel.IdPanel}, with a different payable amount or beneficiary.");
                    continue;
                }
                if (await _db.RemittanceRefs.AnyAsync(x => x.IdReceiptAllocation == allocation.IdReceiptAllocation && x.IdPanel == panel.IdPanel, ct)
                    || _db.RemittanceRefs.Local.Any(x => x.IdReceiptAllocation == allocation.IdReceiptAllocation && x.IdPanel == panel.IdPanel)) continue;
                _db.RemittanceRefs.Add(new RemittanceRef
                {
                    IdReceiptAllocation = allocation.IdReceiptAllocation,
                    IdInvoiceAllocation = saved.SingleOrDefault(x => x.IdPanel == panel.IdPanel)?.IdInvoiceAllocation,
                    IdPanel = panel.IdPanel, IdCounterparty = panel.IdCounterparty, IdCurrency = receipt.IdCurrency,
                    SourceType = "Premium", PremiumSignedLine = grossAmounts[i], Deductions = grossAmounts[i] - amounts[i],
                    NetPremiumDue = amounts[i], NetPayableAmount = amounts[i], Status = "Draft", SignatureStatus = "Draft",
                    Description = "Generated from fully paired receipt allocation", CreatedAt = DateTime.UtcNow, CreatedBy = user
                });
            }
        }
    }

    public async Task<ServiceResult<IReadOnlyList<RemittanceResponseDto>>> GenerateReceiptRemittancesAsync(int receiptId, CancellationToken ct)
    {
        if (!CanBack()) return Forbidden<IReadOnlyList<RemittanceResponseDto>>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<IReadOnlyList<RemittanceResponseDto>>();
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockOutgoingWorkflowAsync(ct);
        var receipt = await _db.BankReceipts.SingleOrDefaultAsync(x => x.IdBankReceipt == receiptId, ct);
        if (receipt is null) return NotFound<IReadOnlyList<RemittanceResponseDto>>("Bank receipt was not found.");
        try
        {
            await GeneratePanelRemittancesAsync(receipt, user, ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (OutgoingRemittanceValidationException ex)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return Failure<IReadOnlyList<RemittanceResponseDto>>(ex.Message, status: 409);
        }
        var lines = await RemittanceDetails().Where(x => x.IdReceiptAllocationNavigation!.IdBankReceipt == receiptId).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<RemittanceResponseDto>>.Success(lines.Select(MapOutgoingRemittance).ToList());
    }

    private IQueryable<RemittanceRef> RemittanceDetails() => _db.RemittanceRefs.AsNoTrackingWithIdentityResolution()
        .Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation)
        .Include(x => x.IdPanelNavigation).ThenInclude(x => x.IdContractNavigation)
        .Include(x => x.IdReceiptAllocationNavigation).ThenInclude(x => x!.IdInvoiceNavigation)
        .Include(x => x.IdReceiptAllocationNavigation).ThenInclude(x => x!.IdBankReceiptNavigation)
            .ThenInclude(x => x.ReceiptAllocations)
        .Include(x => x.ClosingLines).ThenInclude(x => x.Closing).AsSplitQuery();

    public async Task<ServiceResult<PagedResponseDto<RemittanceResponseDto>>> ListRemittancesAsync(RemittanceQueryDto query, CancellationToken ct)
    {
        if (!CanFinanceOrBack()) return Forbidden<PagedResponseDto<RemittanceResponseDto>>();
        if (query.DateFrom > query.DateTo) return Failure<PagedResponseDto<RemittanceResponseDto>>("Creation date range is invalid.");
        var rows = RemittanceDetails().Where(x => x.IsActive);
        var term = Optional(query.Search);
        if (term is not null) rows = rows.Where(x => x.RemittanceRef1.ToString().Contains(term)
            || ("REM-" + x.RemittanceRef1.ToString()).Contains(term)
            || x.IdCounterpartyNavigation.Name.Contains(term)
            || x.IdPanelNavigation.IdContractNavigation.Name.Contains(term)
            || x.IdPanelNavigation.IdContractNavigation.Umr.Contains(term)
            || x.IdPanelNavigation.PremiumReference.Contains(term)
            || x.IdReceiptAllocationNavigation!.IdInvoiceNavigation!.InvoiceNumber.Contains(term)
            || x.IdReceiptAllocationNavigation.IdBankReceiptNavigation.BankReference.Contains(term)
            || x.IdReceiptAllocationNavigation.IdBankReceiptNavigation.BusinessReference!.Contains(term));
        if (query.ReinsurerId.HasValue) rows = rows.Where(x => x.IdCounterparty == query.ReinsurerId);
        if (Optional(query.Reinsurer) is { } reinsurer) rows = rows.Where(x => x.IdCounterpartyNavigation.Name.Contains(reinsurer));
        if (query.ContractId.HasValue) rows = rows.Where(x => x.IdPanelNavigation.IdContract == query.ContractId);
        if (Optional(query.Contract) is { } contract) rows = rows.Where(x => x.IdPanelNavigation.IdContractNavigation.Name.Contains(contract) || x.IdPanelNavigation.IdContractNavigation.Umr.Contains(contract));
        if (Optional(query.Umr) is { } umr) rows = rows.Where(x => x.IdPanelNavigation.IdContractNavigation.Umr.Contains(umr));
        if (Optional(query.Status) is { } status) rows = rows.Where(x => x.Status == status);
        if (query.CurrencyId.HasValue) rows = rows.Where(x => x.IdCurrency == query.CurrencyId);
        if (Optional(query.Currency) is { } currency) rows = rows.Where(x => x.IdCurrencyNavigation.CurrencyCode == currency);
        if (query.DateFrom.HasValue) { var from = query.DateFrom.Value.ToDateTime(TimeOnly.MinValue); rows = rows.Where(x => x.CreatedAt >= from); }
        if (query.DateTo.HasValue) { var until = query.DateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue); rows = rows.Where(x => x.CreatedAt < until); }
        var total = await rows.CountAsync(ct);
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<RemittanceRef> ordered = query.SortBy?.ToLowerInvariant() switch
        {
            "reference" or "remittancereference" => descending ? rows.OrderByDescending(x => x.RemittanceRef1) : rows.OrderBy(x => x.RemittanceRef1),
            "reinsurer" or "reinsurername" => descending ? rows.OrderByDescending(x => x.IdCounterpartyNavigation.Name) : rows.OrderBy(x => x.IdCounterpartyNavigation.Name),
            "contract" or "contractname" => descending ? rows.OrderByDescending(x => x.IdPanelNavigation.IdContractNavigation.Name) : rows.OrderBy(x => x.IdPanelNavigation.IdContractNavigation.Name),
            "umr" => descending ? rows.OrderByDescending(x => x.IdPanelNavigation.IdContractNavigation.Umr) : rows.OrderBy(x => x.IdPanelNavigation.IdContractNavigation.Umr),
            "amount" or "netpayableamount" => descending ? rows.OrderByDescending(x => x.NetPayableAmount) : rows.OrderBy(x => x.NetPayableAmount),
            "status" => descending ? rows.OrderByDescending(x => x.Status) : rows.OrderBy(x => x.Status),
            "currency" => descending ? rows.OrderByDescending(x => x.IdCurrencyNavigation.CurrencyCode) : rows.OrderBy(x => x.IdCurrencyNavigation.CurrencyCode),
            _ => descending || query.SortBy is null ? rows.OrderByDescending(x => x.CreatedAt) : rows.OrderBy(x => x.CreatedAt)
        };
        var size = Math.Clamp(query.PageSize, 1, 100);
        var page = Math.Clamp(query.PageNumber, 1, int.MaxValue / size);
        var items = await ordered.ThenBy(x => x.RemittanceRef1).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return ServiceResult<PagedResponseDto<RemittanceResponseDto>>.Success(new()
        {
            Items = items.Select(MapOutgoingRemittance).ToList(), TotalCount = total, PageNumber = page, PageSize = size
        });
    }

    private static decimal RemainingPayable(RemittanceRef x) => x.Status is "Closed" or "Paid" ? 0m : x.NetPayableAmount ?? 0m;

    private static bool ClosingEligible(RemittanceRef x) => x.IsActive && x.IdPanelNavigation is not null
        && x.IdPanelNavigation.IdCounterparty == x.IdCounterparty
        && string.Equals(x.IdPanelNavigation.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(x.IdPanelNavigation.Status?.Trim(), "Confirmed", StringComparison.OrdinalIgnoreCase) || string.Equals(x.IdPanelNavigation.Status?.Trim(), "Bound", StringComparison.OrdinalIgnoreCase))
        && x.IdCounterpartyNavigation.IsActive && x.IdCurrencyNavigation.IsActive
        && x.IdDisbursement is null && (x.Status is "Draft" or "ReadyForDisbursement" or "AwaitingFinanceDisbursement")
        && RemainingPayable(x) > 0m && !x.ClosingIssuedDate.HasValue
        && x.IdReceiptAllocationNavigation is { IsActive: true } origin
        && origin.IdCurrency == x.IdCurrency && origin.IdBankReceiptNavigation.IdCurrency == x.IdCurrency
        && (origin.IdInvoiceNavigation?.IdContract ?? origin.IdContract) == x.IdPanelNavigation.IdContract
        && PanelRemittanceCalculator.IsFullyPaired(origin.IdBankReceiptNavigation.AmountReceived,
            origin.IdBankReceiptNavigation.ReceiptAllocations.Where(a => a.IsActive).Sum(a => a.AllocatedAmount ?? 0m),
            PanelRemittanceCalculator.Precision(x.IdCurrencyNavigation.DecimalPlaces))
        && !x.ClosingLines.Any(l => l.IsActive && l.Closing.IsActive);

    private static RemittanceResponseDto MapOutgoingRemittance(RemittanceRef x)
    {
        var dto = ToRemittance(x);
        dto.RemittanceId = x.RemittanceRef1;
        dto.RemittanceReference = $"REM-{x.RemittanceRef1}";
        dto.ReinsurerId = x.IdCounterparty;
        dto.ReinsurerName = x.IdCounterpartyNavigation.Name;
        dto.ReceiptReference = x.IdReceiptAllocationNavigation?.IdBankReceiptNavigation?.BusinessReference
            ?? x.IdReceiptAllocationNavigation?.IdBankReceiptNavigation?.BankReference;
        dto.InvoiceId = x.IdReceiptAllocationNavigation?.IdInvoice;
        dto.InvoiceNumber = x.IdReceiptAllocationNavigation?.IdInvoiceNavigation?.InvoiceNumber;
        dto.PanelName = x.IdPanelNavigation?.PremiumReference ?? x.IdCounterpartyNavigation.Name;
        dto.CreatedAt = x.CreatedAt;
        dto.ClosingIssuedDate = x.ClosingIssuedDate;
        var closing = x.ClosingLines.FirstOrDefault(l => l.IsActive && l.Closing.IsActive)?.Closing;
        dto.ClosingId = closing?.Id;
        dto.ClosingReference = closing?.Reference;
        dto.ClosingEligible = ClosingEligible(x);
        dto.RemainingPayableAmount = RemainingPayable(x);
        return dto;
    }

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> CreateOutgoingClosingAsync(OutgoingClosingRequestDto request, CancellationToken ct)
    {
        if (!CanBack()) return Forbidden<PrintablePaymentDocumentDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<PrintablePaymentDocumentDto>();
        var ids = request.RemittanceIds;
        if (ids is null || ids.Count == 0 || ids.Count > 500 || ids.Any(x => x <= 0) || ids.Distinct().Count() != ids.Count)
            return Failure<PrintablePaymentDocumentDto>("Select between 1 and 500 distinct remittance IDs.");
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockOutgoingWorkflowAsync(ct);
        var lines = await RemittanceDetails().AsTracking().Where(x => ids.Contains(x.RemittanceRef1)).OrderBy(x => x.RemittanceRef1).ToListAsync(ct);
        if (lines.Count != ids.Count || lines.Any(x => !x.IsActive)) return Failure<PrintablePaymentDocumentDto>("All selected remittances must exist and be active.");
        if (lines.Select(x => x.IdCounterparty).Distinct().Count() != 1) return Failure<PrintablePaymentDocumentDto>("Selected remittances must belong to the same reinsurer.");
        if (lines.Select(x => x.IdCurrency).Distinct().Count() != 1) return Failure<PrintablePaymentDocumentDto>("Selected remittances must use the same currency.");
        if (lines.Any(x => !ClosingEligible(x))) return Failure<PrintablePaymentDocumentDto>("One or more selected remittances are ineligible, already closed, or have no remaining payable amount.");
        if (lines.Any(x => x.IdReceiptAllocationNavigation is not { IsActive: true }))
            return Failure<PrintablePaymentDocumentDto>("Selected remittances must have an active receipt allocation origin.");
        var receiptIds = lines.Select(x => x.IdReceiptAllocationNavigation!.IdBankReceipt).Distinct().ToArray();
        var receipts = await _db.BankReceipts.Include(x => x.ReceiptAllocations).Include(x => x.IdCurrencyNavigation)
            .Where(x => receiptIds.Contains(x.IdBankReceipt)).ToListAsync(ct);
        if (receipts.Any(x => !PanelRemittanceCalculator.IsFullyPaired(x.AmountReceived,
                x.ReceiptAllocations.Where(a => a.IsActive).Sum(a => a.AllocatedAmount ?? 0m),
                PanelRemittanceCalculator.Precision(x.IdCurrencyNavigation.DecimalPlaces))))
            return Failure<PrintablePaymentDocumentDto>("All selected remittances must originate from fully paired receipts.");
        var first = lines[0];
        var now = DateTime.UtcNow;
        var currency = first.IdCurrencyNavigation;
        var reinsurer = first.IdCounterpartyNavigation;
        var account = await _db.CounterpartyBankAccounts.AsNoTracking()
            .Where(x => x.IdCounterparty == first.IdCounterparty && x.IdCurrency == first.IdCurrency && x.IsActive)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.IdBankAccount).FirstOrDefaultAsync(ct);
        var closing = new OutgoingClosing
        {
            Reference = $"CL-{now:yyyyMMdd}-{Guid.NewGuid():N}", IssuedAt = now, CreatedBy = user,
            IdCounterparty = first.IdCounterparty, IdCurrency = first.IdCurrency,
            TotalAmount = lines.Sum(RemainingPayable)
        };
        closing.TotalInWords = AmountInWords(closing.TotalAmount, currency.CurrencyCode, PanelRemittanceCalculator.Precision(currency.DecimalPlaces));
        var text = new List<string>
        {
            "IRIS - OUTGOING REINSURER CLOSING", $"Closing: {closing.Reference}", $"Issue date: {now:yyyy-MM-dd}",
            $"Reinsurer: {reinsurer.Name}", $"Address: {reinsurer.AddressLine}, {reinsurer.City}, {reinsurer.Country}",
            $"Bank: {account?.BankName ?? "Not supplied"} / {account?.BankCity} / {account?.BankCountry}",
            $"Account/RIB: {account?.Rib ?? "Not supplied"}", $"IBAN: {account?.Iban ?? "Not supplied"}",
            $"SWIFT/BIC: {account?.SwiftBic ?? "Not supplied"}",
            $"Correspondent bank: {account?.CorrespondentBankName} / {account?.CorrespondentSwiftBic}", ""
        };
        foreach (var line in lines)
        {
            var dto = MapOutgoingRemittance(line);
            text.Add($"Contract: {dto.ContractName}; UMR: {dto.Umr}");
            text.Add($"Panel: {dto.PanelName}; Remittance: {dto.RemittanceReference}");
            text.Add($"Invoice: {dto.InvoiceNumber}; Encaissement: {dto.ReceiptReference}");
            text.Add($"Payable: {RemainingPayable(line).ToString($"F{PanelRemittanceCalculator.Precision(currency.DecimalPlaces)}", CultureInfo.InvariantCulture)} {currency.CurrencyCode}");
            text.Add("");
            closing.Lines.Add(new OutgoingClosingLine { Remittance = line, PayableAmount = RemainingPayable(line) });
            line.ClosingIssuedDate = DateOnly.FromDateTime(now);
            line.Status = "AwaitingFinanceDisbursement";
            line.ConfirmedAt ??= now;
            line.ConfirmedBy ??= user;
            line.UpdatedAt = now;
            line.UpdatedBy = user;
        }
        text.Add($"Total payable: {closing.TotalAmount.ToString($"F{PanelRemittanceCalculator.Precision(currency.DecimalPlaces)}", CultureInfo.InvariantCulture)} {currency.CurrencyCode}");
        text.Add($"In words: {closing.TotalInWords}");
        text.Add("");
        text.Add("Prepared by: ____________________  Authorized signature: ____________________");
        text.Add("Bank processing / transfer reference: _______________________________________");
        text.Add("Processing date: ____________________  Bank stamp: _________________________");
        closing.PdfContent = OutgoingClosingPdf.Build(text);
        _db.OutgoingClosings.Add(closing);
        await _db.SaveChangesAsync(ct);
        await NotifyRoleAsync("Finance Officer", "Closing issued - disbursement required",
            $"Closing {closing.Reference} is ready for payment ({lines.Count} remittance lines).", "OUTGOING_CLOSING", closing.Id, ct, "CLOSING_DISBURSEMENT_REQUIRED");
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ServiceResult<PrintablePaymentDocumentDto>.Success(ClosingDocument(closing));
    }

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetOutgoingClosingDocumentAsync(int closingId, CancellationToken ct)
    {
        if (!CanFinanceOrBack()) return Forbidden<PrintablePaymentDocumentDto>();
        var closing = await _db.OutgoingClosings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == closingId && x.IsActive, ct);
        if (closing is not null && closing.PdfContent.Length == 0)
            return ServiceResult<PrintablePaymentDocumentDto>.Failure("The closing exists but its PDF is not available.", statusCode: 409, code: "CLOSING_PDF_UNAVAILABLE");
        return closing is null ? NotFound<PrintablePaymentDocumentDto>("Closing was not found.")
            : ServiceResult<PrintablePaymentDocumentDto>.Success(ClosingDocument(closing));
    }

    private static PrintablePaymentDocumentDto ClosingDocument(OutgoingClosing closing) => new()
    {
        Content = closing.PdfContent, ContentType = "application/pdf", FileName = $"IRIS-{closing.Reference}.pdf"
    };

    private static string AmountInWords(decimal amount, string currency, int precision)
    {
        string[] ones = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
        string[] tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];
        string Words(long value)
        {
            if (value < 20) return ones[value];
            if (value < 100) return tens[value / 10] + (value % 10 == 0 ? "" : " " + Words(value % 10));
            if (value < 1000) return Words(value / 100) + " hundred" + (value % 100 == 0 ? "" : " " + Words(value % 100));
            foreach (var (scale, name) in new[] { (1_000_000_000_000L, "trillion"), (1_000_000_000L, "billion"), (1_000_000L, "million"), (1000L, "thousand") })
                if (value >= scale) return Words(value / scale) + " " + name + (value % scale == 0 ? "" : " " + Words(value % scale));
            return "";
        }
        var whole = (long)decimal.Truncate(amount);
        var minor = (long)decimal.Round((amount - whole) / PanelRemittanceCalculator.Unit(precision));
        return Words(whole) + " " + currency + (precision == 0 ? "" : " and " + Words(minor) + $" minor units (1/{1m / PanelRemittanceCalculator.Unit(precision):0})");
    }
}
