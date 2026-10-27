using System.Data;
using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    public async Task<ServiceResult<IReadOnlyList<FinanceClosingQueueItemDto>>> ListClosingDisbursementQueueAsync(CancellationToken ct)
    {
        if (!CanFinance()) return Forbidden<IReadOnlyList<FinanceClosingQueueItemDto>>();
        var closings = await _db.OutgoingClosings.AsNoTracking()
            .Where(x => x.IsActive && (x.Status == "AwaitingFinanceDisbursement" || x.Status == "PaymentInProgress" || x.Status == "Closed"))
            .Include(x => x.Lines.Where(line => line.IsActive)).ThenInclude(line => line.Remittance)
            .OrderByDescending(x => x.IssuedAt).ThenByDescending(x => x.Id)
            .ToListAsync(ct);
        var counterpartyIds = closings.Select(x => x.IdCounterparty).Distinct().ToArray();
        var currencyIds = closings.Select(x => x.IdCurrency).Distinct().ToArray();
        var counterparties = await _db.Counterparties.AsNoTracking().Where(x => counterpartyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var currencies = await _db.Currencies.AsNoTracking().Where(x => currencyIds.Contains(x.IdCurrency))
            .ToDictionaryAsync(x => x.IdCurrency, ct);

        var items = closings.Select(closing =>
        {
            var lines = closing.Lines.Where(line => line.IsActive && line.Remittance is not null).ToList();
            counterparties.TryGetValue(closing.IdCounterparty, out var counterparty);
            currencies.TryGetValue(closing.IdCurrency, out var currency);
            var precision = PanelRemittanceCalculator.Precision(currency?.DecimalPlaces);
            var premiumTotal = lines.Where(line => !IsAvoirSource(line.Remittance.SourceType))
                .Sum(line => Math.Abs(line.PayableAmount));
            var avoirTotal = lines.Where(line => IsAvoirSource(line.Remittance.SourceType))
                .Sum(line => Math.Abs(line.PayableAmount));
            var netPayable = RoundMoney(lines.Sum(line => line.PayableAmount), precision);
            return new FinanceClosingQueueItemDto
            {
                ClosingId = closing.Id,
                ClosingReference = closing.Reference,
                ReinsurerId = closing.IdCounterparty,
                ReinsurerName = counterparty?.Name ?? string.Empty,
                LineCount = lines.Count,
                PremiumTotal = RoundMoney(premiumTotal, precision),
                AvoirTotal = RoundMoney(avoirTotal, precision),
                NetPayableAmount = netPayable,
                SignedNetPayableAmount = netPayable,
                CurrencyId = closing.IdCurrency,
                CurrencyCode = currency?.CurrencyCode ?? string.Empty,
                IssueDate = closing.IssuedAt,
                Status = closing.Status,
                AvailableAction = closing.Status switch
                {
                    "AwaitingFinanceDisbursement" => "MakeDisbursement",
                    "PaymentInProgress" => "ContinuePayment",
                    "Closed" => "ViewInvoice",
                    _ => "None"
                },
                DisbursementId = closing.IdDisbursement
            };
        }).ToList();
        return ServiceResult<IReadOnlyList<FinanceClosingQueueItemDto>>.Success(items);
    }

    public async Task<ServiceResult<ClosingDisbursementContextDto>> GetClosingDisbursementContextAsync(int closingId, CancellationToken ct)
    {
        if (!CanFinance()) return Forbidden<ClosingDisbursementContextDto>();
        var closing = await _db.OutgoingClosings.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == closingId && x.IsActive, ct);
        if (closing is null) return NotFound<ClosingDisbursementContextDto>("Closing was not found.", "CLOSING_NOT_FOUND");
        var ids = closing.Lines.Where(x => x.IsActive).Select(x => x.RemittanceId).ToArray();
        var remittances = await RemittanceDetails().Where(x => ids.Contains(x.RemittanceRef1)).OrderBy(x => x.RemittanceRef1).ToListAsync(ct);
        var reinsurer = await _db.Counterparties.AsNoTracking().SingleAsync(x => x.Id == closing.IdCounterparty, ct);
        var currency = await _db.Currencies.AsNoTracking().SingleAsync(x => x.IdCurrency == closing.IdCurrency, ct);
        var precision = PanelRemittanceCalculator.Precision(currency.DecimalPlaces);
        var accounts = await _db.CounterpartyBankAccounts.AsNoTracking().Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation)
            .Where(x => x.IdCounterparty == closing.IdCounterparty && x.IdCurrency == closing.IdCurrency && x.IsActive)
            .OrderBy(x => x.IdBankAccount).ToListAsync(ct);
        var selection = BankAccountEligibility.Select(accounts);
        var bank = selection.Account is null ? null : MapBankAccountSnapshot(selection.Account);
        if (closing.IdDisbursement.HasValue)
        {
            var paymentAccount = await _db.Disbursements.Where(x => x.IdDisbursement == closing.IdDisbursement.Value).Select(x => x.IdBankAccount).SingleAsync(ct);
            bank = await GetBankAccountSnapshotAsync(paymentAccount, closing.IdCounterparty, closing.IdCurrency, ct);
        }
        var remittanceById = remittances.ToDictionary(x => x.RemittanceRef1);
        var activeLines = closing.Lines.Where(x => x.IsActive).ToArray();
        var calculatedTotal = RoundMoney(activeLines.Sum(line => line.PayableAmount), precision);
        var storedTotal = RoundMoney(closing.TotalAmount, precision);
        var difference = RoundMoney(calculatedTotal - storedTotal, precision);
        var isConsistent = difference == 0m;
        var validRelationships = ids.Length > 0 && remittances.Count == ids.Length && activeLines.Length == ids.Length
            && remittances.All(x => x.IsActive && x.IdCounterparty == closing.IdCounterparty && x.IdCurrency == closing.IdCurrency)
            && activeLines.All(line => remittanceById.ContainsKey(line.RemittanceId)
                && RoundMoney(line.PayableAmount, precision) == RoundMoney(SignedRemittanceAmount(remittanceById[line.RemittanceId]), precision));
        var validLines = validRelationships && isConsistent && storedTotal > 0m;
        var rows = remittances.Select(x =>
        {
            var dto = MapOutgoingRemittance(x);
            var signedAmount = RoundMoney(activeLines.Single(line => line.RemittanceId == x.RemittanceRef1).PayableAmount, precision);
            dto.Amount = signedAmount;
            dto.NetPayableAmount = signedAmount;
            dto.SignedNetPayableAmount = signedAmount;
            return dto;
        }).ToList();
        return ServiceResult<ClosingDisbursementContextDto>.Success(new()
        {
            ClosingId = closing.Id, ClosingReference = closing.Reference, ClosingDate = closing.IssuedAt, Status = closing.Status,
            ReinsurerId = reinsurer.Id, ReinsurerName = reinsurer.Name, ReinsurerAddress = reinsurer.AddressLine,
            ReinsurerCity = reinsurer.City, ReinsurerCountry = reinsurer.Country,
            CurrencyId = currency.IdCurrency, CurrencyCode = currency.CurrencyCode,
            CurrencyDecimalPlaces = precision,
            TotalClosingAmount = storedTotal, StoredTotal = storedTotal, CalculatedTotal = calculatedTotal,
            Difference = difference, IsConsistent = isConsistent,
            Lines = rows, BankAccounts = accounts.Select(MapBankAccount).ToList(),
            SelectedBankAccount = bank, DisbursementId = closing.IdDisbursement,
            CanDownloadPdf = closing.PdfContent.Length > 0,
            PdfDownloadUrl = closing.PdfContent.Length > 0 ? $"/api/finance/closings/{closing.Id}/pdf" : null,
            CanStartPayment = validLines && selection.IsValid && reinsurer.IsActive && closing.Status == "AwaitingFinanceDisbursement"
                && !closing.IdDisbursement.HasValue && remittances.All(x => x.Status == "AwaitingFinanceDisbursement" && !x.IdDisbursement.HasValue),
            CanConfirmTransfer = validLines && closing.Status == "PaymentInProgress" && closing.IdDisbursement.HasValue
                && remittances.All(x => x.Status == "PaymentInProgress" && x.IdDisbursement == closing.IdDisbursement),
            ValidationMessage = !validRelationships ? "One or more closing lines are missing or do not match the closing reinsurer/currency."
                : !isConsistent ? "The closing lines or total are inconsistent; payment cannot proceed."
                : storedTotal <= 0m ? "The calculated closing amount is not payable."
                : selection.ValidationMessage
        });
    }

    public Task<ServiceResult<PrintablePaymentDocumentDto>> GetFinanceClosingPdfAsync(int closingId, CancellationToken ct) =>
        CanFinance() ? GetOutgoingClosingDocumentAsync(closingId, ct) : Task.FromResult(Forbidden<PrintablePaymentDocumentDto>());

    public async Task<ServiceResult<ClosingDisbursementContextDto>> StartClosingDisbursementAsync(int closingId, CancellationToken ct)
    {
        if (!CanFinance()) return Forbidden<ClosingDisbursementContextDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<ClosingDisbursementContextDto>();
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockOutgoingWorkflowAsync(ct);
        var context = await GetClosingDisbursementContextAsync(closingId, ct);
        if (!context.Succeeded) return context;
        var closing = await _db.OutgoingClosings.Include(x => x.Lines).ThenInclude(x => x.Remittance).SingleAsync(x => x.Id == closingId, ct);
        if (closing.IdDisbursement.HasValue && closing.Status is "PaymentInProgress" or "Closed")
        {
            await tx.CommitAsync(ct);
            return context;
        }
        if (!context.Value!.CanStartPayment)
            return ServiceResult<ClosingDisbursementContextDto>.Failure(context.Value.ValidationMessage ?? "The closing is not eligible to start payment.", statusCode: 409, code: "CLOSING_NOT_PAYABLE");
        var account = context.Value.SelectedBankAccount!;
        var now = DateTime.UtcNow;
        var disbursement = new Disbursement
        {
            IdBankAccount = account.IdBankAccount, IdCounterparty = closing.IdCounterparty, IdUser = user,
            CaseReference = closing.Reference, TotalAmount = context.Value.CalculatedTotal, OperationDate = DateOnly.FromDateTime(now),
            PaymentMethode = "BankTransfer", SignatureStatus = "PaymentInProgress", Description = $"Payment for closing {closing.Reference}",
            CreatedAt = now, CreatedBy = user, StartedAt = now, StartedBy = user,
            BankNameSnapshot = account.BankName, BankCitySnapshot = account.BankCity, BankCountrySnapshot = account.BankCountry,
            IbanSnapshot = account.Iban, SwiftBicSnapshot = account.SwiftBic,
            CorrespondentBankNameSnapshot = account.CorrespondentBankName, CorrespondentSwiftBicSnapshot = account.CorrespondentSwiftBic,
            CurrencyCodeSnapshot = context.Value.CurrencyCode, ReinsurerNameSnapshot = context.Value.ReinsurerName,
            ReinsurerAddressSnapshot = context.Value.ReinsurerAddress, ReinsurerCitySnapshot = context.Value.ReinsurerCity,
            ReinsurerCountrySnapshot = context.Value.ReinsurerCountry
        };
        _db.Disbursements.Add(disbursement);
        await _db.SaveChangesAsync(ct);
        closing.IdDisbursement = disbursement.IdDisbursement;
        closing.Status = "PaymentInProgress";
        foreach (var line in closing.Lines.Where(x => x.IsActive))
        {
            line.Remittance.IdDisbursement = disbursement.IdDisbursement;
            line.Remittance.Status = closing.Status;
            line.Remittance.UpdatedAt = now;
            line.Remittance.UpdatedBy = user;
        }
        await MarkClosingNotificationsReadAsync(closing, now, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetClosingDisbursementContextAsync(closingId, ct);
    }

    public async Task<ServiceResult<ClosingDisbursementContextDto>> ConfirmClosingTransferAsync(int closingId, CancellationToken ct)
    {
        if (!CanFinance()) return Forbidden<ClosingDisbursementContextDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<ClosingDisbursementContextDto>();
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockOutgoingWorkflowAsync(ct);
        var context = await GetClosingDisbursementContextAsync(closingId, ct);
        if (!context.Succeeded) return context;
        var closing = await _db.OutgoingClosings.Include(x => x.Lines).ThenInclude(x => x.Remittance).SingleAsync(x => x.Id == closingId, ct);
        if (closing.Status == "Closed" && closing.IdDisbursement.HasValue)
        {
            await tx.CommitAsync(ct);
            return context;
        }
        if (!context.Value!.CanConfirmTransfer)
            return ServiceResult<ClosingDisbursementContextDto>.Failure("Only a complete closing payment in progress can be confirmed.", statusCode: 409, code: "CLOSING_NOT_IN_PROGRESS");
        var disbursement = await _db.Disbursements.SingleAsync(x => x.IdDisbursement == closing.IdDisbursement, ct);
        var precision = context.Value.CurrencyDecimalPlaces;
        if (disbursement.SignatureStatus != "PaymentInProgress"
            || RoundMoney(disbursement.TotalAmount, precision) != context.Value.CalculatedTotal)
            return ServiceResult<ClosingDisbursementContextDto>.Failure("The closing payment does not match its persisted total or status.", statusCode: 409, code: "CLOSING_PAYMENT_MISMATCH");
        var now = DateTime.UtcNow;
        disbursement.SignatureStatus = closing.Status = "Closed";
        disbursement.ClosedAt = now;
        disbursement.ClosedBy = user;
        disbursement.FinancialOfficerSignatureDate = DateOnly.FromDateTime(now);
        disbursement.UpdatedAt = now;
        disbursement.UpdatedBy = user;
        foreach (var line in closing.Lines.Where(x => x.IsActive))
        {
            line.Remittance.Status = closing.Status;
            line.Remittance.UpdatedAt = now;
            line.Remittance.UpdatedBy = user;
        }
        await MarkClosingNotificationsReadAsync(closing, now, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetClosingDisbursementContextAsync(closingId, ct);
    }

    private Task<int> MarkClosingNotificationsReadAsync(OutgoingClosing closing, DateTime now, CancellationToken ct)
    {
        var ids = closing.Lines.Where(x => x.IsActive).Select(x => x.RemittanceId).ToArray();
        var user = CurrentUser();
        return _db.Notifications.Where(x => x.RecipientUserId == user && !x.IsRead &&
            (x.ClosingId == closing.Id || (x.EntityType == "REMITTANCE_REF" && x.EntityId.HasValue && ids.Contains(x.EntityId.Value))))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAt, now), ct);
    }
}
