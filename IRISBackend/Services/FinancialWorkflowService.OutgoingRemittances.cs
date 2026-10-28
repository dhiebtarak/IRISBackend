using System.Data;
using System.Globalization;
using System.Text;
using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    private async Task CreateDirectAvoirRemittancesAsync(Invoice avoir, Contract contract, string user, CancellationToken ct)
    {
        var traceId = _http.HttpContext?.TraceIdentifier ?? "unavailable";
        _logger.LogInformation(
            "Direct Avoir stage=AllocationLoadStarted TraceId={TraceId} ContractId={ContractId} InvoiceId={InvoiceId} UserId={UserId}",
            traceId, contract.IdContract, avoir.IdInvoice, user);
        var allocationRows = await _db.InvoicePanelAllocations.AsNoTracking()
            .Include(line => line.IdCounterpartyNavigation)
                .ThenInclude(counterparty => counterparty.TypeAssignments)
            .Include(line => line.IdPanelNavigation)
                .ThenInclude(panel => panel.IdCounterpartyNavigation)
                    .ThenInclude(counterparty => counterparty.TypeAssignments)
            .Where(line => line.IdInvoice == avoir.IdInvoice)
            .OrderBy(line => line.IdPanel)
            .ToListAsync(ct);

        _logger.LogInformation(
            "Direct Avoir stage=AllocationRowsLoaded TraceId={TraceId} ContractId={ContractId} InvoiceId={InvoiceId} AllocationCount={AllocationCount}",
            traceId, contract.IdContract, avoir.IdInvoice, allocationRows.Count);

        var eligible = allocationRows.Where(line =>
                line.IdPanelNavigation is { } panel
                && line.IdCounterpartyNavigation is { } counterparty
                && panel.IdContract == contract.IdContract
                && panel.IdCounterparty == line.IdCounterparty
                && string.Equals(panel.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase)
                && string.Equals(panel.Status?.Trim(), ContractConstants.ConfirmedPanelStatus, StringComparison.OrdinalIgnoreCase)
                && counterparty.IsActive
                && counterparty.TypeAssignments is not null
                && counterparty.TypeAssignments.Any(type => type is not null
                    && string.Equals(type.Type?.Trim(), CounterpartyTypes.Reinsurer, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (eligible.Count == 0 || eligible.Count != allocationRows.Count)
            throw new OutgoingRemittanceValidationException(
                "Every Avoir allocation must belong to an active reinsurer on a confirmed panel of the selected contract.");

        if (eligible.Any(line => PremiumDebitNoteAllocationCalculator.NetPayableFromPersistedAllocation(line) < 0m))
            throw new OutgoingRemittanceValidationException(
                "An Avoir allocation has an invalid negative net-payable amount.");

        _logger.LogInformation(
            "Direct Avoir stage=PanelReinsurersValidated TraceId={TraceId} ContractId={ContractId} InvoiceId={InvoiceId} PanelCount={PanelCount}",
            traceId, contract.IdContract, avoir.IdInvoice, eligible.Count);

        var existing = await _db.RemittanceRefs.AsNoTracking()
            .Where(row => row.IsActive && row.SourceInvoiceId == avoir.IdInvoice
                && row.SourceType == "AVOIR" && row.IdReceiptAllocation == null
                && row.IdInvoiceAllocation != null && row.IdPanel != null)
            .ToDictionaryAsync(row => new { AllocationId = row.IdInvoiceAllocation!.Value, PanelId = row.IdPanel!.Value }, ct);
        var currency = await _db.Currencies.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IdCurrency == contract.IdCurrency, ct);
        if (currency is null)
            throw new OutgoingRemittanceValidationException("The Avoir contract currency is not configured.");
        var precision = PanelRemittanceCalculator.Precision(currency.DecimalPlaces);
        var now = DateTime.UtcNow;

        foreach (var line in eligible)
        {
            var panel = line.IdPanelNavigation;
            var counterparty = line.IdCounterpartyNavigation;
            var payable = RoundMoney(Math.Abs(PremiumDebitNoteAllocationCalculator.NetPayableFromPersistedAllocation(line)), precision);
            var key = new { AllocationId = line.IdInvoiceAllocation, PanelId = line.IdPanel };
            if (existing.TryGetValue(key, out var prior))
            {
                if (prior.IdCounterparty != counterparty.Id || prior.IdCurrency != contract.IdCurrency
                    || prior.NetPayableAmount != payable)
                    throw new OutgoingRemittanceValidationException(
                        $"Avoir remittance already exists for panel {panel.IdPanel} with different allocation data.");
                continue;
            }

            _db.RemittanceRefs.Add(new RemittanceRef
            {
                SourceType = "AVOIR",
                SourceInvoiceId = avoir.IdInvoice,
                IdInvoiceAllocation = line.IdInvoiceAllocation,
                IdReceiptAllocation = null,
                BeneficiaryType = "REINSURER",
                IdPanel = panel.IdPanel,
                IdCounterparty = counterparty.Id,
                IdCurrency = contract.IdCurrency!.Value,
                PartPct = line.AllocationRatio * 100m,
                PremiumSignedLine = Math.Abs(line.GrossPremium),
                Deductions = Math.Abs(line.GrossPremium - payable),
                NetPremiumDue = payable,
                NetPayableAmount = payable,
                Description = $"Generated directly from Avoir {avoir.InvoiceNumber}",
                Status = "Draft",
                SignatureStatus = "Draft",
                CreatedAt = now,
                CreatedBy = user,
                IsActive = true
            });
        }

        _logger.LogInformation(
            "Direct Avoir stage=RemittancesPrepared TraceId={TraceId} ContractId={ContractId} InvoiceId={InvoiceId} CreatedCount={CreatedCount}",
            traceId, contract.IdContract, avoir.IdInvoice, eligible.Count);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Direct Avoir stage=RemittancesSaved TraceId={TraceId} ContractId={ContractId} InvoiceId={InvoiceId}",
            traceId, contract.IdContract, avoir.IdInvoice);
    }

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

    private async Task GeneratePanelRemittancesAsync(BankReceipt receipt, string user, CancellationToken ct,
        IReadOnlyCollection<int>? allocationIds = null)
    {
        var currency = await _db.Currencies.SingleAsync(x => x.IdCurrency == receipt.IdCurrency, ct);
        var precision = PanelRemittanceCalculator.Precision(currency.DecimalPlaces);
        var allocations = await _db.ReceiptAllocations.AsNoTrackingWithIdentityResolution()
            .Include(x => x.IdInvoiceNavigation).ThenInclude(x => x!.PanelAllocations).ThenInclude(x => x.IdPanelNavigation)
                .ThenInclude(x => x.IdCounterpartyNavigation).ThenInclude(x => x.TypeAssignments)
            .Include(x => x.IdInvoiceNavigation).ThenInclude(x => x!.CreditApplicationsAsTarget)
                .ThenInclude(application => application.AvoirInvoiceNavigation).ThenInclude(invoice => invoice.PanelAllocations)
                    .ThenInclude(line => line.IdPanelNavigation).ThenInclude(panel => panel.IdCounterpartyNavigation).ThenInclude(counterparty => counterparty.TypeAssignments)
            .Include(x => x.IdInvoiceNavigation).ThenInclude(x => x!.CreditApplicationsAsTarget)
                .ThenInclude(application => application.AvoirInvoiceNavigation).ThenInclude(invoice => invoice.PanelAllocations)
                    .ThenInclude(line => line.IdCounterpartyNavigation).ThenInclude(counterparty => counterparty.TypeAssignments)
            .Where(x => x.IdBankReceipt == receipt.IdBankReceipt && x.IsActive
                && (allocationIds == null || allocationIds.Contains(x.IdReceiptAllocation)))
            .OrderBy(x => x.IdReceiptAllocation).ToListAsync(ct);

        foreach (var allocation in allocations)
        {
            var invoice = allocation.IdInvoiceNavigation;
            var allocatedAmount = allocation.AllocatedAmount ?? 0m;
            if (invoice is null || InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType) || allocatedAmount <= 0m)
                continue;

            var invoiceAllocations = invoice.PanelAllocations.Where(line => line.IdInvoice == invoice.IdInvoice).ToList();
            var saved = invoiceAllocations
                .Where(line => line.IdInvoice == invoice.IdInvoice
                    && line.IdPanelNavigation.IdContract == invoice.IdContract
                    && string.Equals(line.IdPanelNavigation.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(line.IdPanelNavigation.Status?.Trim(), "Confirmed", StringComparison.OrdinalIgnoreCase)
                    && line.IdPanelNavigation.IdCounterparty == line.IdCounterparty
                    && line.IdCounterpartyNavigation.IsActive
                    && line.IdCounterpartyNavigation.TypeAssignments.Any(type =>
                        string.Equals(type.Type.Trim(), CounterpartyTypes.Reinsurer, StringComparison.OrdinalIgnoreCase))
                    && line.AllocationRatio > 0m)
                .OrderBy(line => line.IdPanel).ToList();
            if (saved.Count == 0 || saved.Count != invoiceAllocations.Count || saved.Sum(line => line.AllocationRatio) <= 0m)
                throw new OutgoingRemittanceValidationException(
                    $"Invoice {invoice.InvoiceNumber} has no saved allocation for a confirmed reinsurer panel.");

            var panelIds = saved.Select(line => line.IdPanel).ToHashSet();
            var invoiceAmount = invoice.GrossPremium ?? 0m;
            if (invoiceAmount <= 0m || allocatedAmount + invoiceAmount <= 0m)
                throw new OutgoingRemittanceValidationException(
                    $"Invoice {invoice.InvoiceNumber} has an invalid amount for remittance generation.");
            var credits = await _db.InvoiceCreditApplications.AsNoTracking()
                .Include(application => application.AvoirInvoiceNavigation).ThenInclude(avoir => avoir.PanelAllocations)
                .Where(application => application.IsActive && application.IdBankReceipt == receipt.IdBankReceipt
                    && application.TargetInvoiceId == invoice.IdInvoice)
                .ToListAsync(ct);
            var positiveAmount = allocatedAmount + credits.Sum(application => application.Amount);
            if (positiveAmount > invoiceAmount)
                throw new OutgoingRemittanceValidationException(
                    $"Paired amount for invoice {invoice.InvoiceNumber} exceeds its invoice amount.");

            var invoicePayableWeights = saved
                .Select(line => Math.Max(0m, PremiumDebitNoteAllocationCalculator.NetPayableFromPersistedAllocation(line)))
                .ToArray();
            var invoicePayableTotal = RoundMoney(
                invoicePayableWeights.Sum() * positiveAmount / invoiceAmount, precision);
            var invoiceAmounts = DistributeRemittanceNetAmount(
                invoicePayableTotal, invoicePayableWeights, precision);
            var invoiceBaseAmounts = DistributeRemittanceNetAmount(
                positiveAmount, saved.Select(line => line.NetAmount).ToArray(), precision);
            var premiumExisting = await _db.RemittanceRefs.Where(x => x.IdReceiptAllocation == allocation.IdReceiptAllocation
                    && x.IdInvoiceAllocation.HasValue && saved.Select(line => line.IdInvoiceAllocation).Contains(x.IdInvoiceAllocation.Value)
                    && x.SourceType == "Premium")
                .ToListAsync(ct);
            if (premiumExisting.Any(x => !x.IdPanel.HasValue || !panelIds.Contains(x.IdPanel.Value)))
                throw new OutgoingRemittanceValidationException("Existing Premium remittances do not match the allocation's eligible panels.");
            for (var i = 0; i < saved.Count; i++)
            {
                var distribution = saved[i];
                var panel = distribution.IdPanelNavigation;
                if (invoiceAmounts[i] == 0m) continue;
                var prior = premiumExisting.SingleOrDefault(x => x.IdPanel == panel.IdPanel
                    && x.IdInvoiceAllocation == distribution.IdInvoiceAllocation);
                if (prior is not null)
                {
                    if (prior.NetPayableAmount != invoiceAmounts[i] || prior.IdCurrency != receipt.IdCurrency || prior.IdCounterparty != panel.IdCounterparty)
                        throw new OutgoingRemittanceValidationException($"Remittance {prior.RemittanceRef1} already exists for allocation {allocation.IdReceiptAllocation}, panel {panel.IdPanel}, with a different payable amount or beneficiary.");
                    prior.SourceInvoiceId ??= invoice.IdInvoice;
                    continue;
                }
                _db.RemittanceRefs.Add(new RemittanceRef
                {
                    IdReceiptAllocation = allocation.IdReceiptAllocation, IdInvoiceAllocation = distribution.IdInvoiceAllocation,
                    SourceInvoiceId = invoice.IdInvoice, SourceType = "Premium", BeneficiaryType = "REINSURER",
                    IdPanel = panel.IdPanel, IdCounterparty = panel.IdCounterparty, IdCurrency = receipt.IdCurrency,
                    PartPct = distribution.AllocationRatio * 100m, PremiumSignedLine = invoiceBaseAmounts[i],
                    Deductions = invoiceBaseAmounts[i] - invoiceAmounts[i], NetPremiumDue = invoiceAmounts[i],
                    NetPayableAmount = invoiceAmounts[i], Status = "Draft", SignatureStatus = "Draft",
                    Description = $"Generated from invoice {invoice.InvoiceNumber} allocation {allocation.IdReceiptAllocation}",
                    CreatedAt = DateTime.UtcNow, CreatedBy = user, IsActive = true
                });
            }

            foreach (var credit in credits.GroupBy(application => application.AvoirInvoiceId)
                         .Select(group => new { Avoir = group.First().AvoirInvoiceNavigation, Amount = group.Sum(x => x.Amount) }))
            {
                var avoir = credit.Avoir;
                var allAvoirLines = avoir.PanelAllocations.Where(line => line.IdInvoice == avoir.IdInvoice).ToList();
                var avoirLines = allAvoirLines
                    .Where(line => line.IdInvoice == avoir.IdInvoice && line.IdPanelNavigation.IdContract == invoice.IdContract
                        && string.Equals(line.IdPanelNavigation.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(line.IdPanelNavigation.Status?.Trim(), "Confirmed", StringComparison.OrdinalIgnoreCase)
                        && line.IdPanelNavigation.IdCounterparty == line.IdCounterparty
                        && line.IdCounterpartyNavigation.IsActive
                        && line.IdCounterpartyNavigation.TypeAssignments.Any(type =>
                            string.Equals(type.Type.Trim(), CounterpartyTypes.Reinsurer, StringComparison.OrdinalIgnoreCase))
                        && line.AllocationRatio > 0m)
                    .OrderBy(line => line.IdPanel).ToList();
                var avoirAmount = avoir.GrossPremium ?? 0m;
                if (avoirLines.Count == 0 || avoirLines.Count != allAvoirLines.Count
                    || avoirAmount <= 0m || credit.Amount <= 0m || credit.Amount > avoirAmount)
                    throw new OutgoingRemittanceValidationException(
                        $"Avoir {avoir.InvoiceNumber} has no valid saved allocation for confirmed reinsurer panels.");

                var avoirWeights = avoirLines.Select(line => Math.Max(0m,
                    PremiumDebitNoteAllocationCalculator.NetPayableFromPersistedAllocation(line))).ToArray();
                if (avoirWeights.Sum() <= 0m)
                    throw new OutgoingRemittanceValidationException($"Avoir {avoir.InvoiceNumber} has no positive panel net-payable amount.");
                var avoirTotal = RoundMoney(avoirWeights.Sum() * credit.Amount / avoirAmount, precision);
                var avoirAmounts = DistributeRemittanceNetAmount(avoirTotal, avoirWeights, precision);
                var existingAvoir = await _db.RemittanceRefs.Where(x => x.IdReceiptAllocation == allocation.IdReceiptAllocation
                        && x.SourceInvoiceId == avoir.IdInvoice && x.SourceType == "AVOIR"
                        && x.IdInvoiceAllocation.HasValue && avoirLines.Select(line => line.IdInvoiceAllocation).Contains(x.IdInvoiceAllocation.Value))
                    .ToListAsync(ct);
                foreach (var (line, index) in avoirLines.Select((line, index) => (line, index)))
                {
                    if (line.IdPanelNavigation.IdContract != invoice.IdContract || !panelIds.Contains(line.IdPanel))
                        throw new OutgoingRemittanceValidationException($"Avoir {avoir.InvoiceNumber} allocation panel does not belong to the paired contract.");
                    if (avoirAmounts[index] == 0m) continue;
                    var panel = line.IdPanelNavigation;
                    var prior = existingAvoir.SingleOrDefault(x => x.IdPanel == panel.IdPanel && x.IdInvoiceAllocation == line.IdInvoiceAllocation);
                    if (prior is not null)
                    {
                        if (prior.IdCurrency != receipt.IdCurrency || prior.IdCounterparty != panel.IdCounterparty
                            || prior.NetPayableAmount != avoirAmounts[index])
                            throw new OutgoingRemittanceValidationException($"Avoir remittance {prior.RemittanceRef1} already exists with different allocation data.");
                        continue;
                    }
                    _db.RemittanceRefs.Add(new RemittanceRef
                    {
                        IdReceiptAllocation = allocation.IdReceiptAllocation, IdInvoiceAllocation = line.IdInvoiceAllocation,
                        SourceInvoiceId = avoir.IdInvoice, SourceType = "AVOIR", BeneficiaryType = "REINSURER",
                        IdPanel = panel.IdPanel, IdCounterparty = panel.IdCounterparty, IdCurrency = receipt.IdCurrency,
                        PartPct = line.AllocationRatio * 100m,
                        PremiumSignedLine = RoundMoney(line.GrossPremium * credit.Amount / avoirAmount, precision),
                        Deductions = RoundMoney((line.GrossPremium - avoirAmounts[index]) * credit.Amount / avoirAmount, precision),
                        NetPremiumDue = avoirAmounts[index], NetPayableAmount = avoirAmounts[index],
                        Status = "Draft", SignatureStatus = "Draft",
                        Description = $"Generated from Avoir {avoir.InvoiceNumber} allocation {allocation.IdReceiptAllocation}",
                        CreatedAt = DateTime.UtcNow, CreatedBy = user, IsActive = true
                    });
                }
            }
        }
    }

    private static decimal[] DistributeRemittanceNetAmount(decimal total, IReadOnlyList<decimal> weights, int precision)
    {
        if (weights.Count == 0 || weights.Any(weight => weight < 0m))
            throw new OutgoingRemittanceValidationException("Saved invoice net-payable allocations must have positive weights.");
        if (total < 0m || decimal.Round(total, precision, MidpointRounding.AwayFromZero) != total)
            throw new OutgoingRemittanceValidationException("Calculated remittance amount is invalid for the currency precision.");
        if (total == 0m)
            return new decimal[weights.Count];
        if (weights.Sum() <= 0m)
            throw new OutgoingRemittanceValidationException("Saved invoice net-payable allocations must have positive weights.");

        var weightTotal = weights.Sum();
        var result = new decimal[weights.Count];
        var allocated = 0m;
        for (var index = 0; index < weights.Count; index++)
        {
            result[index] = index == weights.Count - 1
                ? total - allocated
                : decimal.Round(total * weights[index] / weightTotal, precision, MidpointRounding.AwayFromZero);
            allocated += result[index];
        }
        return result;
    }

    private static decimal RoundMoney(decimal amount, int precision) =>
        decimal.Round(amount, precision, MidpointRounding.AwayFromZero);

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
            var allocated = await _db.ReceiptAllocations.AsNoTracking()
                .Where(x => x.IsActive && x.IdBankReceipt == receiptId)
                .SumAsync(x => x.AllocatedAmount ?? 0m, ct);
            receipt.DetailsStatus = InvoiceRecoveryCalculator.GetReceiptPairingStatus(receipt.AmountReceived, allocated);
            receipt.UpdatedAt = DateTime.UtcNow;
            receipt.UpdatedBy = user;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (OutgoingRemittanceValidationException ex)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return Failure<IReadOnlyList<RemittanceResponseDto>>(ex.Message, status: 409);
        }
        var lines = await RemittanceDetails().Where(x => x.IdReceiptAllocationNavigation != null
            && x.IdReceiptAllocationNavigation.IdBankReceipt == receiptId).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<RemittanceResponseDto>>.Success(lines.Select(MapOutgoingRemittance).ToList());
    }

    public async Task<ServiceResult<AvoirRemittanceRepairResultDto>> RepairDraftAvoirRemittancesAsync(CancellationToken ct)
    {
        if (!CanBack()) return Forbidden<AvoirRemittanceRepairResultDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<AvoirRemittanceRepairResultDto>();
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockOutgoingWorkflowAsync(ct);
        var historical = await _db.RemittanceRefs.AsNoTracking()
            .Where(row => row.IsActive && row.IdPanel == null && row.SourceInvoiceId != null
                && row.SourceType != null && row.SourceType.Trim().ToUpper() == "AVOIR_REFUND"
                && row.Status != null && (row.Status.Trim().ToUpper() == "PAID" || row.Status.Trim().ToUpper() == "CLOSED"))
            .Select(row => row.RemittanceRef1).ToListAsync(ct);
        var aggregates = await _db.RemittanceRefs.AsTracking()
            .Include(row => row.SourceInvoice).ThenInclude(invoice => invoice!.PanelAllocations)
                .ThenInclude(line => line.IdPanelNavigation).ThenInclude(panel => panel.IdCounterpartyNavigation)
                    .ThenInclude(counterparty => counterparty.TypeAssignments)
            .Include(row => row.SourceInvoice).ThenInclude(invoice => invoice!.PanelAllocations)
                .ThenInclude(line => line.IdCounterpartyNavigation).ThenInclude(counterparty => counterparty.TypeAssignments)
            .Where(row => row.IsActive && row.IdPanel == null && row.SourceInvoiceId != null
                && row.SourceType != null && row.SourceType.Trim().ToUpper() == "AVOIR_REFUND"
                && (row.SourceInvoice == null || row.SourceInvoice.ProcessingMode == null
                    || row.SourceInvoice.ProcessingMode != InvoiceDocumentAccounting.DirectToRemittances)
                && row.BeneficiaryType != null && row.BeneficiaryType.Trim().ToUpper() == "CEDENT"
                && row.Status != null && row.Status.Trim().ToUpper() == "DRAFT")
            .OrderBy(row => row.RemittanceRef1).ToListAsync(ct);
        var repaired = new List<int>();
        var createdCount = 0;
        var unresolved = new List<int>();
        foreach (var aggregate in aggregates)
        {
            var avoir = aggregate.SourceInvoice;
            var savedAllocationCount = avoir is null ? 0
                : await _db.InvoicePanelAllocations.AsNoTracking().CountAsync(line => line.IdInvoice == avoir.IdInvoice, ct);
            var eligible = avoir?.PanelAllocations.Where(line => line.IdInvoice == avoir.IdInvoice
                    && line.IdPanelNavigation.IdContract == avoir.IdContract
                    && string.Equals(line.IdPanelNavigation.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(line.IdPanelNavigation.Status?.Trim(), ContractConstants.ConfirmedPanelStatus, StringComparison.OrdinalIgnoreCase)
                    && line.IdPanelNavigation.IdCounterparty == line.IdCounterparty
                    && line.IdCounterpartyNavigation.IsActive
                    && line.IdCounterpartyNavigation.TypeAssignments.Any(type =>
                        string.Equals(type.Type.Trim(), CounterpartyTypes.Reinsurer, StringComparison.OrdinalIgnoreCase))
                    && line.AllocationRatio > 0m)
                .OrderBy(line => line.IdPanel).ToList() ?? [];
            if (avoir is null || !InvoiceDocumentAccounting.IsAvoir(avoir.InvoiceType)
                || eligible.Count == 0 || eligible.Count != savedAllocationCount)
            {
                unresolved.Add(aggregate.RemittanceRef1);
                continue;
            }

            var existing = await _db.RemittanceRefs.Where(row => row.SourceInvoiceId == avoir.IdInvoice
                    && row.SourceType == "AVOIR" && row.IdInvoiceAllocation.HasValue
                    && row.IdReceiptAllocation == aggregate.IdReceiptAllocation
                    && eligible.Select(line => line.IdInvoiceAllocation).Contains(row.IdInvoiceAllocation.Value)
                    && row.IdPanel.HasValue)
                .ToListAsync(ct);
            var generated = new List<RemittanceRef>();
            foreach (var line in eligible)
            {
                var payable = Math.Abs(PremiumDebitNoteAllocationCalculator.NetPayableFromPersistedAllocation(line));
                if (payable <= 0m)
                {
                    unresolved.Add(aggregate.RemittanceRef1);
                    generated.Clear();
                    break;
                }
                var prior = existing.SingleOrDefault(row => row.IdPanel == line.IdPanel
                    && row.IdInvoiceAllocation == line.IdInvoiceAllocation);
                if (prior is not null)
                {
                    generated.Add(prior);
                    continue;
                }
                var panel = line.IdPanelNavigation;
                var replacement = new RemittanceRef
                {
                    SourceType = "AVOIR", SourceInvoiceId = avoir.IdInvoice, IdInvoiceAllocation = line.IdInvoiceAllocation,
                    BeneficiaryType = "REINSURER", IdPanel = panel.IdPanel, IdCounterparty = panel.IdCounterparty,
                    IdCurrency = aggregate.IdCurrency, PartPct = line.AllocationRatio * 100m,
                    PremiumSignedLine = Math.Abs(line.GrossPremium),
                    Deductions = Math.Abs(line.GrossPremium - payable), NetPremiumDue = payable, NetPayableAmount = payable,
                    Description = $"Panel allocation replacing aggregate Avoir remittance {aggregate.RemittanceRef1}",
                    Status = "Draft", SignatureStatus = "Draft", CreatedAt = DateTime.UtcNow, CreatedBy = user, IsActive = true
                };
                _db.RemittanceRefs.Add(replacement);
                generated.Add(replacement);
                createdCount++;
            }
            if (generated.Count != eligible.Count) continue;
            aggregate.Status = "Cancelled";
            aggregate.UpdatedAt = DateTime.UtcNow;
            aggregate.UpdatedBy = user;
            aggregate.Description = $"Superseded by panel remittances; legacy aggregate {aggregate.RemittanceRef1} retained for audit.";
            repaired.Add(aggregate.RemittanceRef1);
            await _db.SaveChangesAsync(ct);
            var replacementIds = generated.Select(row => row.RemittanceRef1).ToArray();
            var settlement = await _db.AvoirRefundSettlements
                .Where(row => row.IsActive && row.RemittanceId == aggregate.RemittanceRef1)
                .OrderBy(row => row.CreatedAt).FirstOrDefaultAsync(ct);
            if (settlement is not null && replacementIds.Length > 0)
                settlement.RemittanceId = replacementIds[0];
        }
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ServiceResult<AvoirRemittanceRepairResultDto>.Success(new AvoirRemittanceRepairResultDto
        {
            DraftAggregatesSuperseded = repaired.Count,
            PanelRemittancesCreated = createdCount,
            SupersededRemittanceIds = repaired,
            UnrepairableDraftRemittanceIds = unresolved.Distinct().ToArray(),
            PaidOrClosedLegacyRemittanceIds = historical
        });
    }

    private IQueryable<RemittanceRef> RemittanceDetails() => _db.RemittanceRefs.AsNoTrackingWithIdentityResolution()
        .Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation)
        .Include(x => x.IdPanelNavigation).ThenInclude(x => x.IdContractNavigation).ThenInclude(contract => contract.IdCounterpartyNavigation)
        .Include(x => x.IdPanelNavigation).ThenInclude(panel => panel.IdContractNavigation)
            .ThenInclude(contract => contract.ContractCoverageSelections).ThenInclude(selection => selection.IdCoverageItemNavigation)
        .Include(x => x.SourceInvoice).ThenInclude(x => x!.IdContractNavigation).ThenInclude(contract => contract.IdCounterpartyNavigation)
        .Include(x => x.SourceInvoice).ThenInclude(x => x!.IdContractNavigation)
            .ThenInclude(contract => contract.ContractCoverageSelections).ThenInclude(selection => selection.IdCoverageItemNavigation)
        .Include(x => x.AvoirRefundSettlements).ThenInclude(x => x.BankReceipt).ThenInclude(receipt => receipt.ReceiptAllocations)
        .Include(x => x.IdReceiptAllocationNavigation).ThenInclude(x => x!.IdInvoiceNavigation)
        .Include(x => x.IdReceiptAllocationNavigation).ThenInclude(x => x!.IdBankReceiptNavigation)
            .ThenInclude(x => x.ReceiptAllocations)
        .Include(x => x.ClosingLines).ThenInclude(x => x.Closing).AsSplitQuery();

    public async Task<ServiceResult<PagedResponseDto<RemittanceResponseDto>>> ListRemittancesAsync(RemittanceQueryDto query, CancellationToken ct)
    {
        if (!CanFinanceOrBack()) return Forbidden<PagedResponseDto<RemittanceResponseDto>>();
        if (query.PageNumber < 1) return Failure<PagedResponseDto<RemittanceResponseDto>>("pageNumber must be at least 1.", status: StatusCodes.Status400BadRequest);
        if (query.PageSize is < 1 or > 100) return Failure<PagedResponseDto<RemittanceResponseDto>>("pageSize must be between 1 and 100.", status: StatusCodes.Status400BadRequest);
        if (query.DateFrom > query.DateTo) return Failure<PagedResponseDto<RemittanceResponseDto>>("Creation date range is invalid.");
        var sortBy = Optional(query.SortBy)?.ToLowerInvariant() ?? "createdat";
        if (sortBy is not ("createdat" or "reference" or "remittancereference" or "beneficiaryname" or "reinsurer" or "reinsurername" or "contract" or "contractname" or "umr" or "amount" or "netpayableamount" or "status" or "currency"))
            return Failure<PagedResponseDto<RemittanceResponseDto>>($"Unsupported sortBy value '{query.SortBy}'.", status: StatusCodes.Status400BadRequest);
        if (Optional(query.SortDirection) is { } sortDirection && !sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase) && !sortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase))
            return Failure<PagedResponseDto<RemittanceResponseDto>>("sortDirection must be 'asc' or 'desc'.", status: StatusCodes.Status400BadRequest);

        var rows = RemittanceDetails().Where(x => x.IsActive
            && (x.IdPanelNavigation == null
                || (x.IdPanelNavigation.Role != null
                    && x.IdPanelNavigation.Role.Trim().ToUpper() == "REINSURER"
                    && x.IdPanelNavigation.IdCounterparty == x.IdCounterparty)));
        var term = Optional(query.Search);
        if (term is not null) rows = rows.Where(x => x.RemittanceRef1.ToString().Contains(term)
            || ("REM-" + x.RemittanceRef1.ToString()).Contains(term)
            || (x.IdCounterpartyNavigation != null && x.IdCounterpartyNavigation.Name.Contains(term))
            || (x.IdPanelNavigation != null && x.IdPanelNavigation.IdContractNavigation != null
                && (x.IdPanelNavigation.IdContractNavigation.Name.Contains(term) || x.IdPanelNavigation.IdContractNavigation.Umr.Contains(term) || x.IdPanelNavigation.PremiumReference.Contains(term)
                    || (x.IdPanelNavigation.IdContractNavigation.IdCounterpartyNavigation != null && x.IdPanelNavigation.IdContractNavigation.IdCounterpartyNavigation.Name.Contains(term))))
            || (x.SourceInvoice != null && (x.SourceInvoice.InvoiceNumber.Contains(term)
                || (x.SourceInvoice.IdContractNavigation != null && (x.SourceInvoice.IdContractNavigation.Name.Contains(term) || x.SourceInvoice.IdContractNavigation.Umr.Contains(term)))))
            || (x.IdReceiptAllocationNavigation != null && x.IdReceiptAllocationNavigation.IdInvoiceNavigation != null && x.IdReceiptAllocationNavigation.IdInvoiceNavigation.InvoiceNumber.Contains(term))
            || (x.IdReceiptAllocationNavigation != null && x.IdReceiptAllocationNavigation.IdBankReceiptNavigation != null
                && ((x.IdReceiptAllocationNavigation.IdBankReceiptNavigation.BankReference != null && x.IdReceiptAllocationNavigation.IdBankReceiptNavigation.BankReference.Contains(term))
                    || (x.IdReceiptAllocationNavigation.IdBankReceiptNavigation.BusinessReference != null && x.IdReceiptAllocationNavigation.IdBankReceiptNavigation.BusinessReference.Contains(term))))
            || x.AvoirRefundSettlements.Any(settlement => settlement.IsActive && settlement.BankReceipt != null
                && ((settlement.BankReceipt.BankReference != null && settlement.BankReceipt.BankReference.Contains(term))
                    || (settlement.BankReceipt.BusinessReference != null && settlement.BankReceipt.BusinessReference.Contains(term)))));
        if (query.ReinsurerId.HasValue) rows = rows.Where(x => x.IdPanelNavigation != null && x.IdPanelNavigation.IdCounterparty == query.ReinsurerId);
        if (Optional(query.Reinsurer) is { } reinsurer) rows = rows.Where(x => x.IdPanelNavigation != null
            && x.IdPanelNavigation.IdCounterpartyNavigation.Name.Contains(reinsurer));
        if (query.ContractId.HasValue) rows = rows.Where(x => x.IdPanelNavigation != null && x.IdPanelNavigation.IdContract == query.ContractId
            || x.SourceInvoice != null && x.SourceInvoice.IdContract == query.ContractId);
        if (Optional(query.Contract) is { } contract) rows = rows.Where(x =>
            (x.IdPanelNavigation != null && x.IdPanelNavigation.IdContractNavigation != null
                && (x.IdPanelNavigation.IdContractNavigation.Name.Contains(contract) || x.IdPanelNavigation.IdContractNavigation.Umr.Contains(contract)))
            || (x.SourceInvoice != null && x.SourceInvoice.IdContractNavigation != null
                && (x.SourceInvoice.IdContractNavigation.Name.Contains(contract) || x.SourceInvoice.IdContractNavigation.Umr.Contains(contract))));
        if (Optional(query.Umr) is { } umr) rows = rows.Where(x =>
            (x.IdPanelNavigation != null && x.IdPanelNavigation.IdContractNavigation != null && x.IdPanelNavigation.IdContractNavigation.Umr.Contains(umr))
            || (x.SourceInvoice != null && x.SourceInvoice.IdContractNavigation != null && x.SourceInvoice.IdContractNavigation.Umr.Contains(umr)));
        if (Optional(query.Status) is { } status) rows = rows.Where(x => x.Status == status);
        if (query.CurrencyId.HasValue) rows = rows.Where(x => x.IdCurrency == query.CurrencyId);
        if (Optional(query.Currency) is { } currency) rows = rows.Where(x => x.IdCurrencyNavigation.CurrencyCode == currency);
        if (query.DateFrom.HasValue) { var from = query.DateFrom.Value.ToDateTime(TimeOnly.MinValue); rows = rows.Where(x => x.CreatedAt >= from); }
        if (query.DateTo.HasValue) { var until = query.DateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue); rows = rows.Where(x => x.CreatedAt < until); }
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<RemittanceRef> ordered = sortBy switch
        {
            "createdat" => descending ? rows.OrderByDescending(x => x.CreatedAt) : rows.OrderBy(x => x.CreatedAt),
            "reference" or "remittancereference" => descending ? rows.OrderByDescending(x => x.RemittanceRef1) : rows.OrderBy(x => x.RemittanceRef1),
            "beneficiaryname" => descending ? rows.OrderByDescending(x => x.IdCounterpartyNavigation.Name) : rows.OrderBy(x => x.IdCounterpartyNavigation.Name),
            "reinsurer" or "reinsurername" => descending
                ? rows.OrderByDescending(x => x.IdPanelNavigation != null ? x.IdPanelNavigation.IdCounterpartyNavigation.Name : null)
                : rows.OrderBy(x => x.IdPanelNavigation != null ? x.IdPanelNavigation.IdCounterpartyNavigation.Name : null),
            "contract" or "contractname" => descending
                ? rows.OrderByDescending(x => x.IdPanelNavigation != null ? x.IdPanelNavigation.IdContractNavigation.Name : x.SourceInvoice != null ? x.SourceInvoice.IdContractNavigation.Name : null)
                : rows.OrderBy(x => x.IdPanelNavigation != null ? x.IdPanelNavigation.IdContractNavigation.Name : x.SourceInvoice != null ? x.SourceInvoice.IdContractNavigation.Name : null),
            "umr" => descending
                ? rows.OrderByDescending(x => x.IdPanelNavigation != null ? x.IdPanelNavigation.IdContractNavigation.Umr : x.SourceInvoice != null ? x.SourceInvoice.IdContractNavigation.Umr : null)
                : rows.OrderBy(x => x.IdPanelNavigation != null ? x.IdPanelNavigation.IdContractNavigation.Umr : x.SourceInvoice != null ? x.SourceInvoice.IdContractNavigation.Umr : null),
            "amount" or "netpayableamount" => descending ? rows.OrderByDescending(x => x.NetPayableAmount) : rows.OrderBy(x => x.NetPayableAmount),
            "status" => descending ? rows.OrderByDescending(x => x.Status) : rows.OrderBy(x => x.Status),
            "currency" => descending ? rows.OrderByDescending(x => x.IdCurrencyNavigation.CurrencyCode) : rows.OrderBy(x => x.IdCurrencyNavigation.CurrencyCode),
            _ => descending ? rows.OrderByDescending(x => x.CreatedAt) : rows.OrderBy(x => x.CreatedAt)
        };
        var page = query.PageNumber;
        var size = query.PageSize;
        var pagedQuery = ordered.ThenBy(x => x.RemittanceRef1).Skip((page - 1) * size).Take(size);
        var traceId = _http.HttpContext?.TraceIdentifier ?? "unavailable";
        string? generatedSql = null;
        try
        {
            generatedSql = pagedQuery.ToQueryString();
            _logger.LogDebug("Back Officer remittance list SQL. TraceId={TraceId} SortBy={SortBy} Page={Page} Size={Size} Sql={Sql}",
                traceId, sortBy, page, size, generatedSql);
            var total = await rows.CountAsync(ct);
            var items = await pagedQuery.ToListAsync(ct);
            foreach (var sourceType in items.Select(x => x.SourceType).Where(type => type is not null)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Where(type => !KnownRemittanceSourceTypes.Contains(type!, StringComparer.OrdinalIgnoreCase)))
                _logger.LogWarning("Remittance list contains unsupported legacy SourceType={SourceType} TraceId={TraceId}", sourceType, traceId);
            return ServiceResult<PagedResponseDto<RemittanceResponseDto>>.Success(new()
            {
                Items = items.Select(MapOutgoingRemittance).ToList(), TotalCount = total, PageNumber = page, PageSize = size
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to list remittances. TraceId={TraceId} SortBy={SortBy} Page={Page} Size={Size} Sql={Sql} InnerException={InnerException}",
                traceId, sortBy, page, size, generatedSql, exception.InnerException?.ToString());
            throw;
        }
    }

    private static readonly string[] KnownRemittanceSourceTypes = ["Premium", "Claim", "AVOIR", "AVOIR_REFUND"];

    private static decimal RemainingPayable(RemittanceRef x) => x.Status is "Closed" or "Paid" ? 0m : x.NetPayableAmount ?? 0m;

    private static decimal SignedRemittanceAmount(string? sourceType, decimal? amount) =>
        string.Equals(sourceType?.Trim(), "AVOIR", StringComparison.OrdinalIgnoreCase)
            ? -Math.Abs(amount ?? 0m) : Math.Abs(amount ?? 0m);

    private static decimal SignedRemittanceAmount(RemittanceRef remittance) =>
        SignedRemittanceAmount(remittance.SourceType, remittance.NetPayableAmount);

    private static decimal SignedRemainingPayable(RemittanceRef x) =>
        SignedRemittanceAmount(x.SourceType, RemainingPayable(x));

    private static bool ClosingEligible(RemittanceRef x) => x.IsActive && x.IdPanelNavigation is not null
        && x.IdPanelNavigation.IdCounterparty == x.IdCounterparty
        && string.Equals(x.IdPanelNavigation.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase)
        && (x.SourceInvoice == null || x.SourceInvoice.IdContract == x.IdPanelNavigation.IdContract)
        && (string.Equals(x.IdPanelNavigation.Status?.Trim(), "Confirmed", StringComparison.OrdinalIgnoreCase) || string.Equals(x.IdPanelNavigation.Status?.Trim(), "Bound", StringComparison.OrdinalIgnoreCase))
        && x.IdCounterpartyNavigation is { IsActive: true } && x.IdCurrencyNavigation is { IsActive: true }
        && x.IdDisbursement is null && (x.Status is "Draft" or "ReadyForDisbursement" or "AwaitingFinanceDisbursement")
        && RemainingPayable(x) > 0m && !x.ClosingIssuedDate.HasValue
        && !x.ClosingLines.Any(l => l.IsActive && l.Closing != null && l.Closing.IsActive);

    private static RemittanceResponseDto MapOutgoingRemittance(RemittanceRef x)
    {
        var dto = ToRemittance(x);
        dto.RemittanceId = x.RemittanceRef1;
        dto.RemittanceReference = $"REM-{x.RemittanceRef1}";
        dto.ReinsurerId = x.IdPanelNavigation?.IdCounterparty;
        dto.ReinsurerName = x.IdPanelNavigation?.IdCounterpartyNavigation?.Name;
        dto.DataIntegrityWarning = x.IdPanelNavigation is null
            ? "Remittance has no panel and requires data repair."
            : null;
        dto.CedentId = x.IdPanelNavigation?.IdContractNavigation?.IdCounterparty
            ?? x.SourceInvoice?.IdContractNavigation?.IdCounterparty;
        dto.CedentName = x.IdPanelNavigation?.IdContractNavigation?.IdCounterpartyNavigation?.Name
            ?? x.SourceInvoice?.IdContractNavigation?.IdCounterpartyNavigation?.Name;
        dto.ReceiptReference = x.IdReceiptAllocationNavigation?.IdBankReceiptNavigation?.BusinessReference
            ?? x.IdReceiptAllocationNavigation?.IdBankReceiptNavigation?.BankReference;
        var settlementReceipt = x.AvoirRefundSettlements.FirstOrDefault(settlement => settlement.IsActive)?.BankReceipt;
        dto.ReceiptReference ??= settlementReceipt?.BusinessReference ?? settlementReceipt?.BankReference;
        dto.BankReceiptId = x.IdReceiptAllocationNavigation?.IdBankReceipt;
        dto.BankReceiptId ??= settlementReceipt?.IdBankReceipt;
        var sourceDocument = x.SourceInvoice ?? x.IdReceiptAllocationNavigation?.IdInvoiceNavigation;
        dto.InvoiceId = x.SourceInvoiceId ?? x.IdReceiptAllocationNavigation?.IdInvoice;
        dto.InvoiceNumber = x.IdReceiptAllocationNavigation?.IdInvoiceNavigation?.InvoiceNumber
            ?? x.SourceInvoice?.InvoiceNumber;
        dto.InvoiceLabel = x.IdReceiptAllocationNavigation?.IdInvoiceNavigation?.Label
            ?? x.SourceInvoice?.Label;
        dto.InvoiceAvoirNumber = sourceDocument?.InvoiceNumber;
        dto.Label = sourceDocument?.Label;
        dto.SourceType = x.SourceType;
        dto.SourceInvoiceId = x.SourceInvoiceId;
        dto.SourceInvoiceNumber = x.SourceInvoice?.InvoiceNumber;
        dto.SourceDocumentId = x.SourceInvoiceId ?? x.IdReceiptAllocationNavigation?.IdInvoice;
        dto.SourceDocumentNumber = dto.InvoiceAvoirNumber;
        dto.Amount = SignedRemittanceAmount(x);
        dto.SignedNetPayableAmount = SignedRemittanceAmount(x);
        dto.AllocationPercentage = x.PartPct;
        dto.Currency = x.IdCurrencyNavigation?.CurrencyCode ?? string.Empty;
        dto.BeneficiaryType = x.IdPanel is not null ? "REINSURER"
            : string.IsNullOrWhiteSpace(x.BeneficiaryType) ? "CEDENT" : x.BeneficiaryType.Trim().ToUpperInvariant();
        dto.BeneficiaryName = x.IdCounterpartyNavigation?.Name ?? string.Empty;
        dto.BeneficiaryId = x.IdCounterparty;
        dto.BeneficiaryCounterpartyId = x.IdCounterparty;
        if (x.IdPanel is null && x.SourceInvoice?.IdContractNavigation is { } sourceContract)
        {
            dto.ContractId = sourceContract.IdContract;
            dto.ContractName = sourceContract.Name;
            dto.Umr = sourceContract.Umr;
            dto.CedentName = x.IdCounterpartyNavigation?.Name;
            dto.InvoiceId = x.SourceInvoiceId;
            dto.InvoiceNumber = x.SourceInvoice.InvoiceNumber;
            dto.ReceiptReference = settlementReceipt?.BusinessReference ?? settlementReceipt?.BankReference;
            dto.BankReceiptId = settlementReceipt?.IdBankReceipt;
        }
        dto.PanelName = x.IdPanelNavigation?.PremiumReference ?? x.IdPanelNavigation?.IdCounterpartyNavigation?.Name ?? x.IdCounterpartyNavigation?.Name;
        dto.CreatedAt = x.CreatedAt;
        dto.ClosingIssuedDate = x.ClosingIssuedDate;
        var closing = x.ClosingLines.FirstOrDefault(l => l.IsActive && l.Closing?.IsActive == true)?.Closing;
        dto.ClosingId = closing?.Id;
        dto.ClosingReference = closing?.Reference;
        dto.ClosingEligible = ClosingEligible(x);
        dto.RemainingPayableAmount = SignedRemainingPayable(x);
        return dto;
    }

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetRemittanceClosingPdfAsync(int remittanceRef, CancellationToken ct)
    {
        if (!CanBack()) return Forbidden<PrintablePaymentDocumentDto>();

        var remittance = await RemittanceDetails()
            .FirstOrDefaultAsync(row => row.RemittanceRef1 == remittanceRef && row.IsActive, ct);
        if (remittance is null)
            return NotFound<PrintablePaymentDocumentDto>("Remittance was not found.", "REMITTANCE_NOT_FOUND");

        if (!IsClosingAvoir(remittance.SourceType)
            && !string.Equals(remittance.SourceType?.Trim(), "PREMIUM", StringComparison.OrdinalIgnoreCase))
            return Failure<PrintablePaymentDocumentDto>(
                "A Closing PDF is available only for Premium and Avoir remittances.",
                status: StatusCodes.Status409Conflict, code: "REMITTANCE_SOURCE_NOT_SUPPORTED");

        var panel = remittance.IdPanelNavigation;
        var contract = panel?.IdContractNavigation ?? remittance.SourceInvoice?.IdContractNavigation;
        var reinsurer = remittance.IdCounterpartyNavigation;
        var cedent = contract?.IdCounterpartyNavigation;
        var currency = remittance.IdCurrencyNavigation;
        if (panel is null || contract is null || reinsurer is null || cedent is null || currency is null)
            return Failure<PrintablePaymentDocumentDto>(
                "The remittance is missing its panel, contract, cedent, reinsurer or currency information.",
                status: StatusCodes.Status409Conflict, code: "REMITTANCE_CLOSING_DATA_INCOMPLETE");
        if (panel.IdContract != contract.IdContract || panel.IdCounterparty != reinsurer.Id)
            return Failure<PrintablePaymentDocumentDto>(
                "The remittance panel does not match its contract and reinsurer.",
                status: StatusCodes.Status409Conflict, code: "REMITTANCE_PANEL_MISMATCH");

        var mainCoverage = contract.ContractCoverageSelections
            .Where(selection => selection.IdCoverageItemNavigation is not null
                && CoverageConstants.IsMainCoverLevel(selection.IdCoverageItemNavigation.CoverageLevel))
            .OrderByDescending(selection => selection.IsPrimary)
            .ThenBy(selection => selection.IdCoverageItem)
            .Select(selection => selection.IdCoverageItemNavigation!.CoverageName)
            .FirstOrDefault();
        var description = BuildClosingDescription(contract, mainCoverage);
        var precision = PanelRemittanceCalculator.Precision(currency.DecimalPlaces);
        var issueDate = remittance.ClosingIssuedDate
            ?? DateOnly.FromDateTime(remittance.ConfirmedAt ?? remittance.CreatedAt);
        var content = BuildSingleRemittanceClosingPdf(remittance, panel, contract, cedent, reinsurer,
            currency, mainCoverage, description, issueDate, precision, remittance.NetPayableAmount ?? remittance.NetPremiumDue ?? 0m);
        var reference = $"REM-{remittance.RemittanceRef1}";

        return ServiceResult<PrintablePaymentDocumentDto>.Success(new PrintablePaymentDocumentDto
        {
            Content = content,
            ContentType = "application/pdf",
            FileName = $"Closing_{reference}.pdf",
            DocumentReference = reference,
            DocumentType = IsClosingAvoir(remittance.SourceType) ? "AVOIR_CLOSING" : "PREMIUM_CLOSING"
        });
    }

    private byte[] BuildSingleRemittanceClosingPdf(RemittanceRef remittance, ContractPanel panel, Contract contract,
        Counterparty cedent, Counterparty reinsurer, Currency currency, string? coverage, string description,
        DateOnly issueDate, int precision, decimal payableAmount)
    {
        var logoBytes = LoadIrisInvoiceLogo();
        var logo = logoBytes is null ? null : DecodePngForPdf(logoBytes);
        var drawing = new StringBuilder();
        if (logo is not null)
        {
            const double logoWidth = 155d;
            var logoHeight = logoWidth * logo.Height / logo.Width;
            drawing.Append("q ").Append(logoWidth.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" 0 0 ").Append(logoHeight.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" 48 756 cm /Logo Do Q\n");
        }

        AddText(drawing, _invoiceBranding.CompanyName, 340, 800, 10, bold: true);
        AddText(drawing, _invoiceBranding.AddressLine1, 340, 784, 8);
        AddText(drawing, _invoiceBranding.AddressLine2, 340, 772, 8);
        AddText(drawing, $"Tel. {_invoiceBranding.Phone}", 340, 760, 8);
        AddText(drawing, "ATTENTION:", 48, 710, 9, bold: true);
        AddText(drawing, reinsurer.Name, 112, 710, 9, bold: true);
        var dateLabel = $"DATE: {issueDate:dd/MM/yyyy}";
        var dateX = 547 - (int)Math.Ceiling(dateLabel.Length * 8 * 0.52);
        AddText(drawing, dateLabel, dateX, 710, 8, bold: true);
        AddCenteredText(drawing, "REINSURANCE PREMIUM CLOSING", 675, 14, bold: true);

        var isAvoir = IsClosingAvoir(remittance.SourceType);
        var netPayable = payableAmount;
        var signedLine = remittance.PremiumSignedLine ?? 0m;
        var deductions = remittance.Deductions ?? 0m;
        if (isAvoir)
        {
            netPayable = Math.Abs(netPayable);
            signedLine = Math.Abs(signedLine);
            deductions = Math.Abs(deductions);
        }

        var share = panel.SignedSharePct ?? remittance.PartPct;
        var fields = new (string Label, string Value)[]
        {
            ("COVERAGE TYPE:", coverage ?? string.Empty),
            ("ORIGINAL INSURED:", contract.Name ?? string.Empty),
            ("REINSURED:", cedent.Name ?? string.Empty),
            ("EFFECTIVE PERIOD:", FormatPeriod(contract)),
            ("YOUR SHARE:", share.HasValue ? $"{share.Value:0.########}%" : string.Empty),
            ("CURRENCY:", currency.CurrencyCode ?? string.Empty),
            ("PREMIUM FOR SIGNED LINE:", $"{FormatAmount(signedLine, precision)} {currency.CurrencyCode}"),
            ("DEDUCTIONS:", $"{FormatAmount(deductions, precision)} {currency.CurrencyCode}"),
            (isAvoir ? "NET PREMIUM DUE FROM YOU:" : "NET PREMIUM DUE TO YOU:", $"{FormatAmount(netPayable, precision)} {currency.CurrencyCode}"),
            ("DESCRIPTION:", description)
        };

        var y = 620;
        foreach (var (label, value) in fields)
        {
            AddText(drawing, label, 60, y, 8, bold: true);
            if (label == "DESCRIPTION:")
                AddWrappedText(drawing, value, 250, y, 8, 56, 12);
            else
                AddText(drawing, value, 250, y, 8);
            y -= label == "DESCRIPTION:" ? 40 : 30;
        }

        AddPartnershipLine(drawing, 360, 108);
        AddRule(drawing, 48, 91, 499, 0.6m);
        AddText(drawing, JoinNonEmpty(_invoiceBranding.CompanyName, _invoiceBranding.AddressLine1,
            _invoiceBranding.AddressLine2), 48, 74, 7);
        AddText(drawing, "1", 535, 74, 7);

        return AssembleClosingAdvicePdf([Latin1.GetBytes(drawing.ToString())], logo);
    }

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> CreateOutgoingClosingAsync(OutgoingClosingRequestDto request, CancellationToken ct)
    {
        if (!CanBack()) return Forbidden<PrintablePaymentDocumentDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<PrintablePaymentDocumentDto>();
        var ids = request.RemittanceIds;
        if (ids is null || ids.Count != 1 || ids.Any(x => x <= 0) || ids.Distinct().Count() != ids.Count)
            return Failure<PrintablePaymentDocumentDto>("A Closing document must be generated for exactly one remittance ID.", status: StatusCodes.Status400BadRequest, code: "CLOSING_REQUIRES_ONE_REMITTANCE");
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockOutgoingWorkflowAsync(ct);
        var lines = await RemittanceDetails().AsTracking().Where(x => ids.Contains(x.RemittanceRef1)).OrderBy(x => x.RemittanceRef1).ToListAsync(ct);
        if (lines.Count != ids.Count || lines.Any(x => !x.IsActive)) return Failure<PrintablePaymentDocumentDto>("All selected remittances must exist and be active.");
        if (lines.Select(x => x.IdCounterparty).Distinct().Count() != 1) return Failure<PrintablePaymentDocumentDto>("Selected remittances must belong to the same reinsurer.");
        if (lines.Select(x => x.IdCurrency).Distinct().Count() != 1) return Failure<PrintablePaymentDocumentDto>("Selected remittances must use the same currency.");
        if (lines.Any(x => !ClosingEligible(x))) return Failure<PrintablePaymentDocumentDto>("One or more selected remittances are ineligible, already closed, or have no remaining payable amount.");
        var first = lines[0];
        var currency = first.IdCurrencyNavigation;
        var precision = PanelRemittanceCalculator.Precision(currency.DecimalPlaces);
        var signedAmounts = lines.Select(line => RoundMoney(SignedRemainingPayable(line), precision)).ToArray();
        var signedTotal = RoundMoney(lines.Sum(SignedRemainingPayable), precision);
        signedAmounts[^1] = RoundMoney(signedAmounts[^1] + signedTotal - signedAmounts.Sum(), precision);
        if (signedTotal <= 0m)
            return Failure<PrintablePaymentDocumentDto>("The selected reinsurer Premium/Avoir lines have no positive net amount payable.", status: StatusCodes.Status409Conflict);
        var now = DateTime.UtcNow;
        var reinsurer = first.IdCounterpartyNavigation;
        var closing = new OutgoingClosing
        {
            Reference = $"CL-{now:yyyyMMdd}-{Guid.NewGuid():N}", IssuedAt = now, CreatedBy = user,
            IdCounterparty = first.IdCounterparty, IdCurrency = first.IdCurrency,
            TotalAmount = signedTotal
        };
        closing.TotalInWords = AmountInWords(closing.TotalAmount, currency.CurrencyCode, precision);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            closing.Lines.Add(new OutgoingClosingLine { Remittance = line, PayableAmount = signedAmounts[index] });
            line.ClosingIssuedDate = DateOnly.FromDateTime(now);
            line.Status = "AwaitingFinanceDisbursement";
            line.ConfirmedAt ??= now;
            line.ConfirmedBy ??= user;
            line.UpdatedAt = now;
            line.UpdatedBy = user;
        }
        var singleLine = lines[0];
        var contract = singleLine.IdPanelNavigation?.IdContractNavigation;
        var cedent = contract?.IdCounterpartyNavigation;
        if (contract is null || cedent is null)
            return Failure<PrintablePaymentDocumentDto>("The remittance is missing its contract or cedent information.", status: StatusCodes.Status409Conflict, code: "REMITTANCE_CLOSING_DATA_INCOMPLETE");
        var coverage = contract.ContractCoverageSelections
            .Where(selection => selection.IdCoverageItemNavigation is not null && CoverageConstants.IsMainCoverLevel(selection.IdCoverageItemNavigation.CoverageLevel))
            .OrderByDescending(selection => selection.IsPrimary).ThenBy(selection => selection.IdCoverageItem)
            .Select(selection => selection.IdCoverageItemNavigation!.CoverageName).FirstOrDefault();
        var description = BuildClosingDescription(contract, coverage);
        closing.PdfContent = BuildSingleRemittanceClosingPdf(singleLine, singleLine.IdPanelNavigation!, contract,
            cedent, reinsurer, currency, coverage, description, DateOnly.FromDateTime(now), precision, signedAmounts[0]);
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
        if (closing is null) return NotFound<PrintablePaymentDocumentDto>("Closing was not found.");
        var remittanceIds = await _db.OutgoingClosingLines.AsNoTracking()
            .Where(line => line.ClosingId == closingId && line.IsActive)
            .Select(line => line.RemittanceId).ToArrayAsync(ct);
        if (remittanceIds.Length != 1)
            return Failure<PrintablePaymentDocumentDto>("This finance closing has no single-remittance PDF.", status: StatusCodes.Status409Conflict, code: "CLOSING_PDF_NOT_SINGLE_REMITTANCE");
        var remittanceId = remittanceIds[0];
        var remittance = await RemittanceDetails().FirstOrDefaultAsync(row => row.RemittanceRef1 == remittanceId && row.IsActive, ct);
        if (remittance is null || remittance.IdPanelNavigation?.IdContractNavigation is not { } contract
            || contract.IdCounterpartyNavigation is not { } cedent || remittance.IdCounterpartyNavigation is not { } reinsurer
            || remittance.IdCurrencyNavigation is not { } currency)
            return Failure<PrintablePaymentDocumentDto>("The closing remittance is missing its required business relationships.", status: StatusCodes.Status409Conflict, code: "REMITTANCE_CLOSING_DATA_INCOMPLETE");
        var coverage = contract.ContractCoverageSelections
            .Where(selection => selection.IdCoverageItemNavigation is not null && CoverageConstants.IsMainCoverLevel(selection.IdCoverageItemNavigation.CoverageLevel))
            .OrderByDescending(selection => selection.IsPrimary).ThenBy(selection => selection.IdCoverageItem)
            .Select(selection => selection.IdCoverageItemNavigation!.CoverageName).FirstOrDefault();
        var description = BuildClosingDescription(contract, coverage);
        var precision = PanelRemittanceCalculator.Precision(currency.DecimalPlaces);
        var payableAmount = await _db.OutgoingClosingLines.AsNoTracking()
            .Where(line => line.ClosingId == closingId && line.RemittanceId == remittanceId && line.IsActive)
            .Select(line => line.PayableAmount).SingleAsync(ct);
        var documentDate = remittance.ClosingIssuedDate ?? DateOnly.FromDateTime(closing.IssuedAt);
        var document = ClosingDocument(closing);
        document.Content = BuildSingleRemittanceClosingPdf(remittance, remittance.IdPanelNavigation!, contract, cedent,
            reinsurer, currency, coverage, description, documentDate, precision, payableAmount);
        document.FileName = $"Closing_REM-{remittanceId}.pdf";
        return ServiceResult<PrintablePaymentDocumentDto>.Success(document);
    }

    private static bool IsClosingAvoir(string? sourceType) =>
        string.Equals(sourceType?.Trim(), "AVOIR", StringComparison.OrdinalIgnoreCase);

    private static string BuildClosingDescription(Contract contract, string? coverage) =>
        $"Your share of the reinsurance premium in respect to \"{contract.Name?.Trim() ?? string.Empty}\" {coverage?.Trim() ?? string.Empty} Policy.";

    private static void AddPartnershipLine(StringBuilder drawing, int x, int y)
    {
        drawing.Append("0.0 0.55 0.63 rg BT /F2 9 Tf 1 0 0 1 ").Append(x)
            .Append(' ').Append(y).Append(" Tm (IRIS) Tj ET\n")
            .Append("0.07 0.12 0.19 rg BT /F2 9 Tf 1 0 0 1 ").Append(x + 25)
            .Append(' ').Append(y).Append(" Tm (- Partner of Marsh McLennan) Tj ET\n");
    }

    private static byte[] AssembleClosingAdvicePdf(IReadOnlyList<byte[]> pageContents, PngPdfImage? logo)
    {
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Array.Empty<byte>(),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>")
        };
        int? logoObjectId = null;
        int? alphaObjectId = null;
        if (logo is not null)
        {
            logoObjectId = objects.Count + 1;
            alphaObjectId = logo.Alpha is null ? null : logoObjectId + 1;
            var softMask = alphaObjectId.HasValue ? $" /SMask {alphaObjectId.Value} 0 R" : string.Empty;
            objects.Add(PdfStream(
                $"/Type /XObject /Subtype /Image /Width {logo.Width} /Height {logo.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns {logo.Width} >>{softMask}",
                logo.Rgb));
            if (logo.Alpha is not null)
                objects.Add(PdfStream(
                    $"/Type /XObject /Subtype /Image /Width {logo.Width} /Height {logo.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 1 /BitsPerComponent 8 /Columns {logo.Width} >>",
                    logo.Alpha));
        }

        var firstPageId = objects.Count + 1;
        var pageIds = Enumerable.Range(0, pageContents.Count).Select(index => firstPageId + index * 2).ToArray();
        objects[1] = Ascii($"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Length} >>");
        var resources = logoObjectId.HasValue
            ? $"<< /Font << /F1 3 0 R /F2 4 0 R >> /XObject << /Logo {logoObjectId.Value} 0 R >> >>"
            : "<< /Font << /F1 3 0 R /F2 4 0 R >> >>";
        for (var index = 0; index < pageContents.Count; index++)
        {
            var pageId = pageIds[index];
            var contentId = pageId + 1;
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources {resources} /Contents {contentId} 0 R >>"));
            objects.Add(PdfStream(string.Empty, pageContents[index]));
        }
        return AssemblePdf(objects);
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
