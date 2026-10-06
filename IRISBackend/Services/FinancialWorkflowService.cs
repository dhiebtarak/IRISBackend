using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Globalization;
using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;

namespace IRISBackend.Services;

public sealed class FinancialWorkflowService : IFinancialWorkflowService
{
    private const string CedentPremiumInvoiceType = "CEDENT_PREMIUM_INVOICE";
    private readonly IRISBackendContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<FinancialWorkflowService> _logger;

    public FinancialWorkflowService(IRISBackendContext db, IHttpContextAccessor http, ILogger<FinancialWorkflowService> logger)
    {
        _db = db;
        _http = http;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<BankAccountResponseDto>>> GetBankAccountsAsync(int counterpartyId, bool activeOnly, CancellationToken cancellationToken)
    {
        if (!CanFinanceOrBack()) return Forbidden<IReadOnlyList<BankAccountResponseDto>>();
        if (!await _db.Counterparties.AnyAsync(x => x.Id == counterpartyId, cancellationToken)) return NotFound<IReadOnlyList<BankAccountResponseDto>>("Counterparty was not found.");
        var accounts = _db.CounterpartyBankAccounts.AsNoTracking().Where(x => x.IdCounterparty == counterpartyId);
        if (activeOnly) accounts = accounts.Where(x => x.IsActive);
        var items = await accounts
            .OrderByDescending(x => x.IsActive).ThenByDescending(x => x.IsDefault).ThenBy(x => x.BankName)
            .Select(MapBankAccountExpression).ToListAsync(cancellationToken);
        return ServiceResult<IReadOnlyList<BankAccountResponseDto>>.Success(items);
    }

    public async Task<ServiceResult<PagedResponseDto<BankAccountOptionDto>>> GetBankAccountOptionsAsync(BankAccountOptionQueryDto query, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<PagedResponseDto<BankAccountOptionDto>>();

        var page = Math.Clamp(query.PageNumber, 1, int.MaxValue);
        var size = Math.Clamp(query.PageSize, 1, 50);
        var accounts = _db.CounterpartyBankAccounts.AsNoTracking()
            .Where(x => x.IsActive && x.IdCounterpartyNavigation.IsActive);

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = search.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            var text = search.ToLowerInvariant();
            accounts = accounts.Where(x =>
                x.IdCounterpartyNavigation.Name.ToLower().Contains(text) ||
                x.BankName.ToLower().Contains(text) ||
                (x.Iban != null && x.Iban.Replace(" ", "").ToLower().Contains(normalized)) ||
                (x.SwiftBic != null && x.SwiftBic.ToLower().Contains(text)) ||
                x.IdCurrencyNavigation.CurrencyCode.ToLower().Contains(text));
        }

        var total = await accounts.CountAsync(cancellationToken);
        var items = await accounts
            .OrderBy(x => x.IdCounterpartyNavigation.Name)
            .ThenBy(x => x.BankName)
            .Select(x => new BankAccountOptionDto
            {
                IdBankAccount = x.IdBankAccount,
                CounterpartyId = x.IdCounterparty,
                BeneficiaryName = x.IdCounterpartyNavigation.Name,
                BankName = x.BankName,
                BankCity = x.BankCity,
                BankCountry = x.BankCountry,
                MaskedIban = x.Iban == null || x.Iban == "" ? null : x.Iban.Length <= 4 ? x.Iban : x.Iban.Substring(0, 2) + "****" + x.Iban.Substring(x.Iban.Length - 4, 4),
                SwiftBic = x.SwiftBic,
                CurrencyId = x.IdCurrency,
                CurrencyCode = x.IdCurrencyNavigation.CurrencyCode,
                IsActive = x.IsActive
            })
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return ServiceResult<PagedResponseDto<BankAccountOptionDto>>.Success(new PagedResponseDto<BankAccountOptionDto>
        {
            Items = items,
            PageNumber = page,
            PageSize = size,
            TotalCount = total
        });
    }

    public async Task<ServiceResult<BankAccountResponseDto>> CreateBankAccountAsync(int counterpartyId, BankAccountRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<BankAccountResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<BankAccountResponseDto>();
        if (!await _db.Counterparties.AnyAsync(x => x.Id == counterpartyId, cancellationToken)) return NotFound<BankAccountResponseDto>("Counterparty was not found.");
        var validation = await ValidateBankAccountAsync(request, cancellationToken);
        if (validation is not null) return ServiceResult<BankAccountResponseDto>.Failure(validation);
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        if (request.IsDefault && request.IsActive) await ClearDefaultAsync(counterpartyId, request.CurrencyId, null, user, cancellationToken);
        var account = new CounterpartyBankAccount
        {
            IdCounterparty = counterpartyId, IdCurrency = request.CurrencyId, BankName = request.BankName.Trim(),
            BankCity = request.BankCity.Trim(), BankCountry = request.BankCountry.Trim(),
            AgencyCode = Optional(request.AgencyCode), Rib = Optional(request.Rib), Iban = NormalizeIban(request.Iban), SwiftBic = NormalizeBic(request.SwiftBic),
            HasCorrespondentBank = request.HasCorrespondentBank, CorrespondentBankName = request.HasCorrespondentBank ? Optional(request.CorrespondentBankName) : null, CorrespondentSwiftBic = request.HasCorrespondentBank ? NormalizeBic(request.CorrespondentSwiftBic) : null,
            AccountLabel = Optional(request.AccountLabel), IsDefault = request.IsDefault && request.IsActive, IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow, CreatedBy = user
        };
        _db.CounterpartyBankAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await _db.Entry(account).Reference(x => x.IdCurrencyNavigation).LoadAsync(cancellationToken);
        await _db.Entry(account).Reference(x => x.IdCounterpartyNavigation).LoadAsync(cancellationToken);
        return ServiceResult<BankAccountResponseDto>.Success(MapBankAccount(account), StatusCodes.Status201Created);
    }

    public async Task<ServiceResult<BankAccountResponseDto>> UpdateBankAccountAsync(int counterpartyId, int bankAccountId, BankAccountRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<BankAccountResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<BankAccountResponseDto>();
        var account = await _db.CounterpartyBankAccounts.FirstOrDefaultAsync(x => x.IdCounterparty == counterpartyId && x.IdBankAccount == bankAccountId, cancellationToken);
        if (account is null) return NotFound<BankAccountResponseDto>("Bank account was not found.");
        var validation = await ValidateBankAccountAsync(request, cancellationToken);
        if (validation is not null) return ServiceResult<BankAccountResponseDto>.Failure(validation);
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        if (request.IsDefault && request.IsActive) await ClearDefaultAsync(counterpartyId, request.CurrencyId, account.IdBankAccount, user, cancellationToken);
        account.IdCurrency = request.CurrencyId; account.BankName = request.BankName.Trim(); account.BankCity = request.BankCity.Trim(); account.BankCountry = request.BankCountry.Trim(); account.AgencyCode = Optional(request.AgencyCode);
        account.Rib = Optional(request.Rib); account.Iban = NormalizeIban(request.Iban); account.SwiftBic = NormalizeBic(request.SwiftBic); account.HasCorrespondentBank = request.HasCorrespondentBank; account.CorrespondentBankName = request.HasCorrespondentBank ? Optional(request.CorrespondentBankName) : null; account.CorrespondentSwiftBic = request.HasCorrespondentBank ? NormalizeBic(request.CorrespondentSwiftBic) : null; account.AccountLabel = Optional(request.AccountLabel);
        account.IsActive = request.IsActive; account.IsDefault = request.IsDefault && request.IsActive; account.UpdatedAt = DateTime.UtcNow; account.UpdatedBy = user;
        await _db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        await _db.Entry(account).Reference(x => x.IdCurrencyNavigation).LoadAsync(cancellationToken);
        await _db.Entry(account).Reference(x => x.IdCounterpartyNavigation).LoadAsync(cancellationToken);
        return ServiceResult<BankAccountResponseDto>.Success(MapBankAccount(account));
    }

