using System.Data;
using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    public async Task<ServiceResult<IReadOnlyList<EligibleAvoirRefundReceiptDto>>> GetEligibleAvoirRefundReceiptsAsync(
        int avoirInvoiceId, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<IReadOnlyList<EligibleAvoirRefundReceiptDto>>();

        var avoir = await _db.Invoices.AsNoTracking().Include(x => x.IdContractNavigation)
            .FirstOrDefaultAsync(x => x.IdInvoice == avoirInvoiceId, cancellationToken);
        if (avoir is null || !InvoiceDocumentAccounting.IsAvoir(avoir.InvoiceType)) return NotFound<IReadOnlyList<EligibleAvoirRefundReceiptDto>>("Avoir was not found.");
        if (string.Equals(avoir.ProcessingMode, InvoiceDocumentAccounting.DirectToRemittances, StringComparison.OrdinalIgnoreCase))
            return ServiceResult<IReadOnlyList<EligibleAvoirRefundReceiptDto>>.Success([]);

        var usedAmount = await _db.InvoiceCreditApplications.AsNoTracking()
            .Where(x => x.IsActive && x.AvoirInvoiceId == avoirInvoiceId).SumAsync(x => x.Amount, cancellationToken);
        usedAmount += await _db.AvoirRefundSettlements.AsNoTracking()
            .Where(x => x.IsActive && x.AvoirInvoiceId == avoirInvoiceId).SumAsync(x => x.Amount, cancellationToken);
        var availableAvoir = Math.Max(0m, (avoir.GrossPremium ?? 0m) - usedAmount);

        var receiptRows = await _db.BankReceipts.AsNoTracking()
            .Where(receipt => receipt.IdCounterparty == avoir.IdContractNavigation.IdCounterparty
                && receipt.IdCurrency == avoir.IdCurrency
                && receipt.AmountReceived > 0m
                && receipt.IdCounterpartyNavigation.IsActive
                && receipt.IdCounterpartyNavigation.TypeAssignments.Any(type => type.Type.Trim().ToLower() == CounterpartyTypes.Cedent.ToLower())
                && receipt.IdBankAccountNavigation.IsActive)
            .OrderByDescending(receipt => receipt.OperationDate)
            .Select(receipt => new
            {
                receipt.IdBankReceipt,
                ReceiptReference = receipt.BusinessReference ?? receipt.BankReference ?? $"BR-{receipt.IdBankReceipt}",
                receipt.ReceptionDate,
                receipt.OperationDate,
                ReceiptAmount = receipt.AmountReceived,
                AllocatedAmount = receipt.ReceiptAllocations.Where(a => a.IsActive).Sum(a => a.AllocatedAmount ?? 0m),
                CurrencyCode = receipt.IdCurrencyNavigation.CurrencyCode,
                ContractId = avoir.IdContract,
                ContractName = avoir.IdContractNavigation.Name,
                CedentId = receipt.IdCounterparty,
                CedentName = receipt.IdCounterpartyNavigation.Name,
                CedentIsActive = receipt.IdCounterpartyNavigation.IsActive,
                IsCedent = receipt.IdCounterpartyNavigation.TypeAssignments.Any(type => type.Type.Trim().ToLower() == CounterpartyTypes.Cedent.ToLower()),
                AvoirInvoiceId = avoir.IdInvoice,
                AvoirNumber = avoir.InvoiceNumber
            }).ToListAsync(cancellationToken);

        var receipts = receiptRows.Select(receipt =>
        {
            var remaining = Math.Max(0m, receipt.ReceiptAmount - receipt.AllocatedAmount);
            return new EligibleAvoirRefundReceiptDto
            {
                BankReceiptId = receipt.IdBankReceipt,
                ReceiptReference = receipt.ReceiptReference,
                ReceptionDate = receipt.ReceptionDate,
                OperationDate = receipt.OperationDate,
                ReceiptAmount = receipt.ReceiptAmount,
                AllocatedAmount = receipt.AllocatedAmount,
                RemainingAmount = remaining,
                PairingStatus = InvoiceRecoveryCalculator.GetReceiptPairingStatus(receipt.ReceiptAmount, receipt.AllocatedAmount),
                CurrencyCode = receipt.CurrencyCode,
                ContractId = receipt.ContractId,
                ContractName = receipt.ContractName,
                CedentId = receipt.CedentId,
                CedentName = receipt.CedentName,
                AvoirInvoiceId = receipt.AvoirInvoiceId,
                AvoirNumber = receipt.AvoirNumber,
                AvailableAvoirAmount = availableAvoir,
                RefundAmount = availableAvoir,
                IsEligible = availableAvoir > 0m && receipt.CedentIsActive && receipt.IsCedent
                    && receipt.ReceiptAmount > 0m,
                EligibilityFailureReason = availableAvoir <= 0m
                    ? "This Avoir has no refundable balance remaining."
                    : !receipt.CedentIsActive || !receipt.IsCedent
                        ? "The cedent is not active or is not eligible for refunds."
                        : receipt.ReceiptAmount <= 0m
                            ? "The encaissement has no refundable amount."
                            : null
            };
        }).ToList();

        return ServiceResult<IReadOnlyList<EligibleAvoirRefundReceiptDto>>.Success(receipts);
    }

    public async Task<ServiceResult<ReceiptBatchAllocationResponseDto>> RefundAvoirAsync(
        int receiptId, AvoirRefundRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<ReceiptBatchAllocationResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<ReceiptBatchAllocationResponseDto>();
        if (request.AvoirInvoiceId <= 0 || request.Amount <= 0m)
            return Failure<ReceiptBatchAllocationResponseDto>("Avoir and positive refund amount are required.", status: StatusCodes.Status400BadRequest);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 100)
            return Failure<ReceiptBatchAllocationResponseDto>("A valid idempotency key is required.", status: StatusCodes.Status400BadRequest);
        var requestKey = request.IdempotencyKey.Trim();
        var requestHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(requestKey)))[..16];

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await LockOutgoingWorkflowAsync(cancellationToken);
            var retry = await _db.AvoirRefundSettlements.AsNoTracking()
                .Include(x => x.Remittance).Include(x => x.AvoirInvoice)
                .Include(x => x.BankReceipt).ThenInclude(x => x.IdCounterpartyNavigation)
                .Include(x => x.BankReceipt).ThenInclude(x => x.IdCurrencyNavigation)
                .Include(x => x.BankReceipt).ThenInclude(x => x.ReceiptAllocations).ThenInclude(x => x.IdInvoiceNavigation)
                .SingleOrDefaultAsync(x => x.IdempotencyKey == requestKey, cancellationToken);
            if (retry is not null)
            {
                var groupPrefix = $"AV:{retry.AvoirInvoiceId}:{receiptId}:";
                var groupTotal = await _db.AvoirRefundSettlements.AsNoTracking()
                    .Where(x => x.IsActive && x.AvoirInvoiceId == retry.AvoirInvoiceId && x.BankReceiptId == receiptId
                        && (x.IdempotencyKey == requestKey || (x.IdempotencyKey.StartsWith(groupPrefix) && x.IdempotencyKey.EndsWith($":{requestHash}")))
                        )
                    .SumAsync(x => x.Amount, cancellationToken);
                if (retry.BankReceiptId != receiptId || retry.AvoirInvoiceId != request.AvoirInvoiceId || groupTotal != request.Amount)
                    return Failure<ReceiptBatchAllocationResponseDto>("The idempotency key was already used for a different refund.", status: StatusCodes.Status409Conflict);
                await transaction.CommitAsync(cancellationToken);
                return ServiceResult<ReceiptBatchAllocationResponseDto>.Success(await MapAvoirRefundAsync(retry, cancellationToken));
            }

            var receipt = await _db.BankReceipts.AsNoTracking().Include(x => x.ReceiptAllocations)
                .ThenInclude(x => x.IdInvoiceNavigation)
                .Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation)
                .Include(x => x.IdBankAccountNavigation)
                .SingleOrDefaultAsync(x => x.IdBankReceipt == receiptId, cancellationToken);
            if (receipt is null) return NotFound<ReceiptBatchAllocationResponseDto>("Encaissement was not found.");
            var avoir = await _db.Invoices.Include(x => x.IdContractNavigation).ThenInclude(x => x.IdCounterpartyNavigation)
                .SingleOrDefaultAsync(x => x.IdInvoice == request.AvoirInvoiceId, cancellationToken);
            if (avoir is null || !InvoiceDocumentAccounting.IsAvoir(avoir.InvoiceType)) return NotFound<ReceiptBatchAllocationResponseDto>("Avoir was not found.");
            if (string.Equals(avoir.ProcessingMode, InvoiceDocumentAccounting.DirectToRemittances, StringComparison.OrdinalIgnoreCase))
                return Failure<ReceiptBatchAllocationResponseDto>("This Avoir was processed directly to reinsurer remittances and cannot be paired with an encaissement.", status: StatusCodes.Status409Conflict);
            if (avoir.IdContractNavigation.IdCounterparty != receipt.IdCounterparty || avoir.IdCurrency != receipt.IdCurrency)
                return Failure<ReceiptBatchAllocationResponseDto>("Cedent or currency does not match the selected encaissement.", status: StatusCodes.Status409Conflict);

            if (receipt.AmountReceived <= 0m || receipt.IdBankAccountNavigation is null || !receipt.IdBankAccountNavigation.IsActive)
                return Failure<ReceiptBatchAllocationResponseDto>("The selected encaissement is not active or usable.", status: StatusCodes.Status409Conflict);
            var validCedent = await _db.Counterparties.AsNoTracking().AnyAsync(counterparty => counterparty.Id == receipt.IdCounterparty
                && counterparty.IsActive && counterparty.TypeAssignments.Any(type => type.Type.Trim().ToLower() == CounterpartyTypes.Cedent.ToLower()), cancellationToken);
            if (!validCedent) return Failure<ReceiptBatchAllocationResponseDto>("The encaissement beneficiary is not an active cedent.", status: StatusCodes.Status409Conflict);
            var alreadyAppliedToReceipt = await _db.AvoirRefundSettlements.AsNoTracking().AnyAsync(x =>
                x.IsActive && x.AvoirInvoiceId == avoir.IdInvoice && x.BankReceiptId == receiptId, cancellationToken)
                || await _db.InvoiceCreditApplications.AsNoTracking().AnyAsync(x =>
                    x.IsActive && x.AvoirInvoiceId == avoir.IdInvoice && x.IdBankReceipt == receiptId, cancellationToken);
            if (alreadyAppliedToReceipt)
                return Failure<ReceiptBatchAllocationResponseDto>("This Avoir has already been applied to the selected encaissement.", status: StatusCodes.Status409Conflict);
            var applied = await _db.InvoiceCreditApplications.Where(x => x.IsActive && x.AvoirInvoiceId == avoir.IdInvoice).SumAsync(x => x.Amount, cancellationToken);
            var alreadyRefunded = await _db.AvoirRefundSettlements.Where(x => x.IsActive && x.AvoirInvoiceId == avoir.IdInvoice).SumAsync(x => x.Amount, cancellationToken);
            var available = Math.Max(0m, (avoir.GrossPremium ?? 0m) - applied - alreadyRefunded);
            if (available <= 0m)
                return Failure<ReceiptBatchAllocationResponseDto>("The Avoir is already fully settled.", status: StatusCodes.Status409Conflict);
            if (request.Amount > available)
                return Failure<ReceiptBatchAllocationResponseDto>($"Refund amount exceeds the Avoir's unsettled balance of {available:0.####}.", status: StatusCodes.Status409Conflict);

            var panels = await _db.InvoicePanelAllocations
                .Include(line => line.IdPanelNavigation).ThenInclude(panel => panel.IdCounterpartyNavigation).ThenInclude(counterparty => counterparty.TypeAssignments)
                .Where(line => line.IdInvoice == avoir.IdInvoice
                    && line.IdPanelNavigation.IdContract == avoir.IdContract
                    && line.IdPanelNavigation.Role != null && line.IdPanelNavigation.Role.Trim().ToLower() == "reinsurer"
                    && line.IdPanelNavigation.Status != null && line.IdPanelNavigation.Status.Trim().ToLower() == ContractConstants.ConfirmedPanelStatus.ToLower()
                    && line.IdPanelNavigation.IdCounterparty == line.IdCounterparty
                    && line.IdCounterpartyNavigation.IsActive
                    && line.IdCounterpartyNavigation.TypeAssignments.Any(type => type.Type.Trim().ToLower() == CounterpartyTypes.Reinsurer.ToLower())
                    && line.AllocationRatio > 0m)
                .OrderBy(line => line.IdPanel).ToListAsync(cancellationToken);
            var savedPanelAllocationCount = await _db.InvoicePanelAllocations.AsNoTracking()
                .CountAsync(line => line.IdInvoice == avoir.IdInvoice, cancellationToken);
            if (panels.Count == 0 || panels.Count != savedPanelAllocationCount)
                return Failure<ReceiptBatchAllocationResponseDto>("Every saved Avoir allocation must belong to a confirmed reinsurer panel.", status: StatusCodes.Status409Conflict);
            var precision = PanelRemittanceCalculator.Precision(receipt.IdCurrencyNavigation.DecimalPlaces);
            var avoirGross = avoir.GrossPremium ?? 0m;
            if (avoirGross <= 0m)
                return Failure<ReceiptBatchAllocationResponseDto>("The Avoir has no positive amount available for panel allocation.", status: StatusCodes.Status409Conflict);
            var panelGrossShares = DistributeRemittanceNetAmount(request.Amount,
                panels.Select(line => line.GrossPremium).ToArray(), precision);
            var payableWeights = panels.Select(line => Math.Max(0m,
                PremiumDebitNoteAllocationCalculator.NetPayableFromPersistedAllocation(line))).ToArray();
            if (payableWeights.Any(amount => amount <= 0m))
                return Failure<ReceiptBatchAllocationResponseDto>("Every confirmed Avoir panel allocation must have a positive net payable amount.", status: StatusCodes.Status409Conflict);
            var payableTotal = decimal.Round(payableWeights.Sum() * request.Amount / avoirGross, precision, MidpointRounding.AwayFromZero);
            var panelPayables = DistributeRemittanceNetAmount(payableTotal, payableWeights, precision);
            var now = DateTime.UtcNow;
            var created = new List<RemittanceRef>(panels.Count);
            for (var index = 0; index < panels.Count; index++)
            {
                var allocation = panels[index];
                var panel = allocation.IdPanelNavigation;
                var remittance = new RemittanceRef
                {
                    SourceType = "AVOIR", SourceInvoiceId = avoir.IdInvoice, IdInvoiceAllocation = allocation.IdInvoiceAllocation,
                    BeneficiaryType = "REINSURER", IdReceiptAllocation = null, IdPanel = panel.IdPanel,
                    IdCounterparty = panel.IdCounterparty, IdCurrency = receipt.IdCurrency,
                    PartPct = allocation.AllocationRatio * 100m,
                    PremiumSignedLine = panelGrossShares[index],
                    Deductions = decimal.Round(panelGrossShares[index] - panelPayables[index], precision, MidpointRounding.AwayFromZero),
                    NetPremiumDue = panelPayables[index], NetPayableAmount = panelPayables[index],
                    Description = $"Generated from Avoir {avoir.InvoiceNumber} for {receipt.BusinessReference ?? $"BR-{receipt.IdBankReceipt}"}",
                    Status = "Draft", SignatureStatus = "Draft", CreatedAt = now, CreatedBy = user, IsActive = true
                };
                _db.RemittanceRefs.Add(remittance);
                created.Add(remittance);
            }
            await _db.SaveChangesAsync(cancellationToken);
            var settlementRows = new List<AvoirRefundSettlement>(panels.Count);
            var generatedKeyPrefix = $"AV:{avoir.IdInvoice}:{receiptId}:";
            for (var index = 0; index < created.Count; index++)
            {
                var key = index == 0 ? requestKey : $"{generatedKeyPrefix}{panels[index].IdPanel}:{requestHash}";
                var settlement = new AvoirRefundSettlement
                {
                    AvoirInvoiceId = avoir.IdInvoice, BankReceiptId = receiptId, RemittanceId = created[index].RemittanceRef1,
                    Amount = panelGrossShares[index], IdempotencyKey = key, CreatedAt = now, CreatedBy = user, IsActive = true
                };
                _db.AvoirRefundSettlements.Add(settlement);
                settlementRows.Add(settlement);
            }
            var totalUsed = applied + alreadyRefunded + request.Amount;
            await _db.Invoices.Where(x => x.IdInvoice == avoir.IdInvoice).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RecoveryStatus, totalUsed >= (avoir.GrossPremium ?? 0m)
                    ? InvoiceRecoveryCalculator.Settled : InvoiceRecoveryCalculator.PartiallySettled)
                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.UpdatedBy, user), cancellationToken);
            await NotifyRoleAsync(
                "Back Officer",
                "Avoir reinsurer remittances created",
                $"Avoir {avoir.InvoiceNumber} has panel remittances requiring review.",
                "REMITTANCE_REF",
                created[0].RemittanceRef1,
                cancellationToken,
                "AvoirRemittancesRequireReview",
                "/back-office/remittances?sourceType=AVOIR");
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var persistedSettlement = await _db.AvoirRefundSettlements.AsNoTracking()
                .Include(x => x.AvoirInvoice)
                .Include(x => x.BankReceipt).ThenInclude(x => x.IdCounterpartyNavigation)
                .Include(x => x.BankReceipt).ThenInclude(x => x.IdCurrencyNavigation)
                .Include(x => x.Remittance)
                .SingleAsync(x => x.Id == settlementRows[0].Id, cancellationToken);
            return ServiceResult<ReceiptBatchAllocationResponseDto>.Success(await MapAvoirRefundAsync(persistedSettlement, cancellationToken), StatusCodes.Status201Created);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<ReceiptBatchAllocationResponseDto> MapAvoirRefundAsync(AvoirRefundSettlement settlement, CancellationToken cancellationToken)
    {
        var receipt = settlement.BankReceipt;
        var invoice = settlement.AvoirInvoice;
        var remittance = settlement.Remittance;
        var totalUsed = await _db.InvoiceCreditApplications.AsNoTracking()
            .Where(x => x.IsActive && x.AvoirInvoiceId == invoice.IdInvoice).SumAsync(x => x.Amount, cancellationToken);
        totalUsed += await _db.AvoirRefundSettlements.AsNoTracking()
            .Where(x => x.IsActive && x.AvoirInvoiceId == invoice.IdInvoice).SumAsync(x => x.Amount, cancellationToken);
        var receiptAllocated = await _db.ReceiptAllocations.AsNoTracking()
            .Where(x => x.IsActive && x.IdBankReceipt == receipt.IdBankReceipt).SumAsync(x => x.AllocatedAmount ?? 0m, cancellationToken);
        var remittances = await RemittanceDetails().Where(item => item.SourceInvoiceId == invoice.IdInvoice
                && item.IdPanel.HasValue && item.SourceType == "AVOIR"
                && item.AvoirRefundSettlements.Any(link => link.IsActive && link.BankReceiptId == receipt.IdBankReceipt))
            .OrderBy(item => item.IdPanel).ToListAsync(cancellationToken);
        var appliedToReceipt = await _db.AvoirRefundSettlements.AsNoTracking()
            .Where(item => item.IsActive && item.AvoirInvoiceId == invoice.IdInvoice && item.BankReceiptId == receipt.IdBankReceipt)
            .SumAsync(item => item.Amount, cancellationToken);
        return new ReceiptBatchAllocationResponseDto
        {
            PairingMode = "AVOIR_REINSURER_ALLOCATION", ReceiptId = receipt.IdBankReceipt,
            ReceiptReference = receipt.BusinessReference ?? receipt.BankReference ?? $"BR-{receipt.IdBankReceipt}",
            TotalAllocated = receiptAllocated, RemainingAmount = Math.Max(0m, receipt.AmountReceived - receiptAllocated),
            PairingStatus = InvoiceRecoveryCalculator.GetReceiptPairingStatus(receipt.AmountReceived, receiptAllocated),
            SignedPairingTotal = -appliedToReceipt,
            AvoirInvoiceId = invoice.IdInvoice, AvoirNumber = invoice.InvoiceNumber,
            AvoirOriginalAmount = invoice.GrossPremium,
            AvoirAppliedAmount = appliedToReceipt,
            AvoirRemainingAmount = Math.Max(0m, (invoice.GrossPremium ?? 0m) - totalUsed),
            BeneficiaryId = remittances.FirstOrDefault()?.IdCounterparty,
            BeneficiaryName = remittances.FirstOrDefault()?.IdCounterpartyNavigation?.Name ?? string.Empty,
            BeneficiaryType = "REINSURER", RemittanceId = remittance.RemittanceRef1,
            RemittanceReference = $"REM-{remittance.RemittanceRef1}", RemittanceStatus = remittance.Status,
            CurrencyCode = receipt.IdCurrencyNavigation?.CurrencyCode,
            CreatedRemittances = remittances.Select(MapOutgoingRemittance).ToList()
        };
    }
}
