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
            invoiceRemaining = invoices.ToDictionary(x => x.Key, x => (x.Value.GrossPremium ?? 0m) - pairedByInvoice.GetValueOrDefault(x.Key));
            var priorOperation = await _db.InvoicePairingOperations.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.Allocation)
                .SingleOrDefaultAsync(x => x.ReceiptId == receiptId && x.RequestHash == hash && x.IsActive, ct);
            if (priorOperation is not null && priorOperation.Lines.All(x => x.Allocation.IsActive))
            {
                foreach (var item in items) LogPairing(item, "IDEMPOTENT_RETRY");
                await tx.CommitAsync(ct);
                return ServiceResult<ReceiptBatchAllocationResponseDto>.Success(ToPairingResponse(priorOperation));
            }
            if (priorOperation is not null)
                await _db.InvoicePairingOperations.Where(x => x.Id == priorOperation.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);

            var priorRows = await _db.ReceiptAllocations.AsNoTracking()
                .Where(x => x.IsActive && x.IdBankReceipt == receiptId && x.IdInvoice.HasValue && ids.Contains(x.IdInvoice.Value))
                .ToDictionaryAsync(x => x.IdInvoice!.Value, ct);
            // Adopt historical successful requests without inserting their allocations again.
            var legacyRetry = priorOperation is null && priorRows.Count == items.Count
                && items.All(x => priorRows[x.InvoiceId].AllocatedAmount == x.AllocatedAmount)
                && !await _db.InvoicePairingOperations.AnyAsync(x => x.ReceiptId == receiptId, ct);
            if (!legacyRetry)
            {
                foreach (var item in items)
                {
                    var invoice = invoices[item.InvoiceId];
                    if (invoice.IdCurrency != receipt.IdCurrency)
                        return Conflict("CURRENCY_MISMATCH", "The invoice currency does not match the encaissement currency.", item);
                    if (!string.Equals(invoice.InvoiceType, CedentPremiumInvoiceType, StringComparison.OrdinalIgnoreCase))
                        return Conflict("INVOICE_TYPE_NOT_ELIGIBLE", "Only cedent premium invoices can receive incoming allocations.", item, 400);
                    if (string.Equals(invoice.Status?.Trim(), "Cancelled", StringComparison.OrdinalIgnoreCase))
                        return Conflict("INVOICE_CANCELLED", "Cancelled invoices cannot receive allocations.", item);
                    if (invoice.IdContractNavigation.IdCounterparty != receipt.IdCounterparty)
                        return Conflict("CEDENT_MISMATCH", "The invoice cedent does not match the encaissement counterparty.", item, 400);
                    if (receiptRemaining <= 0m)
                        return Conflict("RECEIPT_ALREADY_FULLY_ALLOCATED", "The encaissement has no remaining amount to allocate.", item);
                    if (invoiceRemaining[item.InvoiceId] <= 0m)
                        return Conflict("INVOICE_ALREADY_SETTLED", "The invoice has no remaining amount to settle.", item);
                    if (item.AllocatedAmount > receiptRemaining)
                        return Conflict("ALLOCATION_EXCEEDS_RECEIPT_REMAINING", $"Requested amount {item.AllocatedAmount} exceeds receipt remaining amount {receiptRemaining}.", item);
                    if (item.AllocatedAmount > invoiceRemaining[item.InvoiceId])
                        return Conflict("ALLOCATION_EXCEEDS_INVOICE_REMAINING", $"Requested amount {item.AllocatedAmount} exceeds invoice remaining amount {invoiceRemaining[item.InvoiceId]}.", item);
                }
                if (items.Sum(x => x.AllocatedAmount) > receiptRemaining)
                    return Conflict("ALLOCATION_EXCEEDS_RECEIPT_REMAINING", "The total requested allocation exceeds the encaissement remaining amount.");
            }

            var now = DateTime.UtcNow;
            var allocations = new Dictionary<int, ReceiptAllocation>();
            foreach (var item in items)
            {
                LogPairing(item, legacyRetry ? "IDEMPOTENT_LEGACY_RETRY" : "ACCEPTED");
                if (priorRows.TryGetValue(item.InvoiceId, out var existing))
                {
                    if (!legacyRetry)
                    {
                        var amount = (existing.AllocatedAmount ?? 0m) + item.AllocatedAmount;
                        await _db.ReceiptAllocations.Where(x => x.IdReceiptAllocation == existing.IdReceiptAllocation)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AllocatedAmount, amount).SetProperty(x => x.PairedAt, now), ct);
                    }
                    allocations[item.InvoiceId] = existing;
                }
                else
                {
                    var allocation = new ReceiptAllocation
                    {
                        IdBankReceipt = receiptId, IdInvoice = item.InvoiceId, TargetType = "INVOICE",
                        AllocatedAmount = item.AllocatedAmount, IdCurrency = receipt.IdCurrency,
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
            foreach (var item in items)
            {
                var amount = await _db.ReceiptAllocations.AsNoTracking().Where(x => x.IsActive && x.IdInvoice == item.InvoiceId)
                    .SumAsync(x => x.AllocatedAmount ?? 0m, ct);
                var recovery = InvoiceRecoveryCalculator.Calculate(invoices[item.InvoiceId].GrossPremium ?? 0m, amount).Status;
                await _db.Invoices.Where(x => x.IdInvoice == item.InvoiceId).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RecoveryStatus, recovery).SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.UpdatedBy, user), ct);
            }
            await GeneratePanelRemittancesAsync(receipt, user, ct);
            var operation = new InvoicePairingOperation
            {
                ReceiptId = receiptId, RequestHash = hash, CreatedAt = now, CreatedBy = user,
                TotalAllocated = total, RemainingAmount = Math.Max(0m, receipt.AmountReceived - total), PairingStatus = status,
                Lines = items.OrderBy(x => x.InvoiceId).Select(x => new InvoicePairingOperationLine
                {
                    ReceiptAllocationId = allocations[x.InvoiceId].IdReceiptAllocation, RequestedAmount = x.AllocatedAmount
                }).ToList()
            };
            _db.InvoicePairingOperations.Add(operation);
            await _db.Notifications.Where(x => x.RecipientUserId == user && x.EntityType == "BANK_RECEIPT" && x.EntityId == receiptId && !x.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAt, now), ct);
            await _db.SaveChangesAsync(ct);
            var persisted = await _db.InvoicePairingOperations.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.Allocation)
                .SingleAsync(x => x.Id == operation.Id, ct);
            await tx.CommitAsync(ct);
            return ServiceResult<ReceiptBatchAllocationResponseDto>.Success(ToPairingResponse(persisted), legacyRetry ? 200 : 201);
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
        ReceiptId = operation.ReceiptId, TotalAllocated = operation.TotalAllocated,
        RemainingAmount = operation.RemainingAmount, PairingStatus = operation.PairingStatus,
        Allocations = operation.Lines.OrderBy(x => x.Allocation.IdInvoice).Select(x => new ReceiptBatchAllocationItemDto
        {
            IdReceiptAllocation = x.ReceiptAllocationId, InvoiceId = x.Allocation.IdInvoice, AllocatedAmount = x.RequestedAmount
        }).ToList()
    };
}