    public async Task<ServiceResult<bool>> SetBankAccountStatusAsync(int counterpartyId, int bankAccountId, bool isActive, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<bool>();
        var user = CurrentUser(); if (user is null) return Unauthorized<bool>();
        var account = await _db.CounterpartyBankAccounts.FirstOrDefaultAsync(x => x.IdCounterparty == counterpartyId && x.IdBankAccount == bankAccountId, cancellationToken);
        if (account is null) return NotFound<bool>("Bank account was not found.");
        account.IsActive = isActive; if (!isActive) account.IsDefault = false; account.UpdatedAt = DateTime.UtcNow; account.UpdatedBy = user;
        await _db.SaveChangesAsync(cancellationToken); return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<PagedResponseDto<BankReceiptResponseDto>>> ListReceiptsAsync(FinancialReceiptQueryDto query, bool backOffice, CancellationToken cancellationToken)
    {
        if (!CanFinanceOrBack()) return Forbidden<PagedResponseDto<BankReceiptResponseDto>>();
        var current = _db.BankReceipts.AsNoTracking().Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation).Include(x => x.ReceiptAllocations).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search)) current = current.Where(x => x.BankReference.Contains(query.Search) || x.SettlementLabel.Contains(query.Search));
        if (query.CounterpartyId.HasValue) current = current.Where(x => x.IdCounterparty == query.CounterpartyId);
        if (query.CurrencyId.HasValue) current = current.Where(x => x.IdCurrency == query.CurrencyId);
        if (query.OperationFrom.HasValue) current = current.Where(x => x.OperationDate >= query.OperationFrom);
        if (query.OperationTo.HasValue) current = current.Where(x => x.OperationDate <= query.OperationTo);
        var projected = current.Select(ToReceiptExpression);
        if (!string.IsNullOrWhiteSpace(query.PairingStatus)) projected = projected.Where(x => x.PairingStatus == query.PairingStatus);
        var total = await projected.CountAsync(cancellationToken);
        var ordered = query.SortBy?.ToLowerInvariant() switch
        {
            "operationdate" => query.SortDirection?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true ? projected.OrderByDescending(x => x.OperationDate) : projected.OrderBy(x => x.OperationDate),
            "amountreceived" => projected.OrderByDescending(x => x.AmountReceived),
            _ => projected.OrderBy(x => x.PairingStatus == "Paired").ThenByDescending(x => x.OperationDate)
        };
        var page = Math.Clamp(query.PageNumber, 1, int.MaxValue); var size = Math.Clamp(query.PageSize, 1, 100);
        var items = await ordered.Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return ServiceResult<PagedResponseDto<BankReceiptResponseDto>>.Success(new PagedResponseDto<BankReceiptResponseDto> { Items = items, PageNumber = page, PageSize = size, TotalCount = total });
    }

    public async Task<ServiceResult<BankReceiptResponseDto>> GetReceiptAsync(int id, CancellationToken cancellationToken)
    {
        if (!CanFinanceOrBack()) return Forbidden<BankReceiptResponseDto>();
        var entity = await _db.BankReceipts.AsNoTracking().Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation).Include(x => x.IdBankAccountNavigation).Include(x => x.ReceiptAllocations).FirstOrDefaultAsync(x => x.IdBankReceipt == id, cancellationToken);
        return entity is null ? NotFound<BankReceiptResponseDto>("Bank receipt was not found.") : ServiceResult<BankReceiptResponseDto>.Success(ToReceiptEntity(entity));
    }

    public async Task<ServiceResult<BankReceiptResponseDto>> CreateReceiptAsync(BankReceiptRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<BankReceiptResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<BankReceiptResponseDto>();
        if (request.AmountReceived <= 0) return Failure<BankReceiptResponseDto>("Amount received must be positive.");
        if (string.IsNullOrWhiteSpace(request.SettlementMethod)) return Failure<BankReceiptResponseDto>("Settlement method is required.");
        if (string.IsNullOrWhiteSpace(request.CollectionType)) return Failure<BankReceiptResponseDto>("Collection type is required.");
        DateOnly? declaredInvoiceMonth = null;
        if (!string.IsNullOrWhiteSpace(request.DeclaredInvoiceMonth))
        {
            if (!DateTime.TryParseExact(request.DeclaredInvoiceMonth.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var declaredMonth)) return Failure<BankReceiptResponseDto>("Declared invoice month must use the YYYY-MM format.");
            declaredInvoiceMonth = new DateOnly(declaredMonth.Year, declaredMonth.Month, 1);
        }
        if (!DateTime.TryParseExact(request.ReceiptMonth?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) return Failure<BankReceiptResponseDto>("Receipt month must use the YYYY-MM format.");
        var operationMonth = DateOnly.FromDateTime(month).AddDays(1 - DateOnly.FromDateTime(month).Day);
        var currentMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        if (operationMonth > currentMonth.AddMonths(1)) return Failure<BankReceiptResponseDto>("Receipt month cannot be unreasonably future-dated.");
        var account = await _db.CounterpartyBankAccounts
            .Include(x => x.IdCounterpartyNavigation)
            .Include(x => x.IdCurrencyNavigation)
            .FirstOrDefaultAsync(x => x.IdBankAccount == request.BankAccountId && x.IsActive && x.IdCounterpartyNavigation.IsActive && x.IdCurrencyNavigation.IsActive, cancellationToken);
        if (account is null) return Failure<BankReceiptResponseDto>("An active bank account for an active counterparty is required.", status: StatusCodes.Status400BadRequest);
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var businessReference = Optional(request.BusinessReference) ?? Optional(request.SettlementLabel);
        var receipt = new BankReceipt { IdBankAccount = account.IdBankAccount, IdCounterparty = account.IdCounterparty, ReceptionDate = DateOnly.FromDateTime(now), OperationDate = operationMonth, AmountReceived = request.AmountReceived, IdCurrency = account.IdCurrency, AppliedExchangeRate = request.AppliedExchangeRate, SettlementMethod = request.SettlementMethod.Trim(), CollectionType = request.CollectionType.Trim(), BankReference = Optional(request.BankReference), SettlementLabel = businessReference, BusinessReference = businessReference, DeclaredInvoiceNumber = Optional(request.DeclaredInvoiceNumber), DeclaredInvoiceMonth = declaredInvoiceMonth, Reason = Optional(request.Reason), DetailsStatus = "Unpaired", CreatedAt = now, CreatedBy = user };
        _db.BankReceipts.Add(receipt); await _db.SaveChangesAsync(cancellationToken);
        await NotifyRoleAsync("Back Officer", "New incoming receipt", "New incoming receipt requires pairing.", "BANK_RECEIPT", receipt.IdBankReceipt, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        var result = await GetReceiptAsync(receipt.IdBankReceipt, cancellationToken); return result.StatusCode == 200 ? ServiceResult<BankReceiptResponseDto>.Success(result.Value!, StatusCodes.Status201Created) : result;
    }

    public async Task<ServiceResult<IReadOnlyList<EligibleContractDto>>> GetEligibleContractsAsync(int receiptId, string? search, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<IReadOnlyList<EligibleContractDto>>();
        var receipt = await _db.BankReceipts.AsNoTracking().FirstOrDefaultAsync(x => x.IdBankReceipt == receiptId, cancellationToken);
        if (receipt is null) return NotFound<IReadOnlyList<EligibleContractDto>>("Bank receipt was not found.");

        var contracts = _db.Contracts.AsNoTracking()
            .Where(x => x.Status != null && x.Status.Trim().ToLower() == ContractConstants.BoundStatus.ToLower());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            contracts = contracts.Where(x =>
                (x.Umr != null && x.Umr.ToLower().Contains(term)) ||
                x.Name.ToLower().Contains(term) ||
                x.IdCounterpartyNavigation.Name.ToLower().Contains(term));
        }

        var items = await contracts.OrderBy(x => x.Name).Select(x => new EligibleContractDto
        {
            ContractId = x.IdContract,
            Umr = x.Umr,
            ContractName = x.Name,
            CedentId = x.IdCounterparty,
            CedentName = x.IdCounterpartyNavigation.Name,
            Status = ContractConstants.BoundStatus,
            CurrencyCode = x.IdCurrencyNavigation.CurrencyCode,
            EligibleReinsurerCount = x.ContractPanels.Count(panel => panel.Status != null && panel.Status.Trim().ToLower() == ContractConstants.ConfirmedPanelStatus.ToLower())
        }).ToListAsync(cancellationToken);
        return ServiceResult<IReadOnlyList<EligibleContractDto>>.Success(items);
    }

    public async Task<ServiceResult<IReadOnlyList<EligibleInvoiceDto>>> GetEligibleInvoicesAsync(int receiptId, string? search, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<IReadOnlyList<EligibleInvoiceDto>>();
        var receipt = await _db.BankReceipts.AsNoTracking()
            .FirstOrDefaultAsync(item => item.IdBankReceipt == receiptId, cancellationToken);
        if (receipt is null) return NotFound<IReadOnlyList<EligibleInvoiceDto>>("Bank receipt was not found.");

        var invoices = _db.Invoices.AsNoTracking()
            .Where(invoice => invoice.InvoiceType != null && invoice.InvoiceType.Trim().ToLower() == CedentPremiumInvoiceType.ToLower()
                && invoice.Status != null && invoice.Status.Trim().ToLower() != "cancelled"
                && invoice.IdCurrency == receipt.IdCurrency
                && invoice.IdContractNavigation.IdCounterparty == receipt.IdCounterparty
                && (invoice.GrossPremium ?? 0m) > _db.ReceiptAllocations
                    .Where(allocation => allocation.IdInvoice == invoice.IdInvoice)
                    .Sum(allocation => allocation.AllocatedAmount ?? 0m));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            invoices = invoices.Where(invoice => invoice.InvoiceNumber.ToLower().Contains(term)
                || (invoice.Label != null && invoice.Label.ToLower().Contains(term))
                || invoice.IdContractNavigation.Name.ToLower().Contains(term)
                || (invoice.IdContractNavigation.Umr != null && invoice.IdContractNavigation.Umr.ToLower().Contains(term))
                || invoice.IdContractNavigation.IdCounterpartyNavigation.Name.ToLower().Contains(term));
        }

        var items = await invoices
            .OrderBy(invoice => invoice.InvoiceDate)
            .Select(invoice => new EligibleInvoiceDto
            {
                InvoiceId = invoice.IdInvoice,
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceType = invoice.InvoiceType,
                Label = invoice.Label,
                ContractId = invoice.IdContract,
                ContractName = invoice.IdContractNavigation.Name,
                Umr = invoice.IdContractNavigation.Umr,
                CedentId = invoice.IdContractNavigation.IdCounterparty,
                CedentName = invoice.IdContractNavigation.IdCounterpartyNavigation.Name,
                GrossAmount = invoice.GrossPremium ?? 0m,
                AlreadyPairedAmount = _db.ReceiptAllocations.Where(allocation => allocation.IdInvoice == invoice.IdInvoice).Sum(allocation => allocation.AllocatedAmount ?? 0m),
                OutstandingAmount = (invoice.GrossPremium ?? 0m) - _db.ReceiptAllocations.Where(allocation => allocation.IdInvoice == invoice.IdInvoice).Sum(allocation => allocation.AllocatedAmount ?? 0m),
                CurrencyCode = invoice.IdCurrencyNavigation == null ? string.Empty : invoice.IdCurrencyNavigation.CurrencyCode
            })
            .ToListAsync(cancellationToken);
        return ServiceResult<IReadOnlyList<EligibleInvoiceDto>>.Success(items);
    }

    public async Task<ServiceResult<ReceiptBatchAllocationResponseDto>> AllocateReceiptInvoicesAsync(int receiptId, InvoiceReceiptAllocationRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<ReceiptBatchAllocationResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<ReceiptBatchAllocationResponseDto>();
        var items = request.Allocations ?? [];
        if (items.Count == 0) return Failure<ReceiptBatchAllocationResponseDto>("At least one invoice allocation is required.");
        if (items.Any(item => item.InvoiceId <= 0 || item.AllocatedAmount <= 0m))
            return Failure<ReceiptBatchAllocationResponseDto>("Each allocation requires a positive invoiceId and allocatedAmount.");
        var invoiceIds = items.Select(item => item.InvoiceId).ToArray();
        if (invoiceIds.Distinct().Count() != invoiceIds.Length)
            return Failure<ReceiptBatchAllocationResponseDto>("Invoice IDs must be unique in a batch allocation.");

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var receipt = await _db.BankReceipts.Include(item => item.ReceiptAllocations)
                .FirstOrDefaultAsync(item => item.IdBankReceipt == receiptId, cancellationToken);
            if (receipt is null) return NotFound<ReceiptBatchAllocationResponseDto>("Bank receipt was not found.");

            var invoices = await _db.Invoices
                .Include(invoice => invoice.IdContractNavigation).ThenInclude(contract => contract.IdCounterpartyNavigation)
                .Include(invoice => invoice.IdCurrencyNavigation)
                .Where(invoice => invoiceIds.Contains(invoice.IdInvoice))
                .ToListAsync(cancellationToken);
            if (invoices.Count != invoiceIds.Length)
                return NotFound<ReceiptBatchAllocationResponseDto>("One or more invoices were not found.");

            var priorRows = await _db.ReceiptAllocations
                .Where(allocation => allocation.IdBankReceipt == receiptId && allocation.IdInvoice.HasValue && invoiceIds.Contains(allocation.IdInvoice.Value))
                .ToListAsync(cancellationToken);
            if (priorRows.Count > 0)
            {
                var exactRetry = priorRows.Count == items.Count && items.All(item =>
                    priorRows.Any(row => row.IdInvoice == item.InvoiceId && row.AllocatedAmount == item.AllocatedAmount));
                if (!exactRetry)
                    return Failure<ReceiptBatchAllocationResponseDto>("This receipt already has an allocation for one or more selected invoices.", status: StatusCodes.Status409Conflict);
                var totalExisting = receipt.ReceiptAllocations.Sum(allocation => allocation.AllocatedAmount ?? 0m);
                await transaction.CommitAsync(cancellationToken);
                return ServiceResult<ReceiptBatchAllocationResponseDto>.Success(new ReceiptBatchAllocationResponseDto
                {
                    ReceiptId = receiptId,
                    Allocations = priorRows.Select(row => new ReceiptBatchAllocationItemDto { IdReceiptAllocation = row.IdReceiptAllocation, InvoiceId = row.IdInvoice, AllocatedAmount = row.AllocatedAmount ?? 0m }).ToList(),
                    TotalAllocated = totalExisting,
                    RemainingAmount = Math.Max(0m, receipt.AmountReceived - totalExisting),
                    PairingStatus = InvoiceRecoveryCalculator.GetReceiptPairingStatus(receipt.AmountReceived, totalExisting)
                });
            }

            var existingReceiptAmount = receipt.ReceiptAllocations.Sum(allocation => allocation.AllocatedAmount ?? 0m);
            var newReceiptAmount = items.Sum(item => item.AllocatedAmount);
            if (existingReceiptAmount + newReceiptAmount > receipt.AmountReceived)
                return Failure<ReceiptBatchAllocationResponseDto>("Allocation exceeds the encaissement balance.", status: StatusCodes.Status409Conflict);

            var pairedByInvoice = await _db.ReceiptAllocations.AsNoTracking()
                .Where(allocation => allocation.IdInvoice.HasValue && invoiceIds.Contains(allocation.IdInvoice.Value))
                .GroupBy(allocation => allocation.IdInvoice!.Value)
                .Select(group => new { InvoiceId = group.Key, Amount = group.Sum(allocation => allocation.AllocatedAmount ?? 0m) })
                .ToDictionaryAsync(group => group.InvoiceId, group => group.Amount, cancellationToken);

            foreach (var item in items)
            {
                var invoice = invoices.Single(value => value.IdInvoice == item.InvoiceId);
                if (!string.Equals(invoice.InvoiceType, CedentPremiumInvoiceType, StringComparison.OrdinalIgnoreCase))
                    return Failure<ReceiptBatchAllocationResponseDto>("Only cedent premium invoices can be paired with an incoming encaissement.");
                if (string.Equals(invoice.Status?.Trim(), "Cancelled", StringComparison.OrdinalIgnoreCase))
                    return Failure<ReceiptBatchAllocationResponseDto>("Cancelled invoices cannot receive allocations.", status: StatusCodes.Status409Conflict);
                if (invoice.IdContractNavigation.IdCounterparty != receipt.IdCounterparty)
                    return Failure<ReceiptBatchAllocationResponseDto>("The invoice cedent does not match the encaissement counterparty.");
                if (invoice.IdCurrency != receipt.IdCurrency)
                    return Failure<ReceiptBatchAllocationResponseDto>("The invoice currency does not match the encaissement currency.");
                var previouslyPaired = pairedByInvoice.GetValueOrDefault(invoice.IdInvoice);
                if (previouslyPaired + item.AllocatedAmount > (invoice.GrossPremium ?? 0m))
                    return Failure<ReceiptBatchAllocationResponseDto>($"Allocation exceeds the outstanding amount for invoice {invoice.InvoiceNumber}.", status: StatusCodes.Status409Conflict);
            }

            var created = new List<ReceiptBatchAllocationItemDto>();
            foreach (var item in items)
            {
                var allocation = new ReceiptAllocation
                {
                    IdBankReceipt = receiptId,
                    IdInvoice = item.InvoiceId,
                    TargetType = "INVOICE",
                    AllocatedAmount = item.AllocatedAmount,
                    IdCurrency = receipt.IdCurrency,
                    ExchangeRate = 1m,
                    PairedAt = DateTime.UtcNow,
                    IdUser = user
                };
                _db.ReceiptAllocations.Add(allocation);
                created.Add(new ReceiptBatchAllocationItemDto { InvoiceId = item.InvoiceId, AllocatedAmount = item.AllocatedAmount });
            }
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var item in items)
            {
                var invoice = invoices.Single(value => value.IdInvoice == item.InvoiceId);
                var paired = pairedByInvoice.GetValueOrDefault(invoice.IdInvoice) + item.AllocatedAmount;
                invoice.RecoveryStatus = InvoiceRecoveryCalculator.Calculate(invoice.GrossPremium ?? 0m, paired).Status;
                invoice.UpdatedAt = DateTime.UtcNow;
                invoice.UpdatedBy = user;
                var row = created.Single(value => value.InvoiceId == item.InvoiceId);
                row.IdReceiptAllocation = _db.ReceiptAllocations.Local.Single(value => value.IdInvoice == item.InvoiceId && value.IdBankReceipt == receiptId).IdReceiptAllocation;
            }

            var totalAllocated = existingReceiptAmount + newReceiptAmount;
            receipt.DetailsStatus = InvoiceRecoveryCalculator.GetReceiptPairingStatus(receipt.AmountReceived, totalAllocated);
            receipt.UpdatedAt = DateTime.UtcNow;
            receipt.UpdatedBy = user;
            await _db.Notifications
                .Where(notification => notification.RecipientUserId == user && notification.EntityType == "BANK_RECEIPT" && notification.EntityId == receiptId && !notification.IsRead)
                .ExecuteUpdateAsync(setters => setters.SetProperty(notification => notification.IsRead, true).SetProperty(notification => notification.ReadAt, DateTime.UtcNow), cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<ReceiptBatchAllocationResponseDto>.Success(new ReceiptBatchAllocationResponseDto
            {
                ReceiptId = receiptId,
                Allocations = created,
                TotalAllocated = totalAllocated,
                RemainingAmount = Math.Max(0m, receipt.AmountReceived - totalAllocated),
                PairingStatus = receipt.DetailsStatus
            });
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ServiceResult<BankReceiptResponseDto>> AllocateReceiptAsync(int receiptId, ReceiptAllocationRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<BankReceiptResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<BankReceiptResponseDto>();
        if (request.AllocatedAmount <= 0) return Failure<BankReceiptResponseDto>("Allocated amount must be positive.");
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
        _logger.LogInformation("Receipt allocation stage=TransactionStarted ReceiptId={ReceiptId} ContractId={ContractId} UserId={UserId}", receiptId, request.ContractId, user);
        var receipt = await _db.BankReceipts.Include(x => x.ReceiptAllocations).FirstOrDefaultAsync(x => x.IdBankReceipt == receiptId, cancellationToken);
        if (receipt is null) return NotFound<BankReceiptResponseDto>("Bank receipt was not found.");
        _logger.LogInformation("Receipt allocation stage=ReceiptLoaded ReceiptId={ReceiptId} CurrencyId={CurrencyId}", receiptId, receipt.IdCurrency);
        var contract = await _db.Contracts.Include(x => x.ContractPanels).FirstOrDefaultAsync(x => x.IdContract == request.ContractId, cancellationToken);
        if (contract is null) return NotFound<BankReceiptResponseDto>("Contract was not found.");
        _logger.LogInformation("Receipt allocation stage=ContractLoaded ReceiptId={ReceiptId} ContractId={ContractId} Status={Status}", receiptId, contract.IdContract, contract.Status);
        if (!string.Equals(ContractConstants.NormalizeStatus(contract.Status), ContractConstants.BoundStatus, StringComparison.OrdinalIgnoreCase)) return Failure<BankReceiptResponseDto>("Only Bound contracts can receive an encaissement allocation.", status: StatusCodes.Status409Conflict);
        if (contract.IdCounterparty != receipt.IdCounterparty) return Failure<BankReceiptResponseDto>("The contract cedent does not match the encaissement counterparty.");
        if (contract.IdCurrency != receipt.IdCurrency) return Failure<BankReceiptResponseDto>("The contract currency does not match the encaissement currency.");
        _logger.LogInformation("Receipt allocation stage=BusinessValidationPassed ReceiptId={ReceiptId} ContractId={ContractId}", receiptId, contract.IdContract);
        var panels = contract.ContractPanels.Where(x => x.Status != null && x.Status.Trim().ToLower() == ContractConstants.ConfirmedPanelStatus.ToLower() && string.Equals(x.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase) && x.SignedSharePct.HasValue && x.SignedSharePct.Value > 0m).ToList();
        _logger.LogInformation("Receipt allocation stage=EligiblePanelsLoaded ReceiptId={ReceiptId} ContractId={ContractId} PanelCount={PanelCount}", receiptId, contract.IdContract, panels.Count);
        if (panels.Count == 0) return Failure<BankReceiptResponseDto>("The selected Contract has no confirmed reinsurer panels eligible for allocation.");
        var panelCounterpartyIds = panels.Select(x => x.IdCounterparty).Distinct().ToArray();
        var validPanelCounterpartyIds = await _db.Counterparties.AsNoTracking()
            .Where(x => panelCounterpartyIds.Contains(x.Id) && x.IsActive &&
                        x.TypeAssignments.Any(t => t.Type.Trim().ToLower() == CounterpartyTypes.Reinsurer.ToLower()))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (validPanelCounterpartyIds.Count != panelCounterpartyIds.Length)
            return Failure<BankReceiptResponseDto>("One or more confirmed reinsurer panels reference an invalid or inactive reinsurer counterparty.");
        var totalShare = panels.Sum(x => x.SignedSharePct!.Value);
        if (totalShare > 100m) return Failure<BankReceiptResponseDto>("Confirmed reinsurer shares exceed 100 percent.");
        var allocated = receipt.ReceiptAllocations.Sum(x => x.AllocatedAmount ?? 0m);
        if (allocated + request.AllocatedAmount > receipt.AmountReceived) return Failure<BankReceiptResponseDto>("Allocation exceeds the encaissement balance.", status: StatusCodes.Status409Conflict);
        // TargetType describes the incoming business flow; IdContract is the authoritative contract target.
        var allocation = new ReceiptAllocation { IdBankReceipt = receiptId, IdContract = contract.IdContract, TargetType = "PREMIUM", AllocatedAmount = request.AllocatedAmount, IdCurrency = receipt.IdCurrency, ExchangeRate = 1m, PairedAt = DateTime.UtcNow, IdUser = user };
        _db.ReceiptAllocations.Add(allocation);
        _logger.LogInformation("Receipt allocation stage=AllocationPrepared ReceiptId={ReceiptId} ContractId={ContractId} CurrencyId={CurrencyId} UserId={UserId}", receiptId, contract.IdContract, allocation.IdCurrency, allocation.IdUser);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception,
                "Receipt allocation insert failed. Operation=RECEIPT_ALLOCATION INSERT, ReceiptId={ReceiptId}, ContractId={ContractId}, CurrencyId={CurrencyId}, UserId={UserId}, TargetType={TargetType}, InnerSqlException={InnerSqlException}",
                receiptId, contract.IdContract, receipt.IdCurrency, user, allocation.TargetType, exception.InnerException?.ToString());
            throw;
        }
        foreach (var panel in panels)
        {
            var gross = decimal.Round(request.AllocatedAmount * panel.SignedSharePct!.Value / 100m, 4, MidpointRounding.AwayFromZero);
            var brokerage = decimal.Round(gross * (panel.BrokeragePct ?? 0m) / 100m, 4, MidpointRounding.AwayFromZero);
            var deductions = brokerage + (panel.OtherDeductions ?? 0m);
            var net = Math.Max(0m, decimal.Round(gross - deductions, 4, MidpointRounding.AwayFromZero));
            if (!await _db.RemittanceRefs.AnyAsync(x => x.IdReceiptAllocation == allocation.IdReceiptAllocation && x.IdPanel == panel.IdPanel, cancellationToken))
                _db.RemittanceRefs.Add(new RemittanceRef { IdReceiptAllocation = allocation.IdReceiptAllocation, IdPanel = panel.IdPanel, IdCounterparty = panel.IdCounterparty, IdCurrency = receipt.IdCurrency, SourceType = "Premium", PremiumSignedLine = gross, Deductions = deductions, NetPremiumDue = net, NetPayableAmount = net, Status = "Draft", SignatureStatus = "Draft", Description = "Generated from contract encaissement allocation", CreatedAt = DateTime.UtcNow, CreatedBy = user });
        }
        _logger.LogInformation("Receipt allocation stage=DraftRemittancesPrepared ReceiptId={ReceiptId} AllocationId={AllocationId} PanelCount={PanelCount}", receiptId, allocation.IdReceiptAllocation, panels.Count);
        receipt.DetailsStatus = InvoiceRecoveryCalculator.GetReceiptPairingStatus(receipt.AmountReceived, allocated + request.AllocatedAmount);
        receipt.UpdatedAt = DateTime.UtcNow; receipt.UpdatedBy = user;
        await _db.Notifications
            .Where(x => x.RecipientUserId == user && x.EntityType == "BANK_RECEIPT" && x.EntityId == receiptId && !x.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAt, DateTime.UtcNow), cancellationToken);
        _logger.LogInformation("Receipt allocation stage=NotificationPrepared ReceiptId={ReceiptId} AllocationId={AllocationId}", receiptId, allocation.IdReceiptAllocation);
        _logger.LogInformation("Receipt allocation stage=FinalSaveStarted ReceiptId={ReceiptId} AllocationId={AllocationId}", receiptId, allocation.IdReceiptAllocation);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(exception,
                "Receipt allocation workflow final save failed. Operation=REMITTANCE_REF INSERT / BANK_RECEIPT UPDATE / NOTIFICATION UPDATE, ReceiptId={ReceiptId}, ContractId={ContractId}, AllocationId={AllocationId}, PanelIds={PanelIds}, CurrencyId={CurrencyId}, UserId={UserId}, InnerSqlException={InnerSqlException}",
                receiptId, contract.IdContract, allocation.IdReceiptAllocation, string.Join(",", panels.Select(x => x.IdPanel)), receipt.IdCurrency, user, exception.InnerException?.ToString());
            throw;
        }
        await tx.CommitAsync(cancellationToken);
        _logger.LogInformation("Receipt allocation stage=TransactionCommitted ReceiptId={ReceiptId} ContractId={ContractId} AllocationId={AllocationId}", receiptId, contract.IdContract, allocation.IdReceiptAllocation);
        return await GetReceiptAsync(receiptId, cancellationToken);
        }
        catch (Exception exception)
        {
            try { await tx.RollbackAsync(cancellationToken); }
            catch (Exception rollbackException) { _logger.LogError(rollbackException, "Receipt allocation rollback failed. ReceiptId={ReceiptId} ContractId={ContractId}", receiptId, request.ContractId); }
            _logger.LogError(exception,
                "Receipt allocation stage=TransactionRolledBack ReceiptId={ReceiptId} ContractId={ContractId} UserId={UserId} ExceptionType={ExceptionType} InnerException={InnerException}",
                receiptId, request.ContractId, user, exception.GetType().FullName, exception.InnerException?.ToString());
            throw;
        }
    }

    public async Task<ServiceResult<ReceiptBatchAllocationResponseDto>> AllocateReceiptBatchAsync(
        int receiptId,
        ReceiptAllocationRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<ReceiptBatchAllocationResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<ReceiptBatchAllocationResponseDto>();

        var items = request.Allocations ?? [];
        if (items.Count == 0)
        {
            return Failure<ReceiptBatchAllocationResponseDto>("At least one contract allocation is required.");
        }

        if (items.Any(item => item.ContractId <= 0 || item.AllocatedAmount <= 0m))
        {
            return Failure<ReceiptBatchAllocationResponseDto>("Each allocation requires a positive contractId and allocatedAmount.");
        }

        if (items.Select(item => item.ContractId).Distinct().Count() != items.Count)
        {
            return Failure<ReceiptBatchAllocationResponseDto>("Contract IDs must be unique in a batch allocation.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var receipt = await _db.BankReceipts
                .Include(item => item.ReceiptAllocations)
                .FirstOrDefaultAsync(item => item.IdBankReceipt == receiptId, cancellationToken);
            if (receipt is null) return NotFound<ReceiptBatchAllocationResponseDto>("Bank receipt was not found.");

            var existingAmount = receipt.ReceiptAllocations.Sum(item => item.AllocatedAmount ?? 0m);
            var requestedAmount = items.Sum(item => item.AllocatedAmount);
            var createdAllocations = new List<ReceiptBatchAllocationItemDto>();
            if (existingAmount + requestedAmount > receipt.AmountReceived)
            {
                return Failure<ReceiptBatchAllocationResponseDto>("Allocation exceeds the encaissement balance.", status: StatusCodes.Status409Conflict);
            }

            foreach (var item in items)
            {
                var contract = await _db.Contracts
                    .Include(value => value.ContractPanels)
                    .FirstOrDefaultAsync(value => value.IdContract == item.ContractId, cancellationToken);
                if (contract is null) return NotFound<ReceiptBatchAllocationResponseDto>("One of the selected contracts was not found.");
                if (!string.Equals(ContractConstants.NormalizeStatus(contract.Status), ContractConstants.BoundStatus, StringComparison.OrdinalIgnoreCase))
                    return Failure<ReceiptBatchAllocationResponseDto>("Only Bound contracts can receive an encaissement allocation.", status: StatusCodes.Status409Conflict);
                if (contract.IdCounterparty != receipt.IdCounterparty)
                    return Failure<ReceiptBatchAllocationResponseDto>("The contract cedent does not match the encaissement counterparty.");
                if (contract.IdCurrency != receipt.IdCurrency)
                    return Failure<ReceiptBatchAllocationResponseDto>("The contract currency does not match the encaissement currency.");

                var panels = contract.ContractPanels
                    .Where(panel => string.Equals(panel.Status?.Trim(), ContractConstants.ConfirmedPanelStatus, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(panel.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase)
                        && panel.SignedSharePct is > 0m)
                    .ToList();
                if (panels.Count == 0)
                    return Failure<ReceiptBatchAllocationResponseDto>("The selected Contract has no confirmed reinsurer panels eligible for allocation.");

                var panelCounterpartyIds = panels.Select(panel => panel.IdCounterparty).Distinct().ToArray();
                var validPanelCounterpartyIds = await _db.Counterparties.AsNoTracking()
                    .Where(counterparty => panelCounterpartyIds.Contains(counterparty.Id) && counterparty.IsActive &&
                        counterparty.TypeAssignments.Any(type => type.Type.Trim().ToLower() == CounterpartyTypes.Reinsurer.ToLower()))
                    .Select(counterparty => counterparty.Id)
                    .ToListAsync(cancellationToken);
                if (validPanelCounterpartyIds.Count != panelCounterpartyIds.Length)
                    return Failure<ReceiptBatchAllocationResponseDto>("One or more confirmed reinsurer panels reference an invalid or inactive reinsurer counterparty.");

                var allocation = new ReceiptAllocation
                {
                    IdBankReceipt = receiptId,
                    IdContract = contract.IdContract,
                    TargetType = "PREMIUM",
                    AllocatedAmount = item.AllocatedAmount,
                    IdCurrency = receipt.IdCurrency,
                    ExchangeRate = 1m,
                    PairedAt = DateTime.UtcNow,
                    IdUser = user
                };
                _db.ReceiptAllocations.Add(allocation);
                await _db.SaveChangesAsync(cancellationToken);
                createdAllocations.Add(new ReceiptBatchAllocationItemDto
                {
                    IdReceiptAllocation = allocation.IdReceiptAllocation,
                    ContractId = allocation.IdContract!.Value,
                    AllocatedAmount = allocation.AllocatedAmount!.Value
                });

                foreach (var panel in panels)
                {
                    var gross = decimal.Round(item.AllocatedAmount * panel.SignedSharePct!.Value / 100m, 4, MidpointRounding.AwayFromZero);
                    var brokerage = decimal.Round(gross * (panel.BrokeragePct ?? 0m) / 100m, 4, MidpointRounding.AwayFromZero);
                    var deductions = brokerage + (panel.OtherDeductions ?? 0m);
                    var net = Math.Max(0m, decimal.Round(gross - deductions, 4, MidpointRounding.AwayFromZero));
                    _db.RemittanceRefs.Add(new RemittanceRef
                    {
                        IdReceiptAllocation = allocation.IdReceiptAllocation,
                        IdPanel = panel.IdPanel,
                        IdCounterparty = panel.IdCounterparty,
                        IdCurrency = receipt.IdCurrency,
                        SourceType = "Premium",
                        PremiumSignedLine = gross,
                        Deductions = deductions,
                        NetPremiumDue = net,
                        NetPayableAmount = net,
                        Status = "Draft",
                        SignatureStatus = "Draft",
                        Description = "Generated from contract encaissement allocation",
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = user
                    });
                }
            }

            var finalAmount = existingAmount + requestedAmount;
            receipt.DetailsStatus = InvoiceRecoveryCalculator.GetReceiptPairingStatus(receipt.AmountReceived, finalAmount);
            receipt.UpdatedAt = DateTime.UtcNow;
            receipt.UpdatedBy = user;
            await _db.Notifications
                .Where(notification => notification.RecipientUserId == user && notification.EntityType == "BANK_RECEIPT" && notification.EntityId == receiptId && !notification.IsRead)
                .ExecuteUpdateAsync(setters => setters.SetProperty(notification => notification.IsRead, true).SetProperty(notification => notification.ReadAt, DateTime.UtcNow), cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<ReceiptBatchAllocationResponseDto>.Success(new ReceiptBatchAllocationResponseDto
            {
                ReceiptId = receiptId,
                Allocations = createdAllocations,
                TotalAllocated = finalAmount,
                RemainingAmount = Math.Max(0m, receipt.AmountReceived - finalAmount),
                PairingStatus = receipt.DetailsStatus ?? string.Empty
            });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ServiceResult<IReadOnlyList<RemittanceResponseDto>>> ListRemittancesAsync(CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<IReadOnlyList<RemittanceResponseDto>>();
        var list = await _db.RemittanceRefs.AsNoTracking().Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation).Select(x => new RemittanceResponseDto { RemittanceRef = x.RemittanceRef1, ReceiptAllocationId = x.IdReceiptAllocation, DisbursementId = x.IdDisbursement, PanelId = x.IdPanel, CounterpartyId = x.IdCounterparty, CounterpartyName = x.IdCounterpartyNavigation.Name, NetPremiumDue = x.NetPremiumDue, NetPayableAmount = x.NetPayableAmount, CurrencyId = x.IdCurrency, CurrencyCode = x.IdCurrencyNavigation.CurrencyCode, Status = x.Status, Description = x.Description }).ToListAsync(cancellationToken);
        return ServiceResult<IReadOnlyList<RemittanceResponseDto>>.Success(list);
    }

    public async Task<ServiceResult<RemittanceResponseDto>> ConfirmRemittanceAsync(int remittanceRef, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<RemittanceResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<RemittanceResponseDto>();
        var item = await _db.RemittanceRefs.Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation).FirstOrDefaultAsync(x => x.RemittanceRef1 == remittanceRef, cancellationToken);
        if (item is null) return NotFound<RemittanceResponseDto>("Remittance was not found.");
        if (string.Equals(item.Status, "ReadyForDisbursement", StringComparison.OrdinalIgnoreCase))
        {
            var confirmation = await ConfirmRemittanceForDisbursementAsync(remittanceRef, cancellationToken);
            if (!confirmation.Succeeded)
            {
                return ServiceResult<RemittanceResponseDto>.Failure(confirmation.Error?.Message ?? "The remittance could not be confirmed.", confirmation.Error?.Errors, confirmation.StatusCode);
            }

            item.Status = "AwaitingFinanceDisbursement";
            item.ConfirmedAt = confirmation.Value!.ConfirmationDate;
            item.ConfirmedBy = user;
            return ServiceResult<RemittanceResponseDto>.Success(ToRemittance(item));
        }
        if (!string.Equals(item.Status, "Draft", StringComparison.OrdinalIgnoreCase)) return Failure<RemittanceResponseDto>("Only draft remittances can be confirmed.", status: StatusCodes.Status409Conflict);
        item.Status = "ReadyForDisbursement"; item.UpdatedAt = DateTime.UtcNow; item.UpdatedBy = user;
        await _db.SaveChangesAsync(cancellationToken); await NotifyRoleAsync("Finance Officer", "Remittance ready for disbursement", "A remittance is ready for disbursement.", "REMITTANCE_REF", item.RemittanceRef1, cancellationToken); await _db.SaveChangesAsync(cancellationToken);
        return ServiceResult<RemittanceResponseDto>.Success(ToRemittance(item));
    }

    public async Task<ServiceResult<RemittanceConfirmationResponseDto>> ConfirmRemittanceForDisbursementAsync(int remittanceRef, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<RemittanceConfirmationResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<RemittanceConfirmationResponseDto>();

        var item = await _db.RemittanceRefs
            .Include(x => x.IdCounterpartyNavigation)
            .Include(x => x.IdCurrencyNavigation)
            .Include(x => x.IdPanelNavigation)
            .FirstOrDefaultAsync(x => x.RemittanceRef1 == remittanceRef, cancellationToken);
        if (item is null) return NotFound<RemittanceConfirmationResponseDto>("Remittance was not found.");

        if (string.Equals(item.Status, "AwaitingFinanceDisbursement", StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _db.Notifications.AsNoTracking()
                .Where(x => x.EntityType == "REMITTANCE_REF" && x.EntityId == remittanceRef && x.NotificationType == "DISBURSEMENT_REQUIRED")
                .Select(x => (int?)x.IdNotification)
                .FirstOrDefaultAsync(cancellationToken);
            return ServiceResult<RemittanceConfirmationResponseDto>.Success(new RemittanceConfirmationResponseDto
            {
                RemittanceId = remittanceRef,
                Status = "AwaitingFinanceDisbursement",
                FinanceNotificationCreated = existing.HasValue,
                NotificationId = existing,
                ConfirmationDate = item.ConfirmedAt ?? item.UpdatedAt ?? item.CreatedAt
            });
        }

        if (!string.Equals(item.Status, "ReadyForDisbursement", StringComparison.OrdinalIgnoreCase))
            return Failure<RemittanceConfirmationResponseDto>("Only remittances with status ReadyForDisbursement can be confirmed for payment.", status: StatusCodes.Status409Conflict);
        if (!item.IdPanel.HasValue || item.IdPanelNavigation is null)
            return Failure<RemittanceConfirmationResponseDto>("The remittance does not reference a valid reinsurer panel.");
        if (item.IdPanelNavigation.IdCounterparty != item.IdCounterparty)
            return Failure<RemittanceConfirmationResponseDto>("The remittance panel does not reference the remittance reinsurer.");
        if (!string.Equals(item.IdPanelNavigation.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(item.IdPanelNavigation.Status?.Trim(), ContractConstants.ConfirmedPanelStatus, StringComparison.OrdinalIgnoreCase))
            return Failure<RemittanceConfirmationResponseDto>("The remittance panel is not a confirmed reinsurer placement.");
        var validReinsurer = await _db.Counterparties.AsNoTracking().AnyAsync(counterparty =>
            counterparty.Id == item.IdCounterparty && counterparty.IsActive &&
            counterparty.TypeAssignments.Any(type => type.Type.Trim().ToLower() == CounterpartyTypes.Reinsurer.ToLower()), cancellationToken);
        if (!validReinsurer)
            return Failure<RemittanceConfirmationResponseDto>("The remittance does not reference a valid active reinsurer counterparty.");
        if (item.IdCurrencyNavigation is null)
            return Failure<RemittanceConfirmationResponseDto>("The remittance currency is required.");
        if (!item.NetPayableAmount.HasValue || item.NetPayableAmount.Value <= 0m)
            return Failure<RemittanceConfirmationResponseDto>("The remittance net payable amount must be greater than zero.");

        var now = DateTime.UtcNow;
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        item.Status = "AwaitingFinanceDisbursement";
        item.ConfirmedAt = now;
        item.ConfirmedBy = user;
        item.UpdatedAt = now;
        item.UpdatedBy = user;

        var contract = await _db.Contracts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdContract == item.IdPanelNavigation.IdContract, cancellationToken);
        var financeUsers = await _db.AspNetUsers
            .Where(x => x.Position != null && x.Position.Trim().ToLower() == "finance officer")
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var existingNotifications = await _db.Notifications
            .Where(x => x.EntityType == "REMITTANCE_REF" && x.EntityId == remittanceRef && x.NotificationType == "DISBURSEMENT_REQUIRED")
            .Select(x => x.RecipientUserId)
            .ToListAsync(cancellationToken);
        var createdNotifications = new List<Notification>();
        foreach (var financeUser in financeUsers.Where(x => !existingNotifications.Contains(x)))
        {
            var notification = new Notification
            {
                RecipientUserId = financeUser,
                RecipientRole = "Finance Officer",
                NotificationType = "DISBURSEMENT_REQUIRED",
                Title = "Remittance requires disbursement",
                Message = $"Remittance {remittanceRef} for {item.IdCounterpartyNavigation.Name} requires payment. Contract {contract?.Name ?? "unknown"}, amount {item.NetPayableAmount:0.####} {item.IdCurrencyNavigation.CurrencyCode}.",
                EntityType = "REMITTANCE_REF",
                EntityId = remittanceRef,
                TargetRoute = "/finance/disbursements/pending",
                IsRead = false,
                CreatedAt = now,
                CreatedBy = user
            };
            _db.Notifications.Add(notification);
            createdNotifications.Add(notification);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<RemittanceConfirmationResponseDto>.Success(new RemittanceConfirmationResponseDto
        {
            RemittanceId = remittanceRef,
            Status = "AwaitingFinanceDisbursement",
            FinanceNotificationCreated = createdNotifications.Count > 0,
            NotificationId = createdNotifications.FirstOrDefault()?.IdNotification,
            ConfirmationDate = now
        });
    }

    public async Task<ServiceResult<IReadOnlyList<NotificationResponseDto>>> GetNotificationsAsync(bool unreadOnly, CancellationToken cancellationToken)
    {
        var user = CurrentUser();
        if (user is null) return Unauthorized<IReadOnlyList<NotificationResponseDto>>();
        var query = _db.Notifications.AsNoTracking().Where(item => item.RecipientUserId == user);
        if (unreadOnly) query = query.Where(item => !item.IsRead);
        var notifications = await query.OrderByDescending(item => item.CreatedAt)
            .Select(item => new NotificationResponseDto
            {
                IdNotification = item.IdNotification,
                NotificationType = item.NotificationType,
                Title = item.Title,
                Message = item.Message,
                EntityType = item.EntityType,
                EntityId = item.EntityId,
                RecipientRole = item.RecipientRole,
                TargetRoute = item.TargetRoute,
                IsRead = item.IsRead,
                CreatedAt = item.CreatedAt
            })
            .ToListAsync(cancellationToken);
        foreach (var notification in notifications.Where(item =>
                     item.NotificationType == "ContractReadyForDebitNote" &&
                     item.EntityType == "CONTRACT" &&
                     item.EntityId.HasValue))
        {
            notification.Actions =
            [
                new NotificationActionDto
                {
                    Label = "Open Contract",
                    Url = $"/back-office/contracts/{notification.EntityId}/overview"
                },
                new NotificationActionDto
                {
                    Label = "Download Debit Note",
                    Url = $"/api/back-office/contracts/{notification.EntityId}/debit-note",
                    IsDownload = true
                }
            ];
        }
        return ServiceResult<IReadOnlyList<NotificationResponseDto>>.Success(notifications);
    }

    public async Task<ServiceResult<int>> GetUnreadNotificationCountAsync(CancellationToken cancellationToken)
    {
        var user = CurrentUser(); if (user is null) return Unauthorized<int>(); return ServiceResult<int>.Success(await _db.Notifications.CountAsync(x => x.RecipientUserId == user && !x.IsRead, cancellationToken));
    }

    public async Task<ServiceResult<bool>> MarkNotificationReadAsync(int id, bool all, CancellationToken cancellationToken)
    {
        var user = CurrentUser(); if (user is null) return Unauthorized<bool>();
        var query = _db.Notifications.Where(x => x.RecipientUserId == user && (!all ? x.IdNotification == id : !x.IsRead));
        await query.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAt, DateTime.UtcNow), cancellationToken); return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<IReadOnlyList<RemittanceResponseDto>>> ListDisbursementQueueAsync(CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<IReadOnlyList<RemittanceResponseDto>>();
        var incompletePaymentIds = await _db.RemittanceRefs.AsNoTracking()
            .Where(x => x.Status == "PaymentInProgress" &&
                (!x.IdDisbursement.HasValue || !_db.Invoices.Any(invoice => invoice.IdDisbursement == x.IdDisbursement.Value)))
            .Select(x => x.RemittanceRef1)
            .ToListAsync(cancellationToken);
        foreach (var remittanceRef in incompletePaymentIds)
        {
            var repair = await StartDisbursementAsync(remittanceRef, cancellationToken);
            if (!repair.Succeeded)
                _logger.LogWarning("Could not repair PaymentInProgress remittance {RemittanceRef}: {Message}", remittanceRef, repair.Error?.Message);
        }
        var list = await _db.RemittanceRefs.AsNoTracking().Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation)
            .Where(x => x.Status == "AwaitingFinanceDisbursement" || x.Status == "PaymentInProgress" || x.Status == "Closed")
            .Select(x => new RemittanceResponseDto
            {
                RemittanceRef = x.RemittanceRef1,
                RemittanceReference = x.RemittanceRef1.ToString(),
                ReceiptAllocationId = x.IdReceiptAllocation,
                DisbursementId = x.IdDisbursement,
                InvoiceId = x.IdDisbursement.HasValue
                    ? _db.Invoices.Where(invoice => invoice.IdDisbursement == x.IdDisbursement.Value).Select(invoice => (int?)invoice.IdInvoice).FirstOrDefault()
                    : null,
                PanelId = x.IdPanel,
                ContractId = x.IdPanelNavigation == null ? null : x.IdPanelNavigation.IdContract,
                ContractName = x.IdPanelNavigation == null ? null : x.IdPanelNavigation.IdContractNavigation.Name,
                Umr = x.IdPanelNavigation == null ? null : x.IdPanelNavigation.IdContractNavigation.Umr,
                CedentName = x.IdPanelNavigation == null ? null : x.IdPanelNavigation.IdContractNavigation.IdCounterpartyNavigation.Name,
                CounterpartyId = x.IdCounterparty,
                CounterpartyName = x.IdCounterpartyNavigation.Name,
                NetPremiumDue = x.NetPremiumDue,
                GrossPremium = x.PremiumSignedLine,
                OtherDeductions = x.Deductions,
                NetPayableAmount = x.NetPayableAmount,
                CurrencyId = x.IdCurrency,
                CurrencyCode = x.IdCurrencyNavigation.CurrencyCode,
                Status = x.Status,
                Description = x.Description,
                ConfirmedAt = x.ConfirmedAt,
                ConfirmedBy = x.ConfirmedBy,
                HasActiveBankAccount = _db.CounterpartyBankAccounts.Any(account => account.IdCounterparty == x.IdCounterparty && account.IsActive),
                Action = x.Status == "AwaitingFinanceDisbursement" ? "MakeDisbursement" : x.Status == "PaymentInProgress" ? "ContinuePayment" : "ViewInvoice"
            }).ToListAsync(cancellationToken);
        foreach (var item in list)
        {
            item.InvoiceDownloadUrl = item.DisbursementId.HasValue && item.InvoiceId.HasValue
                ? $"/api/finance/disbursements/{item.DisbursementId.Value}/invoice"
                : null;
        }
        return ServiceResult<IReadOnlyList<RemittanceResponseDto>>.Success(list);
    }

    public async Task<ServiceResult<DisbursementContextDto>> GetDisbursementContextAsync(int remittanceRef, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<DisbursementContextDto>();
        var remittance = await _db.RemittanceRefs.AsNoTracking()
            .Include(x => x.IdCounterpartyNavigation)
            .Include(x => x.IdCurrencyNavigation)
            .Include(x => x.IdPanelNavigation)
            .FirstOrDefaultAsync(x => x.RemittanceRef1 == remittanceRef, cancellationToken);
        if (remittance is null) return NotFound<DisbursementContextDto>("Remittance was not found.");

        var panel = remittance.IdPanel.HasValue
            ? await _db.ContractPanels.AsNoTracking().Include(x => x.IdCounterpartyNavigation).FirstOrDefaultAsync(x => x.IdPanel == remittance.IdPanel.Value, cancellationToken)
            : null;
        var contract = panel is null ? null : await _db.Contracts.AsNoTracking()
            .Include(x => x.IdCounterpartyNavigation)
            .FirstOrDefaultAsync(x => x.IdContract == panel.IdContract, cancellationToken);
        var invoice = remittance.IdDisbursement.HasValue
            ? await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(x => x.IdDisbursement == remittance.IdDisbursement.Value, cancellationToken)
            : null;
        if (string.Equals(remittance.Status, "PaymentInProgress", StringComparison.OrdinalIgnoreCase) &&
            (!remittance.IdDisbursement.HasValue || invoice is null))
        {
            var repair = await StartDisbursementAsync(remittanceRef, cancellationToken);
            if (!repair.Succeeded)
                return ServiceResult<DisbursementContextDto>.Failure(repair.Error?.Message ?? "The payment workflow could not be repaired.", repair.Error?.Errors, repair.StatusCode, repair.Error?.Code);
            remittance = await _db.RemittanceRefs.AsNoTracking()
                .Include(x => x.IdCounterpartyNavigation)
                .Include(x => x.IdCurrencyNavigation)
                .FirstOrDefaultAsync(x => x.RemittanceRef1 == remittanceRef, cancellationToken);
            if (remittance is null) return NotFound<DisbursementContextDto>("Remittance was not found.");
            invoice = remittance.IdDisbursement.HasValue
                ? await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(x => x.IdDisbursement == remittance.IdDisbursement.Value, cancellationToken)
                : null;
        }
        var eligibleAccounts = await _db.CounterpartyBankAccounts.AsNoTracking()
            .Where(x => x.IdCounterparty == remittance.IdCounterparty && x.IsActive && x.IdCurrency == remittance.IdCurrency)
            .OrderBy(x => x.IdBankAccount)
            .ToListAsync(cancellationToken);
        var accountSelection = BankAccountEligibility.Select(eligibleAccounts);
        var account = accountSelection.Account;
        var validAccount = accountSelection.IsValid;
        var status = remittance.Status?.Trim() ?? string.Empty;
        var canStart = string.Equals(status, "AwaitingFinanceDisbursement", StringComparison.OrdinalIgnoreCase) && validAccount && remittance.NetPayableAmount > 0m;
        var canConfirm = string.Equals(status, "PaymentInProgress", StringComparison.OrdinalIgnoreCase) && invoice is not null;
        var selectedBankAccount = account is null ? null : MapBankAccountSnapshot(account);
        return ServiceResult<DisbursementContextDto>.Success(new DisbursementContextDto
        {
            RemittanceId = remittanceRef,
            ContractId = contract?.IdContract,
            ContractName = contract?.Name,
            Umr = contract?.Umr,
            CedentName = contract?.IdCounterpartyNavigation?.Name,
            PanelId = panel?.IdPanel,
            ReinsurerId = remittance.IdCounterparty,
            ReinsurerName = remittance.IdCounterpartyNavigation?.Name ?? string.Empty,
            GrossPremium = remittance.PremiumSignedLine,
            Deductions = remittance.Deductions,
            NetPayableAmount = remittance.NetPayableAmount ?? 0m,
            CurrencyCode = remittance.IdCurrencyNavigation?.CurrencyCode ?? string.Empty,
            Status = status,
            DisbursementId = remittance.IdDisbursement,
            InvoiceId = invoice?.IdInvoice,
            InvoiceNumber = invoice?.InvoiceNumber,
            CanDownloadInvoice = invoice is not null,
            CanStartPayment = canStart,
            CanConfirmTransfer = canConfirm,
            ActiveBankAccountCount = eligibleAccounts.Count,
            BankAccountSelectionRequired = eligibleAccounts.Count > 1,
            SelectedBankAccount = selectedBankAccount,
            DefaultBankAccount = selectedBankAccount,
            ValidationMessage = accountSelection.ValidationMessage
        });
    }

    public async Task<ServiceResult<StartDisbursementResponseDto>> StartDisbursementAsync(int remittanceRef, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<StartDisbursementResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<StartDisbursementResponseDto>();
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var remittance = await _db.RemittanceRefs.Include(x => x.IdCurrencyNavigation).FirstOrDefaultAsync(x => x.RemittanceRef1 == remittanceRef, cancellationToken);
        if (remittance is null) return NotFound<StartDisbursementResponseDto>("Remittance was not found.");
        var paymentInProgress = string.Equals(remittance.Status, "PaymentInProgress", StringComparison.OrdinalIgnoreCase);
        var closed = string.Equals(remittance.Status, "Closed", StringComparison.OrdinalIgnoreCase);
        Disbursement? existingDisbursement = remittance.IdDisbursement.HasValue
            ? await _db.Disbursements.FirstOrDefaultAsync(x => x.IdDisbursement == remittance.IdDisbursement.Value, cancellationToken)
            : null;
        if (paymentInProgress && existingDisbursement is null)
        {
            existingDisbursement = await _db.Disbursements
                .Where(x => x.CaseReference == $"REM-{remittanceRef}" && x.IdCounterparty == remittance.IdCounterparty)
                .OrderByDescending(x => x.IdDisbursement)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingDisbursement is not null)
            {
                remittance.IdDisbursement = existingDisbursement.IdDisbursement;
                remittance.UpdatedAt = DateTime.UtcNow;
                remittance.UpdatedBy = user;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        if (paymentInProgress || closed)
        {
            var existingInvoice = existingDisbursement is not null
                ? await _db.Invoices.FirstOrDefaultAsync(x => x.IdDisbursement == existingDisbursement.IdDisbursement, cancellationToken)
                : null;
            if (existingDisbursement is not null && existingInvoice is not null)
            {
                var existingDownloadUrl = $"/api/finance/disbursements/{existingDisbursement.IdDisbursement}/invoice";
                return ServiceResult<StartDisbursementResponseDto>.Success(new StartDisbursementResponseDto { RemittanceId = remittanceRef, DisbursementId = existingDisbursement.IdDisbursement, InvoiceId = existingInvoice.IdInvoice, Status = remittance.Status, DownloadUrl = existingDownloadUrl, InvoiceDownloadUrl = existingDownloadUrl, SelectedBankAccount = await GetBankAccountSnapshotAsync(existingDisbursement.IdBankAccount, remittance.IdCounterparty, remittance.IdCurrency, cancellationToken) });
            }

            if (closed)
                return Failure<StartDisbursementResponseDto>("The Closed payment workflow is missing its disbursement or invoice.", status: StatusCodes.Status409Conflict);
        }
        if (!string.Equals(remittance.Status, "AwaitingFinanceDisbursement", StringComparison.OrdinalIgnoreCase) && !paymentInProgress) return Failure<StartDisbursementResponseDto>("Only remittances awaiting Finance disbursement or incomplete payments can start payment.", status: StatusCodes.Status409Conflict);
        if (!remittance.IdPanel.HasValue || !remittance.NetPayableAmount.HasValue || remittance.NetPayableAmount <= 0m) return Failure<StartDisbursementResponseDto>("The remittance panel and positive net payable amount are required.");
        var panel = await _db.ContractPanels.AsNoTracking()
            .Include(x => x.IdCounterpartyNavigation)
            .ThenInclude(x => x.TypeAssignments)
            .Include(x => x.IdContractNavigation).ThenInclude(x => x.IdCounterpartyNavigation)
            .FirstOrDefaultAsync(x => x.IdPanel == remittance.IdPanel.Value, cancellationToken);
        if (panel is null || panel.IdCounterparty != remittance.IdCounterparty || !string.Equals(panel.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase)) return Failure<StartDisbursementResponseDto>("The remittance does not reference a valid reinsurer panel.");
        if (panel.IdCounterpartyNavigation is null || !panel.IdCounterpartyNavigation.IsActive ||
            !panel.IdCounterpartyNavigation.TypeAssignments.Any(type => type.Type.Trim().Equals(CounterpartyTypes.Reinsurer, StringComparison.OrdinalIgnoreCase)))
            return Failure<StartDisbursementResponseDto>("The remittance reinsurer is not active or is not eligible for payment.");
        var eligibleAccounts = await _db.CounterpartyBankAccounts
            .Where(x => x.IdCounterparty == remittance.IdCounterparty && x.IdCurrency == remittance.IdCurrency && x.IsActive)
            .OrderBy(x => x.IdBankAccount)
            .ToListAsync(cancellationToken);
        var accountSelection = BankAccountEligibility.Select(eligibleAccounts);
        if (!accountSelection.IsValid)
            return Failure<StartDisbursementResponseDto>(accountSelection.ValidationMessage!);
        var account = accountSelection.Account!;

        var now = DateTime.UtcNow;
        var disbursement = existingDisbursement ?? new Disbursement
        {
            IdBankAccount = account.IdBankAccount,
            IdCounterparty = remittance.IdCounterparty,
            IdUser = user,
            CaseReference = $"REM-{remittanceRef}",
            TotalAmount = remittance.NetPayableAmount.Value,
            OperationDate = DateOnly.FromDateTime(now),
            PaymentMethode = "BankTransfer",
            SignatureStatus = "PaymentInProgress",
            Description = remittance.Description ?? string.Empty,
            CreatedAt = now,
            CreatedBy = user,
            StartedAt = now,
            StartedBy = user,
            BankNameSnapshot = account.BankName,
            BankCitySnapshot = account.BankCity,
            BankCountrySnapshot = account.BankCountry,
            IbanSnapshot = account.Iban,
            SwiftBicSnapshot = account.SwiftBic,
            CorrespondentBankNameSnapshot = account.CorrespondentBankName,
            CorrespondentSwiftBicSnapshot = account.CorrespondentSwiftBic,
            GrossPremiumSnapshot = remittance.PremiumSignedLine,
            DeductionsSnapshot = remittance.Deductions,
            CurrencyCodeSnapshot = remittance.IdCurrencyNavigation?.CurrencyCode,
            ContractNameSnapshot = panel.IdContractNavigation?.Name,
            UmrSnapshot = panel.IdContractNavigation?.Umr,
            CedentNameSnapshot = panel.IdContractNavigation?.IdCounterpartyNavigation?.Name,
            ReinsurerNameSnapshot = panel.IdCounterpartyNavigation?.Name,
            ReinsurerAddressSnapshot = panel.IdCounterpartyNavigation?.AddressLine,
            ReinsurerCitySnapshot = panel.IdCounterpartyNavigation?.City,
            ReinsurerCountrySnapshot = panel.IdCounterpartyNavigation?.Country,
            ReinsurerTaxIdSnapshot = panel.IdCounterpartyNavigation?.TaxIdentificationNumber,
            PanelReferenceSnapshot = panel.PremiumReference
        };
        if (existingDisbursement is null)
        {
            _db.Disbursements.Add(disbursement);
            await _db.SaveChangesAsync(cancellationToken);
            remittance.IdDisbursement = disbursement.IdDisbursement;
        }
        else if (!string.Equals(disbursement.SignatureStatus, "PaymentInProgress", StringComparison.OrdinalIgnoreCase))
        {
            return Failure<StartDisbursementResponseDto>("The existing disbursement is not in PaymentInProgress state.", status: StatusCodes.Status409Conflict);
        }
        var invoice = new Invoice
        {
            IdContract = panel.IdContract,
            IdCurrency = remittance.IdCurrency,
            IdCounterparty = remittance.IdCounterparty,
            IdPanel = panel.IdPanel,
            RemittanceRef = remittanceRef,
            IdDisbursement = disbursement.IdDisbursement,
            InvoiceNumber = $"PAY-{now:yyyy}-{disbursement.IdDisbursement:000000}",
            InvoiceType = "REINSURER_DISBURSEMENT",
            InvoiceDate = DateOnly.FromDateTime(now),
            GrossPremium = disbursement.TotalAmount,
            NetPremium = disbursement.TotalAmount,
            RecoveryStatus = "Unpaired",
            Label = $"Payment for remittance {remittanceRef}",
            SettlementMethod = "BankTransfer",
            Status = "Issued",
            CreatedAt = now,
            CreatedBy = user,
            IdUser = user
        };
        _db.Invoices.Add(invoice);
        remittance.IdDisbursement = disbursement.IdDisbursement;
        remittance.Status = "PaymentInProgress";
        remittance.UpdatedAt = now;
        remittance.UpdatedBy = user;
        await _db.Notifications.Where(x => x.EntityType == "REMITTANCE_REF" && x.EntityId == remittanceRef && x.NotificationType == "DISBURSEMENT_REQUIRED" && !x.IsRead).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAt, now), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var downloadUrl = $"/api/finance/disbursements/{disbursement.IdDisbursement}/invoice";
        return ServiceResult<StartDisbursementResponseDto>.Success(new StartDisbursementResponseDto { RemittanceId = remittanceRef, DisbursementId = disbursement.IdDisbursement, InvoiceId = invoice.IdInvoice, Status = "PaymentInProgress", DownloadUrl = downloadUrl, InvoiceDownloadUrl = downloadUrl, SelectedBankAccount = MapBankAccountSnapshot(account) });
    }

    public async Task<ServiceResult<DisbursementResponseDto>> CreateDisbursementAsync(DisbursementRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<DisbursementResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<DisbursementResponseDto>();
        var started = await StartDisbursementAsync(request.RemittanceRef, cancellationToken);
        if (!started.Succeeded)
            return ServiceResult<DisbursementResponseDto>.Failure(started.Error?.Message ?? "The payment could not be started.", started.Error?.Errors, started.StatusCode);
        var startedDisbursement = await _db.Disbursements
            .Include(x => x.IdCounterpartyNavigation)
            .Include(x => x.IdBankAccountNavigation).ThenInclude(x => x.IdCurrencyNavigation)
            .FirstOrDefaultAsync(x => x.IdDisbursement == started.Value!.DisbursementId, cancellationToken);
        var startedRemittance = await _db.RemittanceRefs.Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation).FirstOrDefaultAsync(x => x.RemittanceRef1 == request.RemittanceRef, cancellationToken);
        if (startedDisbursement is not null && startedRemittance is not null)
            return ServiceResult<DisbursementResponseDto>.Success(ToDisbursement(startedDisbursement, startedRemittance), StatusCodes.Status201Created);

        // Kept below only for source compatibility with older callers; the return above
        // ensures all current callers use the transactional start-payment workflow.
        var remittance = await _db.RemittanceRefs.Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation).FirstOrDefaultAsync(x => x.RemittanceRef1 == request.RemittanceRef, cancellationToken);
        if (remittance is null) return NotFound<DisbursementResponseDto>("Remittance was not found.");
        if (remittance.IdDisbursement.HasValue)
        {
            var existingDisbursement = await _db.Disbursements
                .Include(x => x.IdCounterpartyNavigation)
                .Include(x => x.IdBankAccountNavigation).ThenInclude(x => x.IdCurrencyNavigation)
                .FirstOrDefaultAsync(x => x.IdDisbursement == remittance.IdDisbursement.Value, cancellationToken);
            return existingDisbursement is null
                ? Failure<DisbursementResponseDto>("The remittance already has a disbursement, but it could not be loaded.", status: StatusCodes.Status409Conflict)
                : ServiceResult<DisbursementResponseDto>.Success(ToDisbursement(existingDisbursement, remittance));
        }
        if (!string.Equals(remittance.Status, "AwaitingFinanceDisbursement", StringComparison.OrdinalIgnoreCase)) return Failure<DisbursementResponseDto>("Only remittances awaiting Finance disbursement can be paid.", status: StatusCodes.Status409Conflict);
        if (!remittance.NetPayableAmount.HasValue || remittance.NetPayableAmount.Value <= 0m) return Failure<DisbursementResponseDto>("The remittance net payable amount must be greater than zero.");
        var account = await _db.CounterpartyBankAccounts.FirstOrDefaultAsync(x => x.IdBankAccount == request.BankAccountId && x.IdCounterparty == remittance.IdCounterparty && x.IsActive, cancellationToken);
        if (account is null) return Failure<DisbursementResponseDto>("An active bank account for the reinsurer is required.");
        if (account.IdCurrency != remittance.IdCurrency) return Failure<DisbursementResponseDto>("Bank account currency does not match the remittance currency.");
        if (string.IsNullOrWhiteSpace(account.Iban) || string.IsNullOrWhiteSpace(account.SwiftBic)) return Failure<DisbursementResponseDto>("The selected bank account must have an IBAN and SWIFT/BIC.");
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var item = new Disbursement { IdBankAccount = account.IdBankAccount, IdCounterparty = remittance.IdCounterparty, IdUser = user, CaseReference = $"REM-{remittance.RemittanceRef1}", BankReference = Optional(request.BankReference), TotalAmount = remittance.NetPayableAmount.Value, OperationDate = request.OperationDate, TransferDate = request.TransferDate ?? request.OperationDate, PaymentMethode = request.PaymentMethod.Trim(), SignatureStatus = "Draft", Description = request.Description?.Trim() ?? remittance.Description ?? string.Empty, CreatedAt = now, CreatedBy = user };
        _db.Disbursements.Add(item); await _db.SaveChangesAsync(cancellationToken); remittance.IdDisbursement = item.IdDisbursement; remittance.Status = "Paid"; remittance.UpdatedAt = now; remittance.UpdatedBy = user;
        var allocation = remittance.IdReceiptAllocation.HasValue ? await _db.ReceiptAllocations.FirstOrDefaultAsync(x => x.IdReceiptAllocation == remittance.IdReceiptAllocation.Value, cancellationToken) : null;
        var panel = remittance.IdPanel.HasValue ? await _db.ContractPanels.FirstOrDefaultAsync(x => x.IdPanel == remittance.IdPanel.Value, cancellationToken) : null;
        if (panel is not null && !await _db.Invoices.AnyAsync(x => x.IdDisbursement == item.IdDisbursement, cancellationToken))
        {
            var invoice = new Invoice
            {
                IdContract = panel.IdContract,
                IdReceiptAllocation = allocation?.IdReceiptAllocation,
                IdCurrency = remittance.IdCurrency,
                IdCounterparty = remittance.IdCounterparty,
                IdPanel = panel.IdPanel,
                RemittanceRef = remittance.RemittanceRef1,
                IdDisbursement = item.IdDisbursement,
                InvoiceNumber = $"PAY-{now:yyyy}-{item.IdDisbursement:000000}",
                InvoiceType = "REINSURER_DISBURSEMENT",
                InvoiceDate = request.TransferDate ?? request.OperationDate,
                GrossPremium = item.TotalAmount,
                NetPremium = item.TotalAmount,
                RecoveryStatus = "Unpaired",
                Label = $"Payment for remittance {remittance.RemittanceRef1}; bank reference {request.BankReference ?? "N/A"}",
                SettlementMethod = request.PaymentMethod.Trim(),
                Status = "Issued",
                CreatedAt = now,
                CreatedBy = user,
                IdUser = user
            };
            _db.Invoices.Add(invoice);
        }
        await _db.Notifications.Where(x => x.EntityType == "REMITTANCE_REF" && x.EntityId == remittance.RemittanceRef1 && x.NotificationType == "DISBURSEMENT_REQUIRED" && !x.IsRead).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAt, now), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return ServiceResult<DisbursementResponseDto>.Success(ToDisbursement(item, remittance), StatusCodes.Status201Created);
    }

    public async Task<ServiceResult<DisbursementResponseDto>> ConfirmDisbursementAsync(int id, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<DisbursementResponseDto>();
        var transfer = await ConfirmTransferAsync(id, cancellationToken);
        if (!transfer.Succeeded)
            return ServiceResult<DisbursementResponseDto>.Failure(transfer.Error?.Message ?? "The transfer could not be confirmed.", transfer.Error?.Errors, transfer.StatusCode);
        var confirmedDisbursement = await _db.Disbursements.Include(x => x.IdCounterpartyNavigation).Include(x => x.IdBankAccountNavigation).ThenInclude(x => x.IdCurrencyNavigation).FirstOrDefaultAsync(x => x.IdDisbursement == id, cancellationToken);
        var confirmedRemittance = await _db.RemittanceRefs.Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation).FirstOrDefaultAsync(x => x.IdDisbursement == id, cancellationToken);
        if (confirmedDisbursement is not null)
            return ServiceResult<DisbursementResponseDto>.Success(ToDisbursement(confirmedDisbursement, confirmedRemittance));

        // Kept below only for source compatibility with older callers.
        var item = await _db.Disbursements.Include(x => x.IdCounterpartyNavigation).Include(x => x.IdBankAccountNavigation).ThenInclude(x => x.IdCurrencyNavigation).Include(x => x.RemittanceRefs).FirstOrDefaultAsync(x => x.IdDisbursement == id, cancellationToken);
        if (item is null) return NotFound<DisbursementResponseDto>("Disbursement was not found.");
        item.SignatureStatus = "Confirmed"; item.FinancialOfficerSignatureDate = DateOnly.FromDateTime(DateTime.UtcNow); item.UpdatedAt = DateTime.UtcNow; item.UpdatedBy = CurrentUser(); await _db.SaveChangesAsync(cancellationToken);
        var remittance = item.RemittanceRefs.FirstOrDefault(); return ServiceResult<DisbursementResponseDto>.Success(ToDisbursement(item, remittance));
    }

    public async Task<ServiceResult<ConfirmTransferResponseDto>> ConfirmTransferAsync(int id, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<ConfirmTransferResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<ConfirmTransferResponseDto>();
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var disbursement = await _db.Disbursements.Include(x => x.RemittanceRefs).FirstOrDefaultAsync(x => x.IdDisbursement == id, cancellationToken);
        if (disbursement is null) return NotFound<ConfirmTransferResponseDto>("Disbursement was not found.");
        var remittance = disbursement.RemittanceRefs.FirstOrDefault();
        if (remittance is null && TryGetRemittanceReference(disbursement.CaseReference, out var orphanRemittanceRef))
        {
            remittance = await _db.RemittanceRefs.FirstOrDefaultAsync(x => x.RemittanceRef1 == orphanRemittanceRef, cancellationToken);
            if (remittance is not null && !remittance.IdDisbursement.HasValue)
            {
                remittance.IdDisbursement = id;
                remittance.UpdatedAt = DateTime.UtcNow;
                remittance.UpdatedBy = user;
            }
        }
        if (remittance is null) return NotFound<ConfirmTransferResponseDto>("The disbursement is not linked to a remittance.");
        var closedAt = disbursement.ClosedAt ?? DateTime.UtcNow;
        if (string.Equals(disbursement.SignatureStatus, "Closed", StringComparison.OrdinalIgnoreCase) && string.Equals(remittance.Status, "Closed", StringComparison.OrdinalIgnoreCase))
        {
            var invoiceId = await _db.Invoices.AsNoTracking().Where(x => x.IdDisbursement == id).Select(x => x.IdInvoice).FirstOrDefaultAsync(cancellationToken);
            return ServiceResult<ConfirmTransferResponseDto>.Success(new ConfirmTransferResponseDto { DisbursementId = id, RemittanceId = remittance.RemittanceRef1, InvoiceId = invoiceId, Status = "Closed", ClosedAt = closedAt, ConfirmedAtUtc = closedAt });
        }
        if (!string.Equals(disbursement.SignatureStatus, "PaymentInProgress", StringComparison.OrdinalIgnoreCase) || !string.Equals(remittance.Status, "PaymentInProgress", StringComparison.OrdinalIgnoreCase))
            return Failure<ConfirmTransferResponseDto>("Only payments in progress can be confirmed as transferred.", status: StatusCodes.Status409Conflict);
        var invoice = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(x => x.IdDisbursement == id, cancellationToken);
        if (invoice is null)
            return Failure<ConfirmTransferResponseDto>("A payment invoice must exist before the transfer can be confirmed.");

        disbursement.SignatureStatus = "Closed";
        disbursement.ClosedAt = closedAt;
        disbursement.ClosedBy = user;
        disbursement.FinancialOfficerSignatureDate = DateOnly.FromDateTime(closedAt);
        disbursement.UpdatedAt = closedAt;
        disbursement.UpdatedBy = user;
        remittance.Status = "Closed";
        remittance.UpdatedAt = closedAt;
        remittance.UpdatedBy = user;
        await _db.Notifications.Where(x => x.EntityType == "REMITTANCE_REF" && x.EntityId == remittance.RemittanceRef1 && x.NotificationType == "DISBURSEMENT_REQUIRED" && !x.IsRead).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsRead, true).SetProperty(x => x.ReadAt, closedAt), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<ConfirmTransferResponseDto>.Success(new ConfirmTransferResponseDto { DisbursementId = id, RemittanceId = remittance.RemittanceRef1, InvoiceId = invoice.IdInvoice, Status = "Closed", ClosedAt = closedAt, ConfirmedAtUtc = closedAt });
    }

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetDisbursementInvoiceAsync(int disbursementId, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<PrintablePaymentDocumentDto>();
        var disbursement = await _db.Disbursements.AsNoTracking()
            .Include(x => x.IdCounterpartyNavigation)
            .Include(x => x.IdBankAccountNavigation).ThenInclude(x => x.IdCurrencyNavigation)
            .Include(x => x.RemittanceRefs)
            .FirstOrDefaultAsync(x => x.IdDisbursement == disbursementId, cancellationToken);
        if (disbursement is null) return NotFound<PrintablePaymentDocumentDto>("No disbursement was found with the supplied identifier.", "DISBURSEMENT_NOT_FOUND");

        var remittance = disbursement.RemittanceRefs.FirstOrDefault();
        if (remittance is null && TryGetRemittanceReference(disbursement.CaseReference, out var orphanRemittanceRef))
            remittance = await _db.RemittanceRefs.AsNoTracking().FirstOrDefaultAsync(x => x.RemittanceRef1 == orphanRemittanceRef, cancellationToken);
        if (remittance is null) return NotFound<PrintablePaymentDocumentDto>("The disbursement is not linked to a remittance.");
        var panel = remittance.IdPanel.HasValue
            ? await _db.ContractPanels.AsNoTracking().FirstOrDefaultAsync(x => x.IdPanel == remittance.IdPanel.Value, cancellationToken)
            : null;
        var contract = panel is null
            ? null
            : await _db.Contracts.AsNoTracking().Include(x => x.IdCounterpartyNavigation).FirstOrDefaultAsync(x => x.IdContract == panel.IdContract, cancellationToken);
        var invoice = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(x => x.IdDisbursement == disbursementId, cancellationToken);
        if (invoice is null) return NotFound<PrintablePaymentDocumentDto>("The invoice for this disbursement has not been generated.", "DISBURSEMENT_INVOICE_NOT_FOUND");

        var lines = new[]
        {
            "IRIS - REINSURER DISBURSEMENT PAYMENT DOCUMENT",
            $"Document number: {invoice.InvoiceNumber}",
            $"Issue date: {invoice.InvoiceDate:yyyy-MM-dd}",
            $"Status: {disbursement.SignatureStatus}",
            $"Remittance reference: {remittance.RemittanceRef1}",
            $"Disbursement reference: {disbursement.CaseReference}",
            $"Bank transfer reference: {disbursement.BankReference ?? "N/A"}",
            $"Contract: {disbursement.ContractNameSnapshot ?? contract?.Name}",
            $"UMR: {disbursement.UmrSnapshot ?? contract?.Umr}",
            $"Cedent: {disbursement.CedentNameSnapshot ?? contract?.IdCounterpartyNavigation?.Name}",
            $"Panel: {disbursement.PanelReferenceSnapshot ?? panel?.IdPanel.ToString()}",
            $"Reinsurer: {disbursement.ReinsurerNameSnapshot ?? disbursement.IdCounterpartyNavigation?.Name}",
            $"Reinsurer address: {disbursement.ReinsurerAddressSnapshot}",
            $"Reinsurer city/country: {disbursement.ReinsurerCitySnapshot}, {disbursement.ReinsurerCountrySnapshot}",
            $"Reinsurer tax ID: {disbursement.ReinsurerTaxIdSnapshot}",
            $"Bank: {disbursement.BankNameSnapshot}",
            $"Bank city/country: {disbursement.BankCitySnapshot}, {disbursement.BankCountrySnapshot}",
            $"IBAN: {disbursement.IbanSnapshot}",
            $"SWIFT/BIC: {disbursement.SwiftBicSnapshot}",
            $"Correspondent bank: {disbursement.CorrespondentBankNameSnapshot} {disbursement.CorrespondentSwiftBicSnapshot}",
            $"Gross/allocated premium: {disbursement.GrossPremiumSnapshot:0.####}",
            $"Deductions: {disbursement.DeductionsSnapshot:0.####}",
            $"Net payable: {disbursement.TotalAmount:0.####} {disbursement.CurrencyCodeSnapshot}",
            $"Payment method: {disbursement.PaymentMethode}",
            $"Description: {disbursement.Description}",
            $"Confirmed by Back Officer: {remittance.ConfirmedBy} at {remittance.ConfirmedAt:O}",
            $"Started by Finance Officer: {disbursement.StartedBy} at {disbursement.StartedAt:O}",
            "Signature/approval: ________________________________"
        };
        return ServiceResult<PrintablePaymentDocumentDto>.Success(new PrintablePaymentDocumentDto
        {
            FileName = $"IRIS-Disbursement-{remittance.RemittanceRef1}.pdf",
            Content = BuildPdf(lines),
            ContentType = "application/pdf"
        });
    }

    public async Task<ServiceResult<PagedResponseDto<InvoiceResponseDto>>> ListInvoicesAsync(InvoiceQueryDto query, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<PagedResponseDto<InvoiceResponseDto>>();
        if (query.ContractId.HasValue && !await _db.Contracts.AnyAsync(contract => contract.IdContract == query.ContractId.Value, cancellationToken))
            return NotFound<PagedResponseDto<InvoiceResponseDto>>("Contract was not found.");

        static string? FilterValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            var normalized = trimmed.ToLowerInvariant();
            return normalized is "null" or "undefined" || normalized.StartsWith("all ", StringComparison.Ordinal)
                ? null
                : trimmed;
        }

        var q = _db.Invoices.AsNoTracking();
        if (query.ContractId.HasValue) q = q.Where(x => x.IdContract == query.ContractId.Value);
        if (query.CounterpartyId.HasValue) q = q.Where(x => x.IdCounterparty == query.CounterpartyId.Value);
        if (query.CedentId.HasValue) q = q.Where(x => x.IdContractNavigation.IdCounterparty == query.CedentId.Value);
        if (query.PanelId.HasValue) q = q.Where(x => x.IdPanel == query.PanelId.Value || x.PanelAllocations.Any(line => line.IdPanel == query.PanelId.Value));
        var invoiceType = FilterValue(query.InvoiceType);
        if (invoiceType is not null) q = q.Where(x => x.InvoiceType == invoiceType);
        var requestedPairingStatus = FilterValue(query.PairingStatus)?.ToLowerInvariant();
        if (requestedPairingStatus is "paired" or "unpaired")
        {
            q = requestedPairingStatus == "paired"
                ? q.Where(x => (x.GrossPremium ?? 0m) > 0m && _db.ReceiptAllocations.Where(a => a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m))
                : q.Where(x => (x.GrossPremium ?? 0m) <= 0m || _db.ReceiptAllocations.Where(a => a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) < (x.GrossPremium ?? 0m));
        }
        if (int.TryParse(FilterValue(query.CurrencyId), out var currencyId) && currencyId > 0)
            q = q.Where(x => x.IdCurrency == currencyId);
        var dateFrom = query.DateFrom ?? query.From;
        var dateTo = query.DateTo ?? query.To;
        if (dateFrom.HasValue) q = q.Where(x => x.InvoiceDate >= dateFrom.Value);
        if (dateTo.HasValue) q = q.Where(x => x.InvoiceDate <= dateTo.Value);
        var lifecycleStatus = FilterValue(query.Status);
        if (lifecycleStatus is not null) q = q.Where(x => x.Status == lifecycleStatus);

        var search = FilterValue(query.Search);
        if (search is not null)
        {
            var term = search.ToLowerInvariant();
            q = q.Where(x =>
                x.InvoiceNumber.ToLower().Contains(term) ||
                (x.Label != null && x.Label.ToLower().Contains(term)) ||
                x.IdContractNavigation.Name.ToLower().Contains(term) ||
                (x.IdContractNavigation.Umr != null && x.IdContractNavigation.Umr.ToLower().Contains(term)) ||
                x.IdContractNavigation.IdCounterpartyNavigation.Name.ToLower().Contains(term) ||
                (x.IdCounterpartyNavigation != null && x.IdCounterpartyNavigation.Name.ToLower().Contains(term)) ||
                x.PanelAllocations.Any(line => line.IdCounterpartyNavigation.Name.ToLower().Contains(term)));
        }

        var total = await q.CountAsync(cancellationToken);
        var sortBy = query.SortBy?.Trim().ToLowerInvariant();
        var descending = string.IsNullOrWhiteSpace(query.SortDirection)
            ? string.IsNullOrWhiteSpace(sortBy)
            : string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<Invoice> ordered = sortBy switch
        {
            "invoicenumber" => descending ? q.OrderByDescending(x => x.InvoiceNumber) : q.OrderBy(x => x.InvoiceNumber),
            "contractname" => descending ? q.OrderByDescending(x => x.IdContractNavigation.Name) : q.OrderBy(x => x.IdContractNavigation.Name),
            "cedentname" => descending ? q.OrderByDescending(x => x.IdContractNavigation.IdCounterpartyNavigation.Name) : q.OrderBy(x => x.IdContractNavigation.IdCounterpartyNavigation.Name),
            "grossamount" or "amount" => descending ? q.OrderByDescending(x => x.GrossPremium) : q.OrderBy(x => x.GrossPremium),
            "netamount" => descending ? q.OrderByDescending(x => x.NetPremium) : q.OrderBy(x => x.NetPremium),
            "pairingstatus" => descending
                ? q.OrderByDescending(x => (x.GrossPremium ?? 0m) > 0m && _db.ReceiptAllocations.Where(a => a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m))
                : q.OrderBy(x => (x.GrossPremium ?? 0m) > 0m && _db.ReceiptAllocations.Where(a => a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m)),
            "invoicetype" => descending ? q.OrderByDescending(x => x.InvoiceType) : q.OrderBy(x => x.InvoiceType),
            _ => descending ? q.OrderByDescending(x => x.InvoiceDate) : q.OrderBy(x => x.InvoiceDate)
        };
        var page = Math.Max(query.PageNumber, 1);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var items = await ordered.ThenByDescending(x => x.IdInvoice)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new InvoiceResponseDto
            {
                IdInvoice = x.IdInvoice,
                InvoiceId = x.IdInvoice,
                ContractId = x.IdContract,
                ContractName = x.IdContractNavigation.Name,
                Umr = x.IdContractNavigation.Umr,
                CedentId = x.IdContractNavigation.IdCounterparty,
                CedentName = x.IdContractNavigation.IdCounterpartyNavigation.Name,
                CounterpartyId = x.IdCounterparty ?? x.IdContractNavigation.IdCounterparty,
                CounterpartyName = x.IdCounterpartyNavigation != null
                    ? x.IdCounterpartyNavigation.Name
                    : x.IdContractNavigation.IdCounterpartyNavigation.Name,
                CurrencyId = x.IdCurrency,
                CurrencyCode = x.IdCurrencyNavigation == null ? null : x.IdCurrencyNavigation.CurrencyCode,
                InvoiceNumber = x.InvoiceNumber,
                InvoiceType = x.InvoiceType,
                InvoiceDate = x.InvoiceDate,
                GrossAmount = x.GrossPremium ?? 0m,
                NetPremium = x.NetPremium,
                NetAmount = x.NetPremium,
                RecoveryStatus = (x.GrossPremium ?? 0m) > 0m && _db.ReceiptAllocations.Where(a => a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m)
                    ? "Paired"
                    : "Unpaired",
                PairingStatus = (x.GrossPremium ?? 0m) > 0m && _db.ReceiptAllocations.Where(a => a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m)
                    ? "Paired"
                    : "Unpaired",
                PairedAmount = _db.ReceiptAllocations.Where(a => a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m),
                OutstandingAmount = (x.GrossPremium ?? 0m) > _db.ReceiptAllocations.Where(a => a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m)
                    ? (x.GrossPremium ?? 0m) - _db.ReceiptAllocations.Where(a => a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) : 0m,
                Label = x.Label ?? string.Empty,
                Status = x.Status,
                ReinsurerName = x.IdCounterpartyNavigation != null ? x.IdCounterpartyNavigation.Name : x.PanelAllocations.Select(line => line.IdCounterpartyNavigation.Name).FirstOrDefault()
            }).ToListAsync(cancellationToken);

        return ServiceResult<PagedResponseDto<InvoiceResponseDto>>.Success(new PagedResponseDto<InvoiceResponseDto>
        {
            Items = items,
            PageNumber = page,
            PageSize = size,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)size)
        });
    }

    public async Task<ServiceResult<InvoiceContextDto>> GetInvoiceContextAsync(int contractId, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<InvoiceContextDto>();
        var contract = await _db.Contracts.AsNoTracking()
            .Include(item => item.IdCurrencyNavigation)
            .Include(item => item.ContractFac)
            .Include(item => item.ContractPanels)
            .FirstOrDefaultAsync(item => item.IdContract == contractId, cancellationToken);
        if (contract is null) return NotFound<InvoiceContextDto>("Contract was not found.");
        var status = ContractConstants.NormalizeStatus(contract.Status);

        var allocationRows = await _db.ReceiptAllocations.AsNoTracking()
            .Where(item => item.IdContract == contractId && (item.AllocatedAmount ?? 0m) > 0m)
            .Select(item => new InvoiceAllocationOptionDto
            {
                ReceiptAllocationId = item.IdReceiptAllocation,
                BankReceiptId = item.IdBankReceipt,
                ReceiptReference = item.IdBankReceiptNavigation.BankReference,
                AllocatedAmount = item.AllocatedAmount ?? 0m,
                AlreadyInvoicedAmount = _db.Invoices
                    .Where(invoice => invoice.IdReceiptAllocation == item.IdReceiptAllocation && invoice.Status != "Cancelled")
                    .Sum(invoice => invoice.NetPremium ?? 0m),
                CurrencyCode = item.IdCurrencyNavigation.CurrencyCode
            })
            .ToListAsync(cancellationToken);
        foreach (var allocation in allocationRows)
        {
            allocation.AvailableAmount = Math.Max(0m, allocation.AllocatedAmount - allocation.AlreadyInvoicedAmount);
        }

        var pairedAmount = allocationRows.Sum(item => item.AllocatedAmount);
        var alreadyInvoiced = allocationRows.Sum(item => item.AlreadyInvoicedAmount);

        var previousInvoices = await _db.Invoices.AsNoTracking()
            .Where(invoice => invoice.IdContract == contractId && invoice.InvoiceType == CedentPremiumInvoiceType && invoice.Status != "Cancelled")
            .OrderBy(invoice => invoice.InvoiceDate)
            .Select(ToInvoiceExpression)
            .ToListAsync(cancellationToken);

        var contractGrossPremium = PremiumDebitNoteFinancials.ResolveContractGrossPremium(contract);
        var alreadyInvoicedAmount = await _db.Invoices.AsNoTracking()
            .Where(invoice => invoice.IdContract == contractId && invoice.InvoiceType == CedentPremiumInvoiceType && invoice.Status != "Cancelled")
            .SumAsync(invoice => invoice.GrossPremium ?? 0m, cancellationToken);
        var remainingInvoiceableAmount = Math.Max(0m, contractGrossPremium - alreadyInvoicedAmount);

        var panels = await _db.ContractPanels.AsNoTracking()
            .Where(panel => panel.IdContract == contractId
                && panel.Role != null
                && panel.Role.Trim().ToLower() == "reinsurer"
                && panel.Status != null
                && panel.Status.Trim().ToLower() == ContractConstants.ConfirmedPanelStatus.ToLower()
                && panel.SignedSharePct > 0m)
            .Select(panel => new InvoicePanelOptionDto
            {
                PanelId = panel.IdPanel,
                CounterpartyId = panel.IdCounterparty,
                ReinsurerName = _db.Counterparties.Where(counterparty => counterparty.Id == panel.IdCounterparty).Select(counterparty => counterparty.Name).FirstOrDefault() ?? string.Empty,
                SignedSharePct = panel.SignedSharePct,
                Status = panel.Status ?? string.Empty
            })
            .ToListAsync(cancellationToken);
        var blockingReason = status != ContractConstants.BoundStatus
            ? "CONTRACT_NOT_BOUND"
            : contractGrossPremium <= 0m
                ? "GROSS_PREMIUM_MISSING"
                : remainingInvoiceableAmount <= 0m
                    ? "FULLY_INVOICED"
                    : panels.Count == 0
                        ? "NO_CONFIRMED_REINSURERS"
                        : null;

        return ServiceResult<InvoiceContextDto>.Success(new InvoiceContextDto
        {
            ContractId = contractId,
            ContractGrossPremium = contractGrossPremium,
            CedentCommissionPct = contract.CedentCommission,
            Status = status,
            Currency = contract.IdCurrencyNavigation.CurrencyCode,
            PairedAmount = pairedAmount,
            AlreadyInvoicedAmount = alreadyInvoicedAmount,
            AvailableAmount = remainingInvoiceableAmount,
            InvoiceableGrossPremium = contractGrossPremium,
            RemainingInvoiceableAmount = remainingInvoiceableAmount,
            CanCreateInvoice = blockingReason is null,
            BlockingReason = blockingReason,
            PreviousInvoices = previousInvoices,
            ReceiptAllocations = allocationRows,
            ReinsurerPanels = panels
        });
    }

    public async Task<ServiceResult<PremiumDebitNotePreviewDto>> PreviewPremiumInvoiceAsync(int contractId, PremiumDebitNoteRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<PremiumDebitNotePreviewDto>();
        if (request.GrossAmount <= 0m) return Failure<PremiumDebitNotePreviewDto>("Gross invoice amount must be greater than zero.");
        var data = await LoadPremiumInvoiceDataAsync(contractId, cancellationToken);
        if (data.Error is not null) return ServiceResult<PremiumDebitNotePreviewDto>.Failure(data.Error.Message, data.Error.Errors, data.ErrorStatusCode);
        var remaining = data.InvoiceableGross - data.AlreadyInvoiced;
        if (remaining <= 0m) return Failure<PremiumDebitNotePreviewDto>("No invoiceable premium remains for this contract.", status: StatusCodes.Status409Conflict);
        if (request.GrossAmount > remaining) return Failure<PremiumDebitNotePreviewDto>("Gross invoice amount exceeds the remaining invoiceable premium.", status: StatusCodes.Status409Conflict);
        var calculation = PremiumDebitNoteAllocationCalculator.Calculate(data.Contract!, data.Panels!, request.GrossAmount);
        return ServiceResult<PremiumDebitNotePreviewDto>.Success(new PremiumDebitNotePreviewDto
        {
            ContractId = contractId,
            ContractGrossPremium = data.InvoiceableGross,
            InvoiceableGrossPremium = data.InvoiceableGross,
            AlreadyInvoicedAmount = data.AlreadyInvoiced,
            GrossAmount = request.GrossAmount,
            RemainingAmount = data.InvoiceableGross - data.AlreadyInvoiced - request.GrossAmount,
            Allocations = calculation.Allocations
        });
    }

    public async Task<ServiceResult<PremiumDebitNoteResponseDto>> CreatePremiumInvoiceAsync(int contractId, PremiumDebitNoteRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<PremiumDebitNoteResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<PremiumDebitNoteResponseDto>();
        if (request.GrossAmount <= 0m) return Failure<PremiumDebitNoteResponseDto>("Gross invoice amount must be greater than zero.");

        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(invoice => invoice.IdempotencyKey == request.IdempotencyKey && invoice.InvoiceType == CedentPremiumInvoiceType && invoice.IdContract == contractId, cancellationToken);
            if (existing is not null) return await MapContractInvoiceAsync(existing.IdInvoice, cancellationToken);
        }

        var data = await LoadPremiumInvoiceDataAsync(contractId, cancellationToken);
        if (data.Error is not null) return ServiceResult<PremiumDebitNoteResponseDto>.Failure(data.Error.Message, data.Error.Errors, data.ErrorStatusCode);
        var remaining = data.InvoiceableGross - data.AlreadyInvoiced;
        if (remaining <= 0m) return Failure<PremiumDebitNoteResponseDto>("No invoiceable premium remains for this contract.", status: StatusCodes.Status409Conflict);
        if (request.GrossAmount > remaining) return Failure<PremiumDebitNoteResponseDto>("Gross invoice amount exceeds the remaining invoiceable premium.", status: StatusCodes.Status409Conflict);

        var calculation = PremiumDebitNoteAllocationCalculator.Calculate(data.Contract!, data.Panels!, request.GrossAmount);
        var now = DateTime.UtcNow;
        var invoiceDate = request.InvoiceDate == default ? DateOnly.FromDateTime(now) : request.InvoiceDate;
        var sequence = await AllocateInvoiceNumberSequenceAsync(invoiceDate.Year, cancellationToken);
        if (sequence > 999999)
            return Failure<PremiumDebitNoteResponseDto>($"The invoice-number sequence for {invoiceDate.Year} has reached its six-digit limit.", status: StatusCodes.Status409Conflict);
        var entity = new Invoice
        {
            IdContract = contractId,
            IdCurrency = data.Contract!.IdCurrency,
            InvoiceNumber = CedentInvoiceNumber.Format(invoiceDate.Year, sequence),
            InvoiceType = CedentPremiumInvoiceType,
            InvoiceDate = invoiceDate,
            GrossPremium = request.GrossAmount,
            NetPremium = calculation.TotalNetAmount,
            RecoveryStatus = "Unpaired",
            SequenceNumber = sequence,
            Label = request.Label?.Trim() ?? "Original Premium",
            Status = "Draft",
            CreatedAt = now,
            CreatedBy = user,
            IdUser = user,
            IdempotencyKey = Optional(request.IdempotencyKey)
        };
        try
        {
            _db.Invoices.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var allocation in calculation.Allocations)
            {
                var panel = data.Panels!.Single(item => item.IdPanel == allocation.PanelId);
                _db.InvoicePanelAllocations.Add(new InvoicePanelAllocation
                {
                    IdInvoice = entity.IdInvoice,
                    IdPanel = panel.IdPanel,
                    IdCounterparty = panel.IdCounterparty,
                    ReinsurerNameSnapshot = panel.IdCounterpartyNavigation?.Name ?? string.Empty,
                    SignedSharePctSnapshot = panel.SignedSharePct,
                    AllocationRatio = allocation.AllocationRatio,
                    GrossPremium = allocation.GrossPremium,
                    CedentCommissionPct = allocation.CedentCommissionPct,
                    CedentCommissionAmount = allocation.CedentCommissionAmount,
                    BrokeragePct = allocation.BrokeragePct,
                    BrokerageAmount = allocation.BrokerageAmount,
                    OtherDeductions = allocation.OtherDeductions,
                    NetAmount = allocation.NetAmount,
                    CurrencyCodeSnapshot = data.Contract.IdCurrencyNavigation.CurrencyCode,
                    CreatedAt = now
                });
            }
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (FindSqlException(exception) is { Number: 2601 or 2627 or 547 or 515 } sqlException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            _logger.LogWarning(exception,
                "Premium invoice persistence conflict for contract {ContractId}; SQL error {SqlErrorNumber}, constraint/index {ConstraintName}.",
                contractId, sqlException.Number, ExtractConstraintName(sqlException.Message));
            var message = sqlException.Number switch
            {
                2601 or 2627 => "An invoice with this number or idempotency key already exists. Retry the request.",
                547 => "An allocation references a panel or counterparty that is no longer valid for this contract.",
                _ => "A required invoice value is missing. Verify the invoice data and retry."
            };
            return Failure<PremiumDebitNoteResponseDto>(message, status: StatusCodes.Status409Conflict);
        }
        var createdInvoice = await MapContractInvoiceAsync(entity.IdInvoice, cancellationToken);
        return createdInvoice.Succeeded
            ? ServiceResult<PremiumDebitNoteResponseDto>.Success(createdInvoice.Value!, StatusCodes.Status201Created)
            : createdInvoice;
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is SqlException sqlException) return sqlException;
        return null;
    }

    private static string? ExtractConstraintName(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message,
            "(?:constraint|index) ['\\\"](?<name>[^'\\\"]+)['\\\"]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["name"].Value : null;
    }

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetContractDebitNoteAsync(int contractId, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<PrintablePaymentDocumentDto>();

        var contract = await _db.Contracts.AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.IdCounterpartyNavigation)
            .Include(item => item.IdCurrencyNavigation)
            .Include(item => item.ContractCoverageSelections)
                .ThenInclude(selection => selection.IdCoverageItemNavigation)
            .Include(item => item.ContractFac)
            .Include(item => item.ContractTreatyProp)
                .ThenInclude(treaty => treaty.ContractTreatyPropQuotaShare)
            .Include(item => item.ContractTreatyProp)
                .ThenInclude(treaty => treaty.ContractTreatyPropSurplu)
            .Include(item => item.ContractTreatyNonprops)
            .FirstOrDefaultAsync(item => item.IdContract == contractId, cancellationToken);
        if (contract is null) return NotFound<PrintablePaymentDocumentDto>("Contract was not found.");
        if (!string.Equals(ContractConstants.NormalizeStatus(contract.Status), ContractConstants.BoundStatus, StringComparison.OrdinalIgnoreCase))
            return Failure<PrintablePaymentDocumentDto>("A debit note is available only for a Bound contract.", status: StatusCodes.Status409Conflict);

        var umr = string.IsNullOrWhiteSpace(contract.Umr) ? $"CONTRACT-{contract.IdContract}" : contract.Umr.Trim();
        var mainCover = contract.ContractCoverageSelections
            .Where(selection => CoverageConstants.IsMainCoverLevel(selection.IdCoverageItemNavigation?.CoverageLevel))
            .OrderByDescending(selection => selection.IsPrimary)
            .Select(selection => selection.IdCoverageItemNavigation)
            .FirstOrDefault();
        var contractType = ResolveDebitNoteContractType(contract);
        var grossPremium = contract.GrossPremium ?? 0m;
        var currencyCode = contract.IdCurrencyNavigation?.CurrencyCode ?? string.Empty;
        var lines = new List<string>
        {
            "DEBIT NOTE",
            $"Debit-note reference: DN-{umr}",
            $"Generation date: {DateTime.UtcNow:yyyy-MM-dd}",
            string.Empty,
            "INVOICED TO",
            $"Cedent: {contract.IdCounterpartyNavigation?.Name}",
            $"Contact: {contract.IdCounterpartyNavigation?.ContactName}",
            $"Address: {contract.IdCounterpartyNavigation?.AddressLine}",
            $"City: {contract.IdCounterpartyNavigation?.City}",
            $"Country: {contract.IdCounterpartyNavigation?.Country}",
            string.Empty,
            "CONTRACT INFORMATION",
            $"Contract name: {contract.Name}",
            $"UMR: {umr}",
            $"Insured/contract label: {contract.Name}",
            $"Contract type: {contractType}",
            $"Main cover: {mainCover?.CoverageName}",
            $"Inception date: {contract.InceptionDate:yyyy-MM-dd}",
            $"Expiry date: {contract.ExpiryDate:yyyy-MM-dd}",
            "Transaction: Original Premium",
            $"Currency: {currencyCode}",
            string.Empty,
            $"Contract gross premium: {grossPremium:0.000} {currencyCode}",
            $"Debit-note gross premium: {grossPremium:0.000} {currencyCode}",
            $"AMOUNT TO REMIT: {grossPremium:0.000} {currencyCode}",
            string.Empty,
            "KINDLY REMIT YOUR PAYMENT TO OUR BANK ACCOUNT",
            "Bank Name:",
            "RIB:",
            "IBAN:",
            "SWIFT/BIC:"
        };

        return ServiceResult<PrintablePaymentDocumentDto>.Success(new PrintablePaymentDocumentDto
        {
            FileName = $"DebitNote-{umr}.pdf",
            Content = BuildPdf(lines),
            ContentType = "application/pdf"
        });
    }

    private static string? ResolveDebitNoteContractType(Contract contract)
    {
        if (contract.ContractFac is not null) return ContractTypeNames.Facultative;
        if (contract.ContractTreatyProp?.ContractTreatyPropQuotaShare is not null) return ContractTypeNames.TreatyProportionalQuotaShare;
        if (contract.ContractTreatyProp?.ContractTreatyPropSurplu is not null) return ContractTypeNames.TreatyProportionalSurplus;
        return contract.ContractTreatyNonprops.Count > 0 ? ContractTypeNames.TreatyNonProportional : null;
    }

    public async Task<ServiceResult<IReadOnlyList<PremiumDebitNoteResponseDto>>> ListContractInvoicesAsync(int contractId, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<IReadOnlyList<PremiumDebitNoteResponseDto>>();
        if (!await _db.Contracts.AnyAsync(contract => contract.IdContract == contractId, cancellationToken)) return NotFound<IReadOnlyList<PremiumDebitNoteResponseDto>>("Contract was not found.");
        var invoices = await _db.Invoices.AsNoTracking()
            .Where(invoice => invoice.IdContract == contractId &&
                (invoice.InvoiceType == CedentPremiumInvoiceType || invoice.InvoiceType == "REINSURER_PAYABLE"))
            .Include(invoice => invoice.IdCurrencyNavigation)
            .Include(invoice => invoice.PanelAllocations).ThenInclude(allocation => allocation.IdCounterpartyNavigation)
            .OrderBy(invoice => invoice.InvoiceDate)
            .ThenBy(invoice => invoice.SequenceNumber)
            .ThenBy(invoice => invoice.CreatedAt)
            .ThenBy(invoice => invoice.IdInvoice)
            .ToListAsync(cancellationToken);
        var contract = await _db.Contracts.AsNoTracking().Include(item => item.ContractFac)
            .SingleOrDefaultAsync(item => item.IdContract == contractId, cancellationToken);
        if (contract is null) return NotFound<IReadOnlyList<PremiumDebitNoteResponseDto>>("Contract was not found.");
        var invoiceable = PremiumDebitNoteFinancials.ResolveContractGrossPremium(contract);
        var runningInvoiced = 0m;
        var result = new List<PremiumDebitNoteResponseDto>();
        foreach (var invoice in invoices)
        {
            var previouslyInvoiced = runningInvoiced;
            if (invoice.InvoiceType == CedentPremiumInvoiceType &&
                !string.Equals(invoice.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                runningInvoiced += invoice.GrossPremium ?? 0m;
            result.Add(MapContractInvoice(invoice, invoiceable, previouslyInvoiced, Math.Max(0m, invoiceable - runningInvoiced)));
        }
        return ServiceResult<IReadOnlyList<PremiumDebitNoteResponseDto>>.Success(result);
    }

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetPremiumInvoiceDocumentAsync(int invoiceId, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<PrintablePaymentDocumentDto>();
        var invoice = await _db.Invoices.AsNoTracking().Include(item => item.IdContractNavigation).ThenInclude(contract => contract.IdCounterpartyNavigation).Include(item => item.IdContractNavigation).ThenInclude(contract => contract.ContractFac).Include(item => item.IdCurrencyNavigation).Include(item => item.PanelAllocations).FirstOrDefaultAsync(item => item.IdInvoice == invoiceId && item.InvoiceType == CedentPremiumInvoiceType, cancellationToken);
        if (invoice is null) return NotFound<PrintablePaymentDocumentDto>("Invoice was not found.");
        var contractGrossPremium = PremiumDebitNoteFinancials.ResolveContractGrossPremium(invoice.IdContractNavigation);
        var previouslyInvoiced = await _db.Invoices.AsNoTracking().Where(item => item.IdContract == invoice.IdContract && item.InvoiceType == CedentPremiumInvoiceType && item.IdInvoice < invoice.IdInvoice && item.Status != "Cancelled").SumAsync(item => item.GrossPremium ?? 0m, cancellationToken);
        var invoicedIncludingCurrent = previouslyInvoiced + (invoice.InvoiceType == CedentPremiumInvoiceType && invoice.Status != "Cancelled" ? invoice.GrossPremium ?? 0m : 0m);
        var lines = new List<string>
        {
            "IRIS - PREMIUM INVOICE",
            $"Number: {invoice.InvoiceNumber}",
            $"Date: {invoice.InvoiceDate:yyyy-MM-dd}",
            $"Contract: {invoice.IdContractNavigation.Name}",
            $"UMR: {invoice.IdContractNavigation.Umr}",
            $"Invoiced to: {invoice.IdContractNavigation.IdCounterpartyNavigation?.Name}",
            $"Cedent address: {invoice.IdContractNavigation.IdCounterpartyNavigation?.AddressLine}",
            $"Cedent location: {invoice.IdContractNavigation.IdCounterpartyNavigation?.City}, {invoice.IdContractNavigation.IdCounterpartyNavigation?.Country}",
            $"Transaction: Original Premium",
            $"Currency: {invoice.IdCurrencyNavigation?.CurrencyCode}",
            $"Gross invoice amount: {invoice.GrossPremium:0.0000} {invoice.IdCurrencyNavigation?.CurrencyCode}",
            $"Contract gross premium: {contractGrossPremium:0.0000}",
            $"Previously invoiced: {previouslyInvoiced:0.0000}",
            $"Remaining after this invoice: {Math.Max(0m, contractGrossPremium - invoicedIncludingCurrent):0.0000}",
            $"Net value due: {invoice.NetPremium:0.0000} {invoice.IdCurrencyNavigation?.CurrencyCode}",
            "Payment instructions: settle to the IRIS collection account configured for the invoice currency.",
            $"Status: {invoice.Status}"
        };
        lines.AddRange(invoice.PanelAllocations.Select(item => $"Reinsurer: {item.ReinsurerNameSnapshot}; Gross: {item.GrossPremium:0.0000}; Cedent commission: {item.CedentCommissionAmount:0.0000} ({item.CedentCommissionPct:0.####}%); IRIS brokerage: {item.BrokerageAmount:0.0000}; Other deductions: {item.OtherDeductions:0.0000}; Net: {item.NetAmount:0.0000}"));
        return ServiceResult<PrintablePaymentDocumentDto>.Success(new PrintablePaymentDocumentDto
        {
            FileName = $"Invoice-{invoice.InvoiceNumber}.pdf",
            Content = BuildPdf(lines),
            ContentType = "application/pdf"
        });
    }

    private async Task<PremiumDebitNoteData> LoadPremiumInvoiceDataAsync(int contractId, CancellationToken cancellationToken)
    {
        var contract = await _db.Contracts.AsNoTracking()
            .Include(item => item.IdCurrencyNavigation)
            .Include(item => item.ContractFac)
            .FirstOrDefaultAsync(item => item.IdContract == contractId, cancellationToken);
        if (contract is null) return PremiumDebitNoteData.Failed("Contract was not found.", StatusCodes.Status404NotFound);
        if (!string.Equals(ContractConstants.NormalizeStatus(contract.Status), ContractConstants.BoundStatus, StringComparison.OrdinalIgnoreCase))
            return PremiumDebitNoteData.Failed("Only Bound contracts are eligible for invoicing.", StatusCodes.Status409Conflict);

        var panels = await _db.ContractPanels.AsNoTracking()
            .Include(item => item.IdCounterpartyNavigation)
            .Where(item => item.IdContract == contractId &&
                item.Role != null && item.Role.Trim().ToLower() == "reinsurer" &&
                item.Status != null && item.Status.Trim().ToLower() == ContractConstants.ConfirmedPanelStatus.ToLower())
            .OrderBy(item => item.IdPanel)
            .ToListAsync(cancellationToken);
        var invoiceableGross = PremiumDebitNoteFinancials.ResolveContractGrossPremium(contract);
        if (invoiceableGross <= 0m)
            return PremiumDebitNoteData.Failed("The contract gross premium is missing or zero.", StatusCodes.Status400BadRequest);
        if (panels.Count == 0 || panels.Sum(item => item.SignedSharePct ?? 0m) <= 0m)
            return PremiumDebitNoteData.Failed("The Bound contract has no confirmed reinsurers with a positive signed share.", StatusCodes.Status400BadRequest);
        var alreadyInvoiced = await _db.Invoices.AsNoTracking()
            .Where(item => item.IdContract == contractId && item.InvoiceType == CedentPremiumInvoiceType && item.Status != "Cancelled")
            .SumAsync(item => item.GrossPremium ?? 0m, cancellationToken);
        return new PremiumDebitNoteData(contract, panels, invoiceableGross, alreadyInvoiced, null, StatusCodes.Status200OK);
    }

    private async Task<ServiceResult<PremiumDebitNoteResponseDto>> MapContractInvoiceAsync(int invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices.AsNoTracking()
            .Include(item => item.IdCurrencyNavigation)
            .Include(item => item.PanelAllocations)
                .ThenInclude(item => item.IdCounterpartyNavigation)
            .FirstOrDefaultAsync(item => item.IdInvoice == invoiceId && item.InvoiceType == CedentPremiumInvoiceType, cancellationToken);
        if (invoice is null) return NotFound<PremiumDebitNoteResponseDto>("Invoice was not found.");
        var already = await _db.Invoices.AsNoTracking()
            .Where(item => item.IdContract == invoice.IdContract && item.InvoiceType == CedentPremiumInvoiceType && item.Status != "Cancelled" && item.IdInvoice < invoice.IdInvoice)
            .SumAsync(item => item.GrossPremium ?? 0m, cancellationToken);
        var contract = await _db.Contracts.AsNoTracking()
            .Include(item => item.ContractFac)
            .SingleOrDefaultAsync(item => item.IdContract == invoice.IdContract, cancellationToken);
        if (contract is null) return NotFound<PremiumDebitNoteResponseDto>("Contract was not found.");
        var invoiceable = PremiumDebitNoteFinancials.ResolveContractGrossPremium(contract);
        return ServiceResult<PremiumDebitNoteResponseDto>.Success(MapContractInvoice(invoice, invoiceable, already, Math.Max(0m, invoiceable - already - (invoice.Status == "Cancelled" ? 0m : invoice.GrossPremium ?? 0m))));
    }

    private static PremiumDebitNoteResponseDto MapContractInvoice(Invoice invoice, decimal invoiceable, decimal already, decimal remaining) => new()
        {
            InvoiceId = invoice.IdInvoice,
            ContractId = invoice.IdContract,
            InvoiceNumber = invoice.InvoiceNumber,
            Sequence = ResolveInvoiceSequence(invoice),
            InvoiceType = invoice.InvoiceType,
            InvoiceDate = invoice.InvoiceDate,
            ContractGrossPremium = invoiceable,
            InvoiceableGrossPremium = invoiceable,
            GrossAmount = invoice.GrossPremium ?? 0m,
            AlreadyInvoicedAmount = already,
            RemainingAmount = remaining,
            TotalNetAmount = invoice.NetPremium ?? 0m,
            CurrencyCode = invoice.IdCurrencyNavigation?.CurrencyCode ?? string.Empty,
            Status = invoice.Status,
            Label = invoice.Label ?? string.Empty,
            Allocations = invoice.PanelAllocations.Select(item => new PremiumDebitNoteAllocationDto
            {
                PanelId = item.IdPanel,
                ReinsurerCode = item.IdCounterpartyNavigation?.LegacyCompanyCode,
                ReinsurerName = string.IsNullOrWhiteSpace(item.ReinsurerNameSnapshot) ? item.IdCounterpartyNavigation?.Name ?? string.Empty : item.ReinsurerNameSnapshot,
                SignedSharePct = item.SignedSharePctSnapshot,
                AllocationRatio = item.AllocationRatio,
                GrossPremium = item.GrossPremium,
                CedentCommissionPct = item.CedentCommissionPct,
                CedentCommissionAmount = item.CedentCommissionAmount,
                BrokeragePct = item.BrokeragePct ?? 0m,
                BrokerageAmount = item.BrokerageAmount,
                OtherDeductions = item.OtherDeductions,
                NetAmount = item.NetAmount,
                CurrencyCode = item.CurrencyCodeSnapshot
            }).ToArray()
        };

    private static int? ResolveInvoiceSequence(Invoice invoice)
    {
        var number = invoice.InvoiceNumber;
        if (number is { Length: >= 15 } && number.StartsWith("INV-", StringComparison.Ordinal) &&
            int.TryParse(number.AsSpan(number.Length - 6), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
            return sequence;

        return invoice.SequenceNumber;
    }

    private async Task<int> AllocateInvoiceNumberSequenceAsync(int year, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            MERGE dbo.INVOICE_NUMBER_SEQUENCE WITH (HOLDLOCK) AS target
            USING (VALUES (@invoice_year)) AS source (invoice_year)
                ON target.invoice_year = source.invoice_year
            WHEN MATCHED THEN
                UPDATE SET last_sequence = target.last_sequence + 1
            WHEN NOT MATCHED THEN
                INSERT (invoice_year, last_sequence) VALUES (source.invoice_year, 1)
            OUTPUT inserted.last_sequence;
            """;
        var yearParameter = command.CreateParameter();
        yearParameter.ParameterName = "@invoice_year";
        yearParameter.DbType = DbType.Int32;
        yearParameter.Value = year;
        command.Parameters.Add(yearParameter);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private sealed record PremiumDebitNoteData(Contract? Contract, IReadOnlyList<ContractPanel>? Panels, decimal InvoiceableGross, decimal AlreadyInvoiced, ApiErrorResponseDto? Error, int ErrorStatusCode)
    {
        public static PremiumDebitNoteData Failed(string message, int statusCode) => new(null, null, 0m, 0m, new ApiErrorResponseDto { Message = message, Errors = [], Code = statusCode == StatusCodes.Status404NotFound ? "NOT_FOUND" : null }, statusCode);
    }


    public async Task<ServiceResult<InvoiceResponseDto>> CreateInvoiceAsync(InvoiceRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<InvoiceResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<InvoiceResponseDto>();
        var amount = request.Amount ?? request.NetPremium;
        if (!amount.HasValue || amount <= 0m) return Failure<InvoiceResponseDto>("Invoice amount must be greater than zero.");
        if (!request.ReceiptAllocationId.HasValue) return Failure<InvoiceResponseDto>("receiptAllocationId is required for a paired encaissement invoice.");
        if (!request.PanelId.HasValue) return Failure<InvoiceResponseDto>("panelId is required for a reinsurer invoice.");

        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var contract = await _db.Contracts.FirstOrDefaultAsync(item => item.IdContract == request.ContractId, cancellationToken);
        if (contract is null) return NotFound<InvoiceResponseDto>("Contract was not found.");
        if (!string.Equals(ContractConstants.NormalizeStatus(contract.Status), ContractConstants.BoundStatus, StringComparison.OrdinalIgnoreCase))
            return Failure<InvoiceResponseDto>("Only Bound contracts are eligible for invoicing.", status: StatusCodes.Status409Conflict);
        var allocation = await _db.ReceiptAllocations.FirstOrDefaultAsync(item => item.IdReceiptAllocation == request.ReceiptAllocationId && item.IdContract == request.ContractId, cancellationToken);
        if (allocation is null) return Failure<InvoiceResponseDto>("The receipt allocation does not belong to the selected contract.");
        var panel = await _db.ContractPanels.FirstOrDefaultAsync(item => item.IdPanel == request.PanelId && item.IdContract == request.ContractId, cancellationToken);
        if (panel is null ||
            !string.Equals(panel.Status?.Trim(), ContractConstants.ConfirmedPanelStatus, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(panel.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase))
            return Failure<InvoiceResponseDto>("The selected reinsurer panel is not eligible for invoicing.");
        var validReinsurer = await _db.Counterparties.AsNoTracking().AnyAsync(counterparty =>
            counterparty.Id == panel.IdCounterparty &&
            counterparty.IsActive &&
            counterparty.TypeAssignments.Any(type => type.Type.Trim().ToLower() == CounterpartyTypes.Reinsurer.ToLower()), cancellationToken);
        if (!validReinsurer) return Failure<InvoiceResponseDto>("The selected panel does not reference an active Reinsurer counterparty.");
        var alreadyInvoiced = await _db.Invoices.Where(item => item.IdReceiptAllocation == allocation.IdReceiptAllocation && item.Status != "Cancelled").SumAsync(item => item.NetPremium ?? 0m, cancellationToken);
        var available = (allocation.AllocatedAmount ?? 0m) - alreadyInvoiced;
        if (amount > available) return Failure<InvoiceResponseDto>("Invoice amount exceeds the available paired amount.", status: StatusCodes.Status409Conflict);
        if (await _db.Invoices.AnyAsync(item =>
                item.IdReceiptAllocation == allocation.IdReceiptAllocation &&
                item.IdPanel == panel.IdPanel &&
                item.Status != "Cancelled", cancellationToken))
            return Failure<InvoiceResponseDto>("An active invoice already exists for this allocation and reinsurer panel.", status: StatusCodes.Status409Conflict);
        if (request.RemittanceRef.HasValue && await _db.Invoices.AnyAsync(item => item.RemittanceRef == request.RemittanceRef, cancellationToken)) return Failure<InvoiceResponseDto>("An invoice already exists for this remittance.", status: StatusCodes.Status409Conflict);

        var now = DateTime.UtcNow;
        var invoiceDate = request.InvoiceDate == default ? DateOnly.FromDateTime(now) : request.InvoiceDate;
        var sequence = await AllocateInvoiceNumberSequenceAsync(invoiceDate.Year, cancellationToken);
        if (sequence > 999999)
            return Failure<InvoiceResponseDto>($"The invoice-number sequence for {invoiceDate.Year} has reached its six-digit limit.", status: StatusCodes.Status409Conflict);
        var invoiceType = string.IsNullOrWhiteSpace(request.InvoiceType) ? "REINSURER_PAYABLE" : request.InvoiceType.Trim();
        if (string.Equals(invoiceType, "Reinsurer", StringComparison.OrdinalIgnoreCase)) invoiceType = "REINSURER_PAYABLE";
        var entity = new Invoice
        {
            IdContract = request.ContractId,
            IdReceiptAllocation = allocation.IdReceiptAllocation,
            IdCurrency = allocation.IdCurrency,
            IdCounterparty = panel.IdCounterparty,
            IdPanel = panel.IdPanel,
            RemittanceRef = request.RemittanceRef,
            InvoiceNumber = CedentInvoiceNumber.Format(invoiceDate.Year, sequence),
            InvoiceType = invoiceType,
            InvoiceDate = invoiceDate,
            GrossPremium = amount,
            RecoveryStatus = "Unpaired",
            SequenceNumber = sequence,
            NetPremium = amount,
            Label = request.Label?.Trim() ?? string.Empty,
            SettlementMethod = request.SettlementMethod?.Trim(),
            Status = "Draft",
            CreatedAt = now,
            CreatedBy = user,
            IdUser = user
        };
        _db.Invoices.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var response = ToInvoice(entity);
        response.ReinsurerName = await _db.Counterparties.AsNoTracking()
            .Where(counterparty => counterparty.Id == panel.IdCounterparty)
            .Select(counterparty => counterparty.Name)
            .FirstOrDefaultAsync(cancellationToken);
        response.AffectedAmount = amount.Value;
        response.UnaffectedAmount = Math.Max(0m, available - amount.Value);
        response.AllocatedAmount = allocation.AllocatedAmount ?? 0m;
        response.AlreadyInvoicedAmount = alreadyInvoiced + amount.Value;
        response.AvailableAmount = response.UnaffectedAmount;
        return ServiceResult<InvoiceResponseDto>.Success(response, StatusCodes.Status201Created);
    }

    public async Task<ServiceResult<InvoiceResponseDto>> GetInvoiceAsync(int id, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<InvoiceResponseDto>();
        var invoice = await _db.Invoices.AsNoTracking()
            .Where(x => x.IdInvoice == id)
            .Select(ToInvoiceExpression)
            .FirstOrDefaultAsync(cancellationToken);
        return invoice is null
            ? NotFound<InvoiceResponseDto>("Invoice was not found.")
            : ServiceResult<InvoiceResponseDto>.Success(invoice);
    }

    public async Task<ServiceResult<FinancialHistoryDto>> GetFinancialHistoryAsync(int contractId, CancellationToken cancellationToken)
    {
        if (!CanFinanceOrBack()) return Forbidden<FinancialHistoryDto>();
        if (!await _db.Contracts.AnyAsync(x => x.IdContract == contractId, cancellationToken)) return NotFound<FinancialHistoryDto>("Contract was not found.");
        var receiptIds = await _db.ReceiptAllocations.Where(x => x.IdPanel.HasValue && _db.ContractPanels.Any(p => p.IdPanel == x.IdPanel && p.IdContract == contractId)).Select(x => x.IdBankReceipt).Distinct().ToListAsync(cancellationToken);
        var receipts = await _db.BankReceipts.AsNoTracking().Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation).Include(x => x.ReceiptAllocations).Where(x => receiptIds.Contains(x.IdBankReceipt)).Select(ToReceiptExpression).ToListAsync(cancellationToken);
        var remittances = await _db.RemittanceRefs.AsNoTracking().Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation).Where(x => x.IdPanel.HasValue && _db.ContractPanels.Any(p => p.IdPanel == x.IdPanel && p.IdContract == contractId)).Select(x => new RemittanceResponseDto { RemittanceRef = x.RemittanceRef1, ReceiptAllocationId = x.IdReceiptAllocation, DisbursementId = x.IdDisbursement, PanelId = x.IdPanel, CounterpartyId = x.IdCounterparty, CounterpartyName = x.IdCounterpartyNavigation.Name, NetPremiumDue = x.NetPremiumDue, NetPayableAmount = x.NetPayableAmount, CurrencyId = x.IdCurrency, CurrencyCode = x.IdCurrencyNavigation.CurrencyCode, Status = x.Status, Description = x.Description }).ToListAsync(cancellationToken);
        var invoices = await _db.Invoices.AsNoTracking().Where(x => x.IdContract == contractId).Select(ToInvoiceExpression).ToListAsync(cancellationToken);
        var disbIds = remittances.Where(x => x.DisbursementId.HasValue).Select(x => x.DisbursementId!.Value).ToArray(); var disbursements = await _db.Disbursements.AsNoTracking().Include(x => x.IdCounterpartyNavigation).Include(x => x.IdBankAccountNavigation).ThenInclude(x => x.IdCurrencyNavigation).Where(x => disbIds.Contains(x.IdDisbursement)).Select(x => new DisbursementResponseDto { IdDisbursement = x.IdDisbursement, CounterpartyId = x.IdCounterparty, CounterpartyName = x.IdCounterpartyNavigation.Name, TotalAmount = x.TotalAmount, CurrencyId = x.IdBankAccountNavigation.IdCurrency, CurrencyCode = x.IdBankAccountNavigation.IdCurrencyNavigation.CurrencyCode, OperationDate = x.OperationDate, SignatureStatus = x.SignatureStatus }).ToListAsync(cancellationToken);
        return ServiceResult<FinancialHistoryDto>.Success(new FinancialHistoryDto { Receipts = receipts, Remittances = remittances, Invoices = invoices, Disbursements = disbursements });
    }

    public async Task<ServiceResult<CedentClosingResponseDto>> CreateCedentClosingAsync(int contractId, CedentClosingRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<CedentClosingResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<CedentClosingResponseDto>();
        if (!await _db.Contracts.AnyAsync(x => x.IdContract == contractId, cancellationToken)) return NotFound<CedentClosingResponseDto>("Contract was not found.");
        if (!await _db.Counterparties.AnyAsync(x => x.Id == request.CounterpartyId && x.IsActive && x.TypeAssignments.Any(t => t.Type.ToLower() == CounterpartyTypes.Cedent.ToLower()), cancellationToken)) return Failure<CedentClosingResponseDto>("An active Cedent counterparty is required.");
        var item = new PremiumRef { IdContract = contractId, IdCounterparty = request.CounterpartyId, IdCurrency = request.CurrencyId, ClosingInformation = request.ClosingInformation?.Trim() ?? string.Empty, OperationDate = request.OperationDate, Status = "Draft", CreatedAt = DateTime.UtcNow, CreatedBy = user, IdUser = user };
        _db.PremiumRefs.Add(item); await _db.SaveChangesAsync(cancellationToken); return ServiceResult<CedentClosingResponseDto>.Success(new CedentClosingResponseDto { PremiumRef = item.PremiumRef1, ContractId = contractId, CounterpartyId = item.IdCounterparty, CurrencyId = item.IdCurrency, Status = item.Status, ClosingInformation = item.ClosingInformation, OperationDate = item.OperationDate }, StatusCodes.Status201Created);
    }

    private async Task<string?> ValidateBankAccountAsync(BankAccountRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BankName)) return "Bank name is required.";
        if (string.IsNullOrWhiteSpace(request.BankCity)) return "Bank city is required.";
        if (string.IsNullOrWhiteSpace(request.BankCountry)) return "Bank country is required.";
        if (string.IsNullOrWhiteSpace(request.Iban)) return "IBAN is required.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(NormalizeIban(request.Iban) ?? string.Empty, "^[A-Z]{2}[0-9A-Z]{13,32}$")) return "IBAN format is invalid.";
        if (string.IsNullOrWhiteSpace(request.SwiftBic) || !System.Text.RegularExpressions.Regex.IsMatch(NormalizeBic(request.SwiftBic) ?? string.Empty, "^[A-Z0-9]{8}([A-Z0-9]{3})?$") ) return "SWIFT/BIC format is invalid.";
        if (request.HasCorrespondentBank && string.IsNullOrWhiteSpace(request.CorrespondentBankName)) return "Correspondent bank name is required when correspondent bank is enabled.";
        if (request.HasCorrespondentBank && (string.IsNullOrWhiteSpace(request.CorrespondentSwiftBic) || !System.Text.RegularExpressions.Regex.IsMatch(NormalizeBic(request.CorrespondentSwiftBic) ?? string.Empty, "^[A-Z0-9]{8}([A-Z0-9]{3})?$"))) return "Correspondent SWIFT/BIC format is invalid.";
        if (!await _db.Currencies.AnyAsync(x => x.IdCurrency == request.CurrencyId, cancellationToken)) return "Currency was not found.";
        return null;
    }

    private async Task ClearDefaultAsync(int counterpartyId, int currencyId, int? exceptId, string user, CancellationToken cancellationToken)
    {
        var accounts = await _db.CounterpartyBankAccounts.Where(x => x.IdCounterparty == counterpartyId && x.IdCurrency == currencyId && x.IsDefault && (!exceptId.HasValue || x.IdBankAccount != exceptId)).ToListAsync(cancellationToken);
        foreach (var account in accounts) { account.IsDefault = false; account.UpdatedAt = DateTime.UtcNow; account.UpdatedBy = user; }
    }

    private async Task NotifyRoleAsync(string position, string title, string message, string entityType, int entityId, CancellationToken cancellationToken)
    {
        var users = await _db.AspNetUsers.Where(x => x.Position != null && x.Position.Trim().ToLower() == position.ToLower()).Select(x => x.Id).ToListAsync(cancellationToken);
        var creator = CurrentUser();
        var targetRoute = entityType == "BANK_RECEIPT" ? $"/back-office/bank-receipts/{entityId}/pairing" : entityType == "REMITTANCE_REF" ? "/finance/disbursement-queue" : "/finance/disbursements";
        foreach (var id in users) _db.Notifications.Add(new Notification { RecipientUserId = id, RecipientRole = position, NotificationType = entityType == "BANK_RECEIPT" ? "ReceiptRequiresPairing" : entityType, Title = title, Message = message, EntityType = entityType, EntityId = entityId, TargetRoute = targetRoute, CreatedAt = DateTime.UtcNow, CreatedBy = creator });
    }

    private static byte[] BuildPdf(IEnumerable<string> lines)
    {
        static string Escape(string value)
        {
            var builder = new System.Text.StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (character is '(' or ')' or '\\') builder.Append('\\');
                builder.Append(character > 127 ? '?' : character);
            }
            return builder.ToString();
        }

        var stream = new System.Text.StringBuilder("BT /F1 9 Tf 50 760 Td\n");
        var first = true;
        foreach (var line in lines)
        {
            if (!first) stream.Append("0 -18 Td\n");
            stream.Append('(').Append(Escape(line)).Append(") Tj\n");
            first = false;
        }
        stream.Append("ET");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {System.Text.Encoding.ASCII.GetByteCount(stream.ToString())} >>\nstream\n{stream}\nendstream"
        };
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(System.Text.Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }
        var xrefOffset = System.Text.Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        for (var index = 1; index < offsets.Count; index++) pdf.Append(offsets[index].ToString("0000000000")).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xrefOffset).Append("\n%%EOF");
        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private string? CurrentUser() => _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool CanFinance() => FinancialAccessRules.IsFinanceOfficer(_http.HttpContext?.User ?? new ClaimsPrincipal());
    private bool CanBack() => FinancialAccessRules.IsBackOfficer(_http.HttpContext?.User ?? new ClaimsPrincipal());
    private bool CanFinanceOrBack() => FinancialAccessRules.IsFinanceOrBackOfficer(_http.HttpContext?.User ?? new ClaimsPrincipal());
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeIban(string? value) => Optional(value)?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    private static string? NormalizeBic(string? value) => Optional(value)?.ToUpperInvariant();
    private static string? MaskIban(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= 4 ? value : $"{value[..2]}****{value[^4..]}";
    private static BankAccountResponseDto MapBankAccount(CounterpartyBankAccount x) => new() { IdBankAccount = x.IdBankAccount, IdCounterparty = x.IdCounterparty, BeneficiaryName = x.IdCounterpartyNavigation?.Name ?? string.Empty, BeneficiaryAddress = x.IdCounterpartyNavigation?.AddressLine, BeneficiaryCity = x.IdCounterpartyNavigation?.City, BeneficiaryCountry = x.IdCounterpartyNavigation?.Country, CurrencyId = x.IdCurrency, CurrencyCode = x.IdCurrencyNavigation?.CurrencyCode ?? string.Empty, BankName = x.BankName, BankCity = x.BankCity, BankCountry = x.BankCountry, AgencyCode = x.AgencyCode, Rib = x.Rib, Iban = x.Iban, MaskedIban = MaskIban(x.Iban), SwiftBic = x.SwiftBic, HasCorrespondentBank = x.HasCorrespondentBank, CorrespondentBankName = x.CorrespondentBankName, CorrespondentSwiftBic = x.CorrespondentSwiftBic, AccountLabel = x.AccountLabel, IsDefault = x.IsDefault, IsActive = x.IsActive };
    private static readonly System.Linq.Expressions.Expression<Func<CounterpartyBankAccount, BankAccountResponseDto>> MapBankAccountExpression = x => new() { IdBankAccount = x.IdBankAccount, IdCounterparty = x.IdCounterparty, BeneficiaryName = x.IdCounterpartyNavigation.Name, BeneficiaryAddress = x.IdCounterpartyNavigation.AddressLine, BeneficiaryCity = x.IdCounterpartyNavigation.City, BeneficiaryCountry = x.IdCounterpartyNavigation.Country, CurrencyId = x.IdCurrency, CurrencyCode = x.IdCurrencyNavigation.CurrencyCode, BankName = x.BankName, BankCity = x.BankCity, BankCountry = x.BankCountry, AgencyCode = x.AgencyCode, Rib = x.Rib, MaskedIban = x.Iban == null ? null : x.Iban.Length <= 4 ? x.Iban : x.Iban.Substring(0, 2) + "****" + x.Iban.Substring(x.Iban.Length - 4, 4), SwiftBic = x.SwiftBic, HasCorrespondentBank = x.HasCorrespondentBank, CorrespondentBankName = x.CorrespondentBankName, CorrespondentSwiftBic = x.CorrespondentSwiftBic, AccountLabel = x.AccountLabel, IsDefault = x.IsDefault, IsActive = x.IsActive };
    private static readonly System.Linq.Expressions.Expression<Func<BankReceipt, BankReceiptResponseDto>> ToReceiptExpression = x => new() { IdBankReceipt = x.IdBankReceipt, BankAccountId = x.IdBankAccount, CounterpartyId = x.IdCounterparty, CounterpartyName = x.IdCounterpartyNavigation.Name, BankName = x.IdBankAccountNavigation.BankName, MaskedIban = x.IdBankAccountNavigation.Iban == null || x.IdBankAccountNavigation.Iban == "" ? null : x.IdBankAccountNavigation.Iban.Length <= 4 ? x.IdBankAccountNavigation.Iban : x.IdBankAccountNavigation.Iban.Substring(0, 2) + "****" + x.IdBankAccountNavigation.Iban.Substring(x.IdBankAccountNavigation.Iban.Length - 4, 4), ReceptionDate = x.ReceptionDate, OperationDate = x.OperationDate, ReceiptMonth = x.OperationDate.Year.ToString() + "-" + (x.OperationDate.Month < 10 ? "0" : "") + x.OperationDate.Month.ToString(), AmountReceived = x.AmountReceived, CurrencyId = x.IdCurrency, CurrencyCode = x.IdCurrencyNavigation.CurrencyCode, AppliedExchangeRate = x.AppliedExchangeRate, AvailableAmount = x.AmountReceived - x.ReceiptAllocations.Sum(a => a.AllocatedAmount ?? 0m), AllocatedAmount = x.ReceiptAllocations.Sum(a => a.AllocatedAmount ?? 0m), PairingStatus = x.AmountReceived > 0m && x.ReceiptAllocations.Sum(a => a.AllocatedAmount ?? 0m) >= x.AmountReceived ? "Paired" : "Unpaired", SettlementMethod = x.SettlementMethod, CollectionType = x.CollectionType, BankReference = x.BankReference, SettlementLabel = x.SettlementLabel, BusinessReference = x.BusinessReference, DeclaredInvoiceNumber = x.DeclaredInvoiceNumber, DeclaredInvoiceMonth = x.DeclaredInvoiceMonth, Reason = x.Reason };
    private static BankReceiptResponseDto ToReceiptEntity(BankReceipt x) => new() { IdBankReceipt = x.IdBankReceipt, BankAccountId = x.IdBankAccount, CounterpartyId = x.IdCounterparty, CounterpartyName = x.IdCounterpartyNavigation?.Name ?? string.Empty, BankName = x.IdBankAccountNavigation?.BankName ?? string.Empty, MaskedIban = MaskIban(x.IdBankAccountNavigation?.Iban), ReceptionDate = x.ReceptionDate, OperationDate = x.OperationDate, ReceiptMonth = x.OperationDate.ToString("yyyy-MM"), AmountReceived = x.AmountReceived, CurrencyId = x.IdCurrency, CurrencyCode = x.IdCurrencyNavigation?.CurrencyCode ?? string.Empty, AppliedExchangeRate = x.AppliedExchangeRate, AvailableAmount = x.AmountReceived - x.ReceiptAllocations.Sum(a => a.AllocatedAmount ?? 0m), AllocatedAmount = x.ReceiptAllocations.Sum(a => a.AllocatedAmount ?? 0m), PairingStatus = InvoiceRecoveryCalculator.GetReceiptPairingStatus(x.AmountReceived, x.ReceiptAllocations.Sum(a => a.AllocatedAmount ?? 0m)), SettlementMethod = x.SettlementMethod, CollectionType = x.CollectionType, BankReference = x.BankReference, SettlementLabel = x.SettlementLabel, BusinessReference = x.BusinessReference, DeclaredInvoiceNumber = x.DeclaredInvoiceNumber, DeclaredInvoiceMonth = x.DeclaredInvoiceMonth, Reason = x.Reason };
    private static RemittanceResponseDto ToRemittance(RemittanceRef x) => new() { RemittanceRef = x.RemittanceRef1, RemittanceReference = x.RemittanceRef1.ToString(), ReceiptAllocationId = x.IdReceiptAllocation, DisbursementId = x.IdDisbursement, PanelId = x.IdPanel, ContractId = x.IdPanelNavigation?.IdContract, ContractName = x.IdPanelNavigation?.IdContractNavigation?.Name, Umr = x.IdPanelNavigation?.IdContractNavigation?.Umr, CedentName = x.IdPanelNavigation?.IdContractNavigation?.IdCounterpartyNavigation?.Name, CounterpartyId = x.IdCounterparty, CounterpartyName = x.IdCounterpartyNavigation?.Name ?? string.Empty, GrossPremium = x.PremiumSignedLine, OtherDeductions = x.Deductions, NetPremiumDue = x.NetPremiumDue, NetPayableAmount = x.NetPayableAmount, CurrencyId = x.IdCurrency, CurrencyCode = x.IdCurrencyNavigation?.CurrencyCode ?? string.Empty, Status = x.Status, Description = x.Description, ConfirmedAt = x.ConfirmedAt, ConfirmedBy = x.ConfirmedBy };
    private static DisbursementResponseDto ToDisbursement(Disbursement x, RemittanceRef? remittance) => new() { IdDisbursement = x.IdDisbursement, RemittanceRef = remittance?.RemittanceRef1 ?? 0, CounterpartyId = x.IdCounterparty, CounterpartyName = x.IdCounterpartyNavigation?.Name ?? remittance?.IdCounterpartyNavigation?.Name ?? string.Empty, TotalAmount = x.TotalAmount, CurrencyId = x.IdBankAccountNavigation?.IdCurrency ?? remittance?.IdCurrency ?? 0, CurrencyCode = x.IdBankAccountNavigation?.IdCurrencyNavigation?.CurrencyCode ?? remittance?.IdCurrencyNavigation?.CurrencyCode ?? string.Empty, OperationDate = x.OperationDate, SignatureStatus = x.SignatureStatus };
    private static InvoiceResponseDto ToInvoice(Invoice x)
    {
        var gross = x.GrossPremium ?? 0m;
        var paired = x.ReceiptAllocations.Sum(allocation => allocation.AllocatedAmount ?? 0m);
        var recovery = InvoiceRecoveryCalculator.Calculate(gross, paired);
        return new InvoiceResponseDto
        {
            IdInvoice = x.IdInvoice,
            InvoiceId = x.IdInvoice,
            ContractId = x.IdContract,
            ContractName = x.IdContractNavigation?.Name ?? string.Empty,
            Umr = x.IdContractNavigation?.Umr,
            CedentId = x.IdContractNavigation?.IdCounterparty ?? 0,
            CedentName = x.IdContractNavigation?.IdCounterpartyNavigation?.Name,
            CounterpartyName = x.IdCounterpartyNavigation?.Name ?? x.IdContractNavigation?.IdCounterpartyNavigation?.Name,
            ReceiptAllocationId = x.IdReceiptAllocation,
            CurrencyId = x.IdCurrency,
            CurrencyCode = x.IdCurrencyNavigation?.CurrencyCode,
            CounterpartyId = x.IdCounterparty ?? x.IdContractNavigation?.IdCounterparty,
            PanelId = x.IdPanel,
            RemittanceRef = x.RemittanceRef,
            InvoiceNumber = x.InvoiceNumber,
            InvoiceType = x.InvoiceType,
            InvoiceDate = x.InvoiceDate,
            GrossAmount = gross,
            NetPremium = x.NetPremium,
            NetAmount = x.NetPremium,
            RecoveryStatus = recovery.Status,
            PairingStatus = recovery.Status,
            PairedAmount = recovery.PairedAmount,
            OutstandingAmount = recovery.OutstandingAmount,
            Status = x.Status,
            Label = x.Label ?? string.Empty
        };
    }

    private static readonly System.Linq.Expressions.Expression<Func<Invoice, InvoiceResponseDto>> ToInvoiceExpression = x => new()
    {
        IdInvoice = x.IdInvoice,
        InvoiceId = x.IdInvoice,
        ContractId = x.IdContract,
        ContractName = x.IdContractNavigation.Name,
        Umr = x.IdContractNavigation.Umr,
        CedentId = x.IdContractNavigation.IdCounterparty,
        CedentName = x.IdContractNavigation.IdCounterpartyNavigation.Name,
        CounterpartyName = x.IdCounterpartyNavigation == null ? x.IdContractNavigation.IdCounterpartyNavigation.Name : x.IdCounterpartyNavigation.Name,
        ReceiptAllocationId = x.IdReceiptAllocation,
        CurrencyId = x.IdCurrency,
        CurrencyCode = x.IdCurrencyNavigation == null ? null : x.IdCurrencyNavigation.CurrencyCode,
        CounterpartyId = x.IdCounterparty ?? x.IdContractNavigation.IdCounterparty,
        PanelId = x.IdPanel,
        RemittanceRef = x.RemittanceRef,
        InvoiceNumber = x.InvoiceNumber,
        InvoiceType = x.InvoiceType,
        InvoiceDate = x.InvoiceDate,
        GrossAmount = x.GrossPremium ?? 0m,
        NetPremium = x.NetPremium,
        NetAmount = x.NetPremium,
        PairedAmount = x.ReceiptAllocations.Sum(allocation => allocation.AllocatedAmount ?? 0m),
        OutstandingAmount = (x.GrossPremium ?? 0m) > x.ReceiptAllocations.Sum(allocation => allocation.AllocatedAmount ?? 0m)
            ? (x.GrossPremium ?? 0m) - x.ReceiptAllocations.Sum(allocation => allocation.AllocatedAmount ?? 0m)
            : 0m,
        RecoveryStatus = (x.GrossPremium ?? 0m) > 0m && x.ReceiptAllocations.Sum(allocation => allocation.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m) ? "Paired" : "Unpaired",
        PairingStatus = (x.GrossPremium ?? 0m) > 0m && x.ReceiptAllocations.Sum(allocation => allocation.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m) ? "Paired" : "Unpaired",
        Status = x.Status,
        Label = x.Label ?? string.Empty
    };
    private static bool TryGetRemittanceReference(string? caseReference, out int remittanceRef)
    {
        remittanceRef = 0;
        return caseReference is not null && caseReference.StartsWith("REM-", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(caseReference[4..], out remittanceRef);
    }

    private async Task<BankAccountSnapshotDto?> GetBankAccountSnapshotAsync(int bankAccountId, int counterpartyId, int currencyId, CancellationToken cancellationToken)
    {
        var account = await _db.CounterpartyBankAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdBankAccount == bankAccountId && x.IdCounterparty == counterpartyId && x.IdCurrency == currencyId && x.IsActive, cancellationToken);
        return account is null ? null : MapBankAccountSnapshot(account);
    }

    private static BankAccountSnapshotDto MapBankAccountSnapshot(CounterpartyBankAccount account) => new()
    {
        IdBankAccount = account.IdBankAccount,
        BankName = account.BankName,
        BankCity = account.BankCity,
        BankCountry = account.BankCountry,
        Iban = account.Iban,
        SwiftBic = account.SwiftBic,
        CorrespondentBankName = account.CorrespondentBankName,
        CorrespondentSwiftBic = account.CorrespondentSwiftBic,
        IsDefault = account.IsDefault
    };

    private static ServiceResult<T> NotFound<T>(string message, string? code = null) => ServiceResult<T>.Failure(message, statusCode: StatusCodes.Status404NotFound, code: code);
    private static ServiceResult<T> Unauthorized<T>() => ServiceResult<T>.Failure("Authentication is required.", statusCode: StatusCodes.Status401Unauthorized);
    private static ServiceResult<T> Forbidden<T>() => ServiceResult<T>.Failure("You are not authorized for this financial operation.", statusCode: StatusCodes.Status403Forbidden);
    private static ServiceResult<T> Failure<T>(string message, IEnumerable<string>? errors = null, int status = StatusCodes.Status400BadRequest) => ServiceResult<T>.Failure(message, errors, status);
}
