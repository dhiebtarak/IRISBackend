using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    public async Task<ServiceResult<ReceiptBatchAllocationResponseDto>> AllocateReceiptInvoicesAsync(
        int receiptId, InvoiceReceiptAllocationRequestDto request, CancellationToken ct)
    {
        if (!CanBack()) return Forbidden<ReceiptBatchAllocationResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<ReceiptBatchAllocationResponseDto>();
        var items = request.Allocations ?? [];
        decimal? receiptRemaining = null;
        var invoiceRemaining = new Dictionary<int, decimal>();

        ServiceResult<ReceiptBatchAllocationResponseDto> Conflict(string code, string message,
            InvoiceReceiptAllocationItemDto? item = null, int status = 409)
        {
            foreach (var current in item is null ? items : [item])
                _logger.LogWarning("Invoice pairing conflict ReceiptId={ReceiptId} InvoiceId={InvoiceId} RequestedAmount={RequestedAmount} ReceiptRemainingAmount={ReceiptRemainingAmount} InvoiceRemainingAmount={InvoiceRemainingAmount} ConflictCode={ConflictCode}",
                    receiptId, current.InvoiceId, current.AllocatedAmount, receiptRemaining,
                    invoiceRemaining.TryGetValue(current.InvoiceId, out var balance) ? (decimal?)balance : null, code);
            return ServiceResult<ReceiptBatchAllocationResponseDto>.Failure(message, statusCode: status, code: code);
        }

        if (items.Count == 0) return Conflict("ALLOCATION_REQUIRED", "At least one invoice allocation is required.", status: 400);
        if (items.Any(x => x.InvoiceId <= 0 || x.AllocatedAmount <= 0m))
            return Conflict("INVALID_ALLOCATION_AMOUNT", "Each allocation requires a positive invoiceId and allocatedAmount.", status: 400);
        var ids = items.Select(x => x.InvoiceId).ToArray();
        if (ids.Distinct().Count() != ids.Length)
            return Conflict("DUPLICATE_INVOICE_IN_REQUEST", "An invoice can occur only once in a pairing request.", status: 400);
        var canonical = string.Join(";", items.OrderBy(x => x.InvoiceId)
            .Select(x => x.InvoiceId.ToString(CultureInfo.InvariantCulture) + ":" + x.AllocatedAmount.ToString("G29", CultureInfo.InvariantCulture)));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await LockOutgoingWorkflowAsync(ct);
            var receipt = await _db.BankReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.IdBankReceipt == receiptId, ct);
            if (receipt is null) return NotFound<ReceiptBatchAllocationResponseDto>("Bank receipt was not found.");
            var invoices = await _db.Invoices.AsNoTracking().Include(x => x.IdContractNavigation)
                .Where(x => ids.Contains(x.IdInvoice)).ToDictionaryAsync(x => x.IdInvoice, ct);
            if (invoices.Count != ids.Length) return NotFound<ReceiptBatchAllocationResponseDto>("One or more invoices were not found.");
            var existingReceiptAmount = await _db.ReceiptAllocations.AsNoTracking()
                .Where(x => x.IsActive && x.IdBankReceipt == receiptId).SumAsync(x => x.AllocatedAmount ?? 0m, ct);
            receiptRemaining = receipt.AmountReceived - existingReceiptAmount;
            var pairedByInvoice = await _db.ReceiptAllocations.AsNoTracking()
                .Where(x => x.IsActive && x.IdInvoice.HasValue && ids.Contains(x.IdInvoice.Value))
                .GroupBy(x => x.IdInvoice!.Value)
                .Select(g => new { Id = g.Key, Amount = g.Sum(x => x.AllocatedAmount ?? 0m) })
                .ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
            var creditApplications = await _db.InvoiceCreditApplications.AsNoTracking().Where(application => application.IsActive
                    && (ids.Contains(application.TargetInvoiceId) || ids.Contains(application.AvoirInvoiceId)))
                .ToListAsync(ct);
            var refundedByAvoir = await _db.AvoirRefundSettlements.AsNoTracking()
                .Where(refund => refund.IsActive && ids.Contains(refund.AvoirInvoiceId))
                .GroupBy(refund => refund.AvoirInvoiceId)
                .Select(group => new { InvoiceId = group.Key, Amount = group.Sum(refund => refund.Amount) })
                .ToDictionaryAsync(item => item.InvoiceId, item => item.Amount, ct);
            invoiceRemaining = invoices.ToDictionary(x => x.Key, x => (x.Value.GrossPremium ?? 0m)
                - pairedByInvoice.GetValueOrDefault(x.Key)
                - (InvoiceDocumentAccounting.IsAvoir(x.Value.InvoiceType)
                    ? creditApplications.Where(application => application.AvoirInvoiceId == x.Key).Sum(application => application.Amount)
                        + refundedByAvoir.GetValueOrDefault(x.Key)
                    : creditApplications.Where(application => application.TargetInvoiceId == x.Key).Sum(application => application.Amount)));
            var priorOperation = await _db.InvoicePairingOperations.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.Allocation)
                .Include(x => x.CreditApplications)
                .SingleOrDefaultAsync(x => x.ReceiptId == receiptId && x.RequestHash == hash && x.IsActive, ct);
            if (priorOperation is not null && priorOperation.Lines.All(x => x.Allocation.IsActive)
                && priorOperation.CreditApplications.All(x => x.IsActive))
            {
                foreach (var item in items) LogPairing(item, "IDEMPOTENT_RETRY");
                var retryResponse = ToPairingResponse(priorOperation);
                retryResponse.CreatedRemittances = await GetRemittancesForAllocationsAsync(
                    priorOperation.Lines.Select(line => line.ReceiptAllocationId).Distinct().ToArray(), ct);
                await tx.CommitAsync(ct);
                return ServiceResult<ReceiptBatchAllocationResponseDto>.Success(retryResponse);
            }
            if (priorOperation is not null)
                await _db.InvoicePairingOperations.Where(x => x.Id == priorOperation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);

            var priorRows = await _db.ReceiptAllocations.AsNoTracking()
                .Where(x => x.IsActive && x.IdBankReceipt == receiptId && x.IdInvoice.HasValue && ids.Contains(x.IdInvoice.Value))
                .ToDictionaryAsync(x => x.IdInvoice!.Value, ct);
            // Adopt historical successful requests without inserting their allocations again.
            var regularItems = items.Where(x => !InvoiceDocumentAccounting.IsAvoir(invoices[x.InvoiceId].InvoiceType)).ToArray();
            var containsAvoir = regularItems.Length != items.Count;
            var legacyRetry = priorOperation is null && !containsAvoir && priorRows.Count == regularItems.Length
                && regularItems.All(x => priorRows.TryGetValue(x.InvoiceId, out var row) && row.AllocatedAmount == x.AllocatedAmount)
                && !await _db.InvoicePairingOperations.AnyAsync(x => x.ReceiptId == receiptId, ct);
            if (receiptRemaining <= 0m && !legacyRetry)
                return Conflict("RECEIPT_ALREADY_FULLY_ALLOCATED", "The encaissement has already been fully allocated.");
            decimal signedPairingTotal = 0m;
            if (!legacyRetry)
            {
                foreach (var item in items)
                {
                    var invoice = invoices[item.InvoiceId];
                    if (invoice.IdCurrency != receipt.IdCurrency)
                        return Conflict("CURRENCY_MISMATCH", "The invoice currency does not match the encaissement currency.", item);
                    if (!InvoiceDocumentAccounting.IsContractReceivableType(invoice.InvoiceType))
                        return Conflict("INVOICE_TYPE_NOT_ELIGIBLE", "Only INVOICE, INVOICE_FEES and AVOIR documents can be paired with incoming encaissements.", item, 400);
                    if (InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType)
                        && string.Equals(invoice.ProcessingMode, InvoiceDocumentAccounting.DirectToRemittances, StringComparison.OrdinalIgnoreCase))
                        return Conflict("AVOIR_ALREADY_PROCESSED_DIRECTLY", "This Avoir was processed directly to reinsurer remittances and cannot be paired with an encaissement.", item);
                    if (string.Equals(invoice.Status?.Trim(), "Cancelled", StringComparison.OrdinalIgnoreCase))
                        return Conflict("INVOICE_CANCELLED", "Cancelled invoices cannot receive allocations.", item);
                    if (invoice.IdContractNavigation.IdCounterparty != receipt.IdCounterparty)
                        return Conflict("CEDENT_MISMATCH", "The invoice cedent does not match the encaissement counterparty.", item, 400);
                    if (!containsAvoir && item.AllocatedAmount > receiptRemaining)
                        return Conflict("ALLOCATION_EXCEEDS_RECEIPT_REMAINING", "The requested allocation exceeds the encaissement remaining amount.", item);
                    if (invoiceRemaining[item.InvoiceId] <= 0m)
                        return Conflict("INVOICE_ALREADY_SETTLED", "The selected invoice or Avoir has no remaining balance.", item);
                    if (item.AllocatedAmount > invoiceRemaining[item.InvoiceId])
                        return Conflict(InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType) ? "AVOIR_EXCEEDS_BALANCE" : "ALLOCATION_EXCEEDS_INVOICE_REMAINING",
                            InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType)
                                ? "The applied Avoir exceeds its unapplied balance."
                                : $"Requested amount {item.AllocatedAmount} exceeds invoice remaining amount {invoiceRemaining[item.InvoiceId]}.",
                            item, InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType) ? 400 : 409);
                    signedPairingTotal += InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType) ? -item.AllocatedAmount : item.AllocatedAmount;
                }
                if (signedPairingTotal < 0m)
                    return Conflict("NEGATIVE_PAIRING_TOTAL", "Avoir amounts cannot exceed the selected invoice and invoice-fees amounts.", status: 400);
                if (signedPairingTotal > receiptRemaining)
                    return Conflict("ALLOCATION_EXCEEDS_RECEIPT_REMAINING", "The total requested allocation exceeds the encaissement remaining amount.");
                if (containsAvoir && signedPairingTotal > 0m && regularItems.Length == 0)
                    return Conflict("AVOIR_TARGET_REQUIRED", "Select at least one invoice or invoice-fees document to apply the Avoir.", status: 400);
            }

            var now = DateTime.UtcNow;
            var allocations = new Dictionary<int, ReceiptAllocation>();
            var creditApplicationsToCreate = new List<InvoiceCreditApplication>();
            var creditByTargetInvoice = new Dictionary<int, decimal>();
            foreach (var avoirItem in items.Where(item => InvoiceDocumentAccounting.IsAvoir(invoices[item.InvoiceId].InvoiceType)))
            {
                var creditLeft = avoirItem.AllocatedAmount;
                foreach (var targetItem in regularItems.OrderBy(item => item.InvoiceId))
                {
                    var remainingForTarget = targetItem.AllocatedAmount - creditByTargetInvoice.GetValueOrDefault(targetItem.InvoiceId);
                    if (remainingForTarget <= 0m || creditLeft <= 0m) continue;
                    var creditAmount = Math.Min(remainingForTarget, creditLeft);
                    creditByTargetInvoice[targetItem.InvoiceId] = creditByTargetInvoice.GetValueOrDefault(targetItem.InvoiceId) + creditAmount;
                    creditLeft -= creditAmount;
                    creditApplicationsToCreate.Add(new InvoiceCreditApplication
                    {
                        IdBankReceipt = receiptId,
                        TargetInvoiceId = targetItem.InvoiceId,
                        AvoirInvoiceId = avoirItem.InvoiceId,
                        Amount = creditAmount,
                        CreatedAt = now,
                        CreatedBy = user,
                        IsActive = true
                    });
                }
                if (creditLeft > 0m)
                    return Conflict("AVOIR_TARGET_REQUIRED", "The selected Avoir exceeds the selected invoice balances available to offset.", avoirItem, 400);
            }

            foreach (var item in regularItems)
            {
                LogPairing(item, legacyRetry ? "IDEMPOTENT_LEGACY_RETRY" : "ACCEPTED");
                var cashAmount = item.AllocatedAmount - creditByTargetInvoice.GetValueOrDefault(item.InvoiceId);
                if (priorRows.TryGetValue(item.InvoiceId, out var existing))
                {
                    allocations[item.InvoiceId] = existing;
                    if (!legacyRetry && cashAmount > 0m)
                    {
                        var newAmount = (existing.AllocatedAmount ?? 0m) + cashAmount;
                        await _db.ReceiptAllocations.Where(x => x.IdReceiptAllocation == existing.IdReceiptAllocation)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AllocatedAmount, newAmount).SetProperty(x => x.PairedAt, now), ct);
                    }
                }
                else if (cashAmount > 0m)
                {
                    var allocation = new ReceiptAllocation
                    {
                        IdBankReceipt = receiptId, IdInvoice = item.InvoiceId, TargetType = "INVOICE",
                        AllocatedAmount = cashAmount, IdCurrency = receipt.IdCurrency,
                        ExchangeRate = 1m, PairedAt = now, IdUser = user
                    };
                    _db.ReceiptAllocations.Add(allocation);
                    allocations[item.InvoiceId] = allocation;
                }
            }
            await _db.SaveChangesAsync(ct);
            var total = await _db.ReceiptAllocations.AsNoTracking().Where(x => x.IsActive && x.IdBankReceipt == receiptId)
                .SumAsync(x => x.AllocatedAmount ?? 0m, ct);
            var status = InvoiceRecoveryCalculator.GetReceiptPairingStatus(receipt.AmountReceived, total);
            await _db.BankReceipts.Where(x => x.IdBankReceipt == receiptId).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.DetailsStatus, status).SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.UpdatedBy, user), ct);
            var operation = new InvoicePairingOperation
            {
                ReceiptId = receiptId, RequestHash = hash, CreatedAt = now, CreatedBy = user,
                TotalAllocated = total, SignedPairingTotal = signedPairingTotal,
                RemainingAmount = Math.Max(0m, receipt.AmountReceived - total), PairingStatus = status,
                Lines = regularItems.Where(x => allocations.ContainsKey(x.InvoiceId)).OrderBy(x => x.InvoiceId).Select(x => new InvoicePairingOperationLine
                {
                    ReceiptAllocationId = allocations[x.InvoiceId].IdReceiptAllocation,
                    RequestedAmount = x.AllocatedAmount - creditByTargetInvoice.GetValueOrDefault(x.InvoiceId)
                }).ToList()
            };
            _db.InvoicePairingOperations.Add(operation);
            await _db.SaveChangesAsync(ct);
            foreach (var creditApplication in creditApplicationsToCreate)
                creditApplication.OperationId = operation.Id;
            _db.InvoiceCreditApplications.AddRange(creditApplicationsToCreate);
            await _db.SaveChangesAsync(ct);
            var pairedAllocationIds = allocations.Values.Select(x => x.IdReceiptAllocation).Distinct().ToArray();
            await GeneratePanelRemittancesAsync(receipt, user, ct, pairedAllocationIds);

            foreach (var item in items)
            {
                var invoice = invoices[item.InvoiceId];
                var cashPaid = await _db.ReceiptAllocations.AsNoTracking().Where(x => x.IsActive && x.IdInvoice == item.InvoiceId)
                    .SumAsync(x => x.AllocatedAmount ?? 0m, ct);
                var creditsPaid = await _db.InvoiceCreditApplications.AsNoTracking().Where(x => x.IsActive
                        && (InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType) ? x.AvoirInvoiceId == item.InvoiceId : x.TargetInvoiceId == item.InvoiceId))
                    .SumAsync(x => x.Amount, ct);
                if (InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType))
                    creditsPaid += await _db.AvoirRefundSettlements.AsNoTracking()
                        .Where(x => x.IsActive && x.AvoirInvoiceId == item.InvoiceId).SumAsync(x => x.Amount, ct);
                var recovery = InvoiceRecoveryCalculator.Calculate(invoice.GrossPremium ?? 0m, cashPaid + creditsPaid).Status;
                await _db.Invoices.Where(x => x.IdInvoice == item.InvoiceId).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RecoveryStatus, recovery).SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.UpdatedBy, user), ct);
            }
            await _db.Notifications.Where(x => x.RecipientUserId == user && x.EntityType == "BANK_RECEIPT" && x.EntityId == receiptId && !x.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAt, now), ct);
            await _db.SaveChangesAsync(ct);
            var persisted = await _db.InvoicePairingOperations.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.Allocation)
                .Include(x => x.CreditApplications)
                .SingleAsync(x => x.Id == operation.Id, ct);
            var response = ToPairingResponse(persisted);
            response.CreatedRemittances = await GetRemittancesForAllocationsAsync(pairedAllocationIds, ct);
            await tx.CommitAsync(ct);
            return ServiceResult<ReceiptBatchAllocationResponseDto>.Success(response, legacyRetry ? 200 : 201);
        }
        catch (OutgoingRemittanceValidationException exception)
        {
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            _logger.LogWarning(exception, "Invoice pairing rolled back ReceiptId={ReceiptId} ConflictCode={ConflictCode} InnerException={InnerException}",
                receiptId, "OUTGOING_REMITTANCE_INVALID", exception.InnerException?.ToString());
            return Conflict("OUTGOING_REMITTANCE_INVALID", exception.Message);
        }
        catch (Exception exception)
        {
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            foreach (var item in items)
                _logger.LogError(exception, "Invoice pairing rolled back ReceiptId={ReceiptId} InvoiceId={InvoiceId} RequestedAmount={RequestedAmount} ReceiptRemainingAmount={ReceiptRemainingAmount} InvoiceRemainingAmount={InvoiceRemainingAmount} ConflictCode={ConflictCode} InnerException={InnerException}",
                    receiptId, item.InvoiceId, item.AllocatedAmount, receiptRemaining, invoiceRemaining.GetValueOrDefault(item.InvoiceId),
                    "PAIRING_TRANSACTION_FAILED", exception.InnerException?.ToString());
            throw;
        }

        void LogPairing(InvoiceReceiptAllocationItemDto item, string code) =>
            _logger.LogInformation("Invoice pairing ReceiptId={ReceiptId} InvoiceId={InvoiceId} RequestedAmount={RequestedAmount} ReceiptRemainingAmount={ReceiptRemainingAmount} InvoiceRemainingAmount={InvoiceRemainingAmount} ConflictCode={ConflictCode}",
                receiptId, item.InvoiceId, item.AllocatedAmount, receiptRemaining, invoiceRemaining[item.InvoiceId], code);
    }

    private static ReceiptBatchAllocationResponseDto ToPairingResponse(InvoicePairingOperation operation) => new()
    {
        PairingMode = "INVOICE_OFFSET",
        ReceiptId = operation.ReceiptId, TotalAllocated = operation.TotalAllocated,
        RemainingAmount = operation.RemainingAmount, PairingStatus = operation.PairingStatus,
        SignedPairingTotal = operation.SignedPairingTotal,
        AppliedAvoirInvoiceIds = operation.CreditApplications.Select(application => application.AvoirInvoiceId).Distinct().Order().ToArray(),
        Allocations = operation.Lines.OrderBy(x => x.Allocation.IdInvoice).Select(x => new ReceiptBatchAllocationItemDto
        {
            IdReceiptAllocation = x.ReceiptAllocationId, InvoiceId = x.Allocation.IdInvoice, AllocatedAmount = x.RequestedAmount
        }).ToList()
    };

    private async Task<IReadOnlyList<RemittanceResponseDto>> GetRemittancesForAllocationsAsync(
        IReadOnlyCollection<int> allocationIds, CancellationToken cancellationToken)
    {
        if (allocationIds.Count == 0) return [];
        var lines = await _db.RemittanceRefs.AsNoTrackingWithIdentityResolution()
            .Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation)
            .Include(x => x.IdPanelNavigation).ThenInclude(x => x!.IdContractNavigation)
            .Include(x => x.IdReceiptAllocationNavigation).ThenInclude(x => x!.IdInvoiceNavigation)
            .Include(x => x.IdReceiptAllocationNavigation).ThenInclude(x => x!.IdBankReceiptNavigation)
            .Where(x => x.IsActive && x.IdReceiptAllocation.HasValue
                && allocationIds.Contains(x.IdReceiptAllocation.Value))
            .OrderBy(x => x.RemittanceRef1).ToListAsync(cancellationToken);
        return lines.Select(MapOutgoingRemittance).ToList();
    }
}
