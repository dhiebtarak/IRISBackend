using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Globalization;
using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService : IFinancialWorkflowService
{
    private const string CedentPremiumInvoiceType = "CEDENT_PREMIUM_INVOICE";
    private const string InvoiceTypeStandard = "INVOICE";
    private const string InvoiceTypeFees = "INVOICE_FEES";
    private const string InvoiceTypeAvoir = "AVOIR";
    private const string DebitNotePdfTemplateVersion = "debit-note-v2";
    private readonly IRISBackendContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<FinancialWorkflowService> _logger;
    private readonly IrisInvoiceBrandingOptions _invoiceBranding;
    private readonly IrisBankAccountsOptions _irisBankAccounts;
    private readonly IConfiguration? _configuration;
    private readonly Microsoft.AspNetCore.Hosting.IWebHostEnvironment? _hostingEnvironment;

    public FinancialWorkflowService(
        IRISBackendContext db,
        IHttpContextAccessor http,
        ILogger<FinancialWorkflowService> logger,
        IOptions<IrisInvoiceBrandingOptions>? invoiceBranding = null,
        Microsoft.AspNetCore.Hosting.IWebHostEnvironment? hostingEnvironment = null,
        IOptions<IrisBankAccountsOptions>? irisBankAccounts = null,
        IConfiguration? configuration = null)
    {
        _db = db;
        _http = http;
        _logger = logger;
        _invoiceBranding = invoiceBranding?.Value ?? new IrisInvoiceBrandingOptions();
        _irisBankAccounts = irisBankAccounts?.Value ?? new IrisBankAccountsOptions();
        _configuration = configuration;
        _hostingEnvironment = hostingEnvironment;
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
        var receipt = new BankReceipt { IdBankAccount = account.IdBankAccount, IdCounterparty = account.IdCounterparty, ReceptionDate = DateOnly.FromDateTime(now), OperationDate = operationMonth, AmountReceived = request.AmountReceived, IdCurrency = account.IdCurrency, AppliedExchangeRate = request.AppliedExchangeRate, SettlementMethod = request.SettlementMethod.Trim(), BankReference = Optional(request.BankReference), SettlementLabel = businessReference, BusinessReference = businessReference, DeclaredInvoiceNumber = Optional(request.DeclaredInvoiceNumber), DeclaredInvoiceMonth = declaredInvoiceMonth, Reason = Optional(request.Reason), DetailsStatus = "Unpaired", CreatedAt = now, CreatedBy = user };
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
            .Where(invoice => invoice.InvoiceType != null
                && (invoice.InvoiceType == CedentPremiumInvoiceType || invoice.InvoiceType == InvoiceTypeStandard || invoice.InvoiceType == InvoiceTypeFees || invoice.InvoiceType == InvoiceTypeAvoir)
                && (invoice.InvoiceType != InvoiceTypeAvoir || invoice.ProcessingMode == null || invoice.ProcessingMode != InvoiceDocumentAccounting.DirectToRemittances)
                && invoice.Status != null && invoice.Status.Trim().ToLower() != "cancelled"
                && invoice.IdCurrency == receipt.IdCurrency
                && invoice.IdContractNavigation.IdCounterparty == receipt.IdCounterparty
                && (invoice.GrossPremium ?? 0m) > _db.ReceiptAllocations
                    .Where(allocation => allocation.IsActive && allocation.IdInvoice == invoice.IdInvoice)
                    .Sum(allocation => allocation.AllocatedAmount ?? 0m)
                    + (invoice.InvoiceType == InvoiceTypeAvoir
                        ? _db.InvoiceCreditApplications.Where(application => application.IsActive && application.AvoirInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)
                            + _db.AvoirRefundSettlements.Where(refund => refund.IsActive && refund.AvoirInvoiceId == invoice.IdInvoice).Sum(refund => refund.Amount)
                        : _db.InvoiceCreditApplications.Where(application => application.IsActive && application.TargetInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)));
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
                DocumentNumber = invoice.InvoiceNumber,
                InvoiceType = invoice.InvoiceType,
                SignedAmount = invoice.InvoiceType == InvoiceTypeAvoir ? -(invoice.GrossPremium ?? 0m) : (invoice.GrossPremium ?? 0m),
                IsAvoir = invoice.InvoiceType == InvoiceTypeAvoir,
                Label = invoice.Label,
                ContractId = invoice.IdContract,
                ContractName = invoice.IdContractNavigation.Name,
                Umr = invoice.IdContractNavigation.Umr,
                CedentId = invoice.IdContractNavigation.IdCounterparty,
                CedentName = invoice.IdContractNavigation.IdCounterpartyNavigation.Name,
                InvoiceAmount = invoice.GrossPremium ?? 0m,
                SettledAmount = _db.ReceiptAllocations.Where(allocation => allocation.IsActive && allocation.IdInvoice == invoice.IdInvoice).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                    + (invoice.InvoiceType == InvoiceTypeAvoir
                        ? _db.InvoiceCreditApplications.Where(application => application.IsActive && application.AvoirInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)
                            + _db.AvoirRefundSettlements.Where(refund => refund.IsActive && refund.AvoirInvoiceId == invoice.IdInvoice).Sum(refund => refund.Amount)
                        : _db.InvoiceCreditApplications.Where(application => application.IsActive && application.TargetInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)),
                RemainingAmount = (invoice.GrossPremium ?? 0m) > _db.ReceiptAllocations.Where(allocation => allocation.IsActive && allocation.IdInvoice == invoice.IdInvoice).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                    + (invoice.InvoiceType == InvoiceTypeAvoir
                        ? _db.InvoiceCreditApplications.Where(application => application.IsActive && application.AvoirInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)
                            + _db.AvoirRefundSettlements.Where(refund => refund.IsActive && refund.AvoirInvoiceId == invoice.IdInvoice).Sum(refund => refund.Amount)
                        : _db.InvoiceCreditApplications.Where(application => application.IsActive && application.TargetInvoiceId == invoice.IdInvoice).Sum(application => application.Amount))
                    ? (invoice.GrossPremium ?? 0m) - _db.ReceiptAllocations.Where(allocation => allocation.IsActive && allocation.IdInvoice == invoice.IdInvoice).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                        - (invoice.InvoiceType == InvoiceTypeAvoir
                            ? _db.InvoiceCreditApplications.Where(application => application.IsActive && application.AvoirInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)
                            : _db.InvoiceCreditApplications.Where(application => application.IsActive && application.TargetInvoiceId == invoice.IdInvoice).Sum(application => application.Amount))
                    : 0m,
                PaymentStatus = _db.ReceiptAllocations.Where(allocation => allocation.IsActive && allocation.IdInvoice == invoice.IdInvoice).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                    + (invoice.InvoiceType == InvoiceTypeAvoir
                        ? _db.InvoiceCreditApplications.Where(application => application.IsActive && application.AvoirInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)
                            + _db.AvoirRefundSettlements.Where(refund => refund.IsActive && refund.AvoirInvoiceId == invoice.IdInvoice).Sum(refund => refund.Amount)
                        : _db.InvoiceCreditApplications.Where(application => application.IsActive && application.TargetInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)) <= 0m
                    ? InvoiceRecoveryCalculator.Unsettled
                    : _db.ReceiptAllocations.Where(allocation => allocation.IsActive && allocation.IdInvoice == invoice.IdInvoice).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                        + (invoice.InvoiceType == InvoiceTypeAvoir
                            ? _db.InvoiceCreditApplications.Where(application => application.IsActive && application.AvoirInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)
                                + _db.AvoirRefundSettlements.Where(refund => refund.IsActive && refund.AvoirInvoiceId == invoice.IdInvoice).Sum(refund => refund.Amount)
                            : _db.InvoiceCreditApplications.Where(application => application.IsActive && application.TargetInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)) >= (invoice.GrossPremium ?? 0m)
                        ? InvoiceRecoveryCalculator.Settled
                        : InvoiceRecoveryCalculator.PartiallySettled,
                GrossAmount = invoice.GrossPremium ?? 0m,
                AlreadyPairedAmount = _db.ReceiptAllocations.Where(allocation => allocation.IsActive && allocation.IdInvoice == invoice.IdInvoice).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                    + (invoice.InvoiceType == InvoiceTypeAvoir
                        ? _db.InvoiceCreditApplications.Where(application => application.IsActive && application.AvoirInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)
                            + _db.AvoirRefundSettlements.Where(refund => refund.IsActive && refund.AvoirInvoiceId == invoice.IdInvoice).Sum(refund => refund.Amount)
                        : _db.InvoiceCreditApplications.Where(application => application.IsActive && application.TargetInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)),
                OutstandingAmount = Math.Max(0m, (invoice.GrossPremium ?? 0m)
                    - _db.ReceiptAllocations.Where(allocation => allocation.IsActive && allocation.IdInvoice == invoice.IdInvoice).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                    - (invoice.InvoiceType == InvoiceTypeAvoir
                        ? _db.InvoiceCreditApplications.Where(application => application.IsActive && application.AvoirInvoiceId == invoice.IdInvoice).Sum(application => application.Amount)
                            + _db.AvoirRefundSettlements.Where(refund => refund.IsActive && refund.AvoirInvoiceId == invoice.IdInvoice).Sum(refund => refund.Amount)
                        : _db.InvoiceCreditApplications.Where(application => application.IsActive && application.TargetInvoiceId == invoice.IdInvoice).Sum(application => application.Amount))),
                CurrencyCode = invoice.IdCurrencyNavigation == null ? string.Empty : invoice.IdCurrencyNavigation.CurrencyCode
            })
            .ToListAsync(cancellationToken);
        return ServiceResult<IReadOnlyList<EligibleInvoiceDto>>.Success(items);
    }


    public async Task<ServiceResult<BankReceiptResponseDto>> AllocateReceiptAsync(int receiptId, ReceiptAllocationRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<BankReceiptResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<BankReceiptResponseDto>();
        if (request.AllocatedAmount <= 0) return Failure<BankReceiptResponseDto>("Allocated amount must be positive.");
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
        await LockOutgoingWorkflowAsync(cancellationToken);
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
        var allocated = receipt.ReceiptAllocations.Where(a => a.IsActive).Sum(x => x.AllocatedAmount ?? 0m);
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
        await GeneratePanelRemittancesAsync(receipt, user, cancellationToken);
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
        catch (OutgoingRemittanceValidationException exception)
        {
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return Failure<BankReceiptResponseDto>(exception.Message, status: StatusCodes.Status409Conflict);
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
            await LockOutgoingWorkflowAsync(cancellationToken);
            var receipt = await _db.BankReceipts
                .Include(item => item.ReceiptAllocations)
                .FirstOrDefaultAsync(item => item.IdBankReceipt == receiptId, cancellationToken);
            if (receipt is null) return NotFound<ReceiptBatchAllocationResponseDto>("Bank receipt was not found.");

            var existingAmount = receipt.ReceiptAllocations.Where(a => a.IsActive).Sum(item => item.AllocatedAmount ?? 0m);
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

            }

            var finalAmount = existingAmount + requestedAmount;
            await GeneratePanelRemittancesAsync(receipt, user, cancellationToken);
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
        catch (OutgoingRemittanceValidationException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return Failure<ReceiptBatchAllocationResponseDto>(exception.Message, status: StatusCodes.Status409Conflict);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ServiceResult<IReadOnlyList<RemittanceResponseDto>>> ListRemittancesAsync(CancellationToken cancellationToken)
    {
        if (!CanFinanceOrBack()) return Forbidden<IReadOnlyList<RemittanceResponseDto>>();
        var list = await RemittanceDetails().Where(x => x.IsActive && x.IdPanelNavigation != null
            && x.IdPanelNavigation.Role != null && x.IdPanelNavigation.Role.Trim().ToUpper() == "REINSURER"
            && x.IdPanelNavigation.IdCounterparty == x.IdCounterparty).ToListAsync(cancellationToken);
        return ServiceResult<IReadOnlyList<RemittanceResponseDto>>.Success(list.Select(MapOutgoingRemittance).ToList());
    }

    public async Task<ServiceResult<RemittanceResponseDto>> ConfirmRemittanceAsync(int remittanceRef, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<RemittanceResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<RemittanceResponseDto>();
        var item = await _db.RemittanceRefs.Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation).FirstOrDefaultAsync(x => x.RemittanceRef1 == remittanceRef, cancellationToken);
        if (item is null) return NotFound<RemittanceResponseDto>("Remittance was not found.");
        if (string.Equals(item.SourceType?.Trim(), "AVOIR", StringComparison.OrdinalIgnoreCase)
            && !await _db.OutgoingClosingLines.AnyAsync(line => line.IsActive && line.RemittanceId == remittanceRef && line.Closing.IsActive, cancellationToken))
            return Failure<RemittanceResponseDto>("Avoir remittances must be netted with eligible Premium remittances for the same reinsurer and currency before confirmation.", status: StatusCodes.Status409Conflict);
        var isAvoirRefund = string.Equals(item.SourceType, "AVOIR_REFUND", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.BeneficiaryType, "CEDENT", StringComparison.OrdinalIgnoreCase);
        if (isAvoirRefund && (string.Equals(item.Status, "Draft", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Status, "ReadyForDisbursement", StringComparison.OrdinalIgnoreCase)))
        {
            var refundConfirmation = await ConfirmRemittanceForDisbursementAsync(remittanceRef, cancellationToken);
            if (!refundConfirmation.Succeeded)
                return ServiceResult<RemittanceResponseDto>.Failure(refundConfirmation.Error?.Message ?? "The Avoir refund could not be confirmed.", refundConfirmation.Error?.Errors, refundConfirmation.StatusCode);
            item.Status = refundConfirmation.Value!.Status;
            item.ConfirmedAt = refundConfirmation.Value.ConfirmationDate;
            item.ConfirmedBy = user;
            return ServiceResult<RemittanceResponseDto>.Success(ToRemittance(item));
        }
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

    public Task<ServiceResult<RemittanceConfirmationResponseDto>> ConfirmRemittanceForDisbursementAsync(int remittanceRef, CancellationToken cancellationToken) =>
        ConfirmRemittanceForDisbursementCoreAsync(remittanceRef, cancellationToken, null);

    private async Task<ServiceResult<RemittanceConfirmationResponseDto>> ConfirmRemittanceForDisbursementCoreAsync(
        int remittanceRef, CancellationToken cancellationToken, IDbContextTransaction? sharedTransaction)
    {
        if (!CanBack()) return Forbidden<RemittanceConfirmationResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<RemittanceConfirmationResponseDto>();

        await using var ownedTransaction = sharedTransaction is null
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        var transaction = sharedTransaction ?? ownedTransaction!;

        var item = await _db.RemittanceRefs
            .Include(x => x.IdCounterpartyNavigation)
            .Include(x => x.IdCurrencyNavigation)
            .Include(x => x.IdPanelNavigation)
            .Include(x => x.SourceInvoice).ThenInclude(x => x!.IdContractNavigation)
            .FirstOrDefaultAsync(x => x.RemittanceRef1 == remittanceRef, cancellationToken);
        if (item is null) return NotFound<RemittanceConfirmationResponseDto>("Remittance was not found.");

        if (string.Equals(item.SourceType?.Trim(), "AVOIR", StringComparison.OrdinalIgnoreCase)
            && !await _db.OutgoingClosingLines.AnyAsync(line => line.IsActive && line.RemittanceId == remittanceRef && line.Closing.IsActive, cancellationToken))
            return Failure<RemittanceConfirmationResponseDto>("Avoir remittances must be netted with eligible Premium remittances for the same reinsurer and currency before confirmation.", status: StatusCodes.Status409Conflict);

        if (string.Equals(item.Status, "AwaitingFinanceDisbursement", StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _db.Notifications.AsNoTracking()
                .Where(x => x.EntityType == "REMITTANCE_REF" && x.EntityId == remittanceRef && x.NotificationType == "DISBURSEMENT_REQUIRED")
                .Select(x => (int?)x.IdNotification)
                .FirstOrDefaultAsync(cancellationToken);
            if (sharedTransaction is null) await transaction.CommitAsync(cancellationToken);
            return ServiceResult<RemittanceConfirmationResponseDto>.Success(new RemittanceConfirmationResponseDto
            {
                RemittanceId = remittanceRef,
                Status = "AwaitingFinanceDisbursement",
                FinanceNotificationCreated = existing.HasValue,
                NotificationId = existing,
                ConfirmationDate = item.ConfirmedAt ?? item.UpdatedAt ?? item.CreatedAt
            });
        }

        var isCedentRefund = string.Equals(item.SourceType, "AVOIR_REFUND", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.BeneficiaryType, "CEDENT", StringComparison.OrdinalIgnoreCase);
        var canConfirm = string.Equals(item.Status?.Trim(), "ReadyForDisbursement", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Status?.Trim(), "Draft", StringComparison.OrdinalIgnoreCase);
        if (!canConfirm)
            return Failure<RemittanceConfirmationResponseDto>("Only eligible remittances can be confirmed for payment.", status: StatusCodes.Status409Conflict);
        Contract? remittanceContract = null;
        if (isCedentRefund)
        {
            var refund = await _db.AvoirRefundSettlements.AsNoTracking()
                .Include(x => x.AvoirInvoice).ThenInclude(x => x.IdContractNavigation)
                .SingleOrDefaultAsync(x => x.IsActive && x.RemittanceId == remittanceRef, cancellationToken);
            if (item.IdPanel.HasValue || item.SourceInvoice is null || !InvoiceDocumentAccounting.IsAvoir(item.SourceInvoice.InvoiceType)
                || refund is null || refund.AvoirInvoiceId != item.SourceInvoiceId || refund.Amount != item.NetPayableAmount)
                return Failure<RemittanceConfirmationResponseDto>("The cedent refund remittance does not have a valid linked Avoir settlement.");
            remittanceContract = refund.AvoirInvoice.IdContractNavigation;
            var validCedent = await _db.Counterparties.AsNoTracking().AnyAsync(counterparty =>
                counterparty.Id == item.IdCounterparty && counterparty.IsActive
                && counterparty.TypeAssignments.Any(type => type.Type.Trim().ToLower() == CounterpartyTypes.Cedent.ToLower()), cancellationToken);
            if (!validCedent || remittanceContract.IdCounterparty != item.IdCounterparty
                || refund.AvoirInvoice.IdCurrency != item.IdCurrency)
                return Failure<RemittanceConfirmationResponseDto>("The refund beneficiary must be the active cedent on the Avoir contract in the same currency.");
        }
        else
        {
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
            remittanceContract = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.IdContract == item.IdPanelNavigation.IdContract, cancellationToken);
        }
        if (item.IdCurrencyNavigation is null)
            return Failure<RemittanceConfirmationResponseDto>("The remittance currency is required.");
        if (!item.NetPayableAmount.HasValue || item.NetPayableAmount.Value <= 0m)
            return Failure<RemittanceConfirmationResponseDto>("The remittance net payable amount must be greater than zero.");

        var now = DateTime.UtcNow;
        item.Status = "AwaitingFinanceDisbursement";
        item.ConfirmedAt = now;
        item.ConfirmedBy = user;
        item.UpdatedAt = now;
        item.UpdatedBy = user;

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
                Title = isCedentRefund ? "Cedent Avoir refund requires disbursement" : "Remittance requires disbursement",
                Message = $"Remittance {remittanceRef} for {item.IdCounterpartyNavigation.Name} requires payment. Contract {remittanceContract?.Name ?? "unknown"}, amount {item.NetPayableAmount:0.####} {item.IdCurrencyNavigation.CurrencyCode}.",
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
        if (sharedTransaction is null) await transaction.CommitAsync(cancellationToken);
        return ServiceResult<RemittanceConfirmationResponseDto>.Success(new RemittanceConfirmationResponseDto
        {
            RemittanceId = remittanceRef,
            Status = "AwaitingFinanceDisbursement",
            FinanceNotificationCreated = createdNotifications.Count > 0,
            NotificationId = createdNotifications.FirstOrDefault()?.IdNotification,
            ConfirmationDate = now
        });
    }

    public async Task<ServiceResult<RemittanceBatchConfirmationResponseDto>> ConfirmRemittancesForDisbursementAsync(
        RemittanceBatchConfirmationRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<RemittanceBatchConfirmationResponseDto>();
        if (request is null || request.RemittanceIds is not { Count: > 0 })
            return ServiceResult<RemittanceBatchConfirmationResponseDto>.Failure("Select at least one remittance to confirm.");
        if (request.RemittanceIds.Any(id => id <= 0) || request.RemittanceIds.Distinct().Count() != request.RemittanceIds.Count)
            return ServiceResult<RemittanceBatchConfirmationResponseDto>.Failure("Remittance IDs must be positive and unique.");

        var requestedIds = request.RemittanceIds.ToArray();
        var traceId = _http.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");
        _logger.LogInformation("Confirm remittance batch started. TraceId={TraceId} RemittanceIds={RemittanceIds} UserId={UserId}",
            traceId, requestedIds, CurrentUser());
        var sourceTypes = await _db.RemittanceRefs.AsNoTracking()
            .Where(row => requestedIds.Contains(row.RemittanceRef1))
            .Select(row => new { row.RemittanceRef1, row.SourceType })
            .ToListAsync(cancellationToken);
        if (sourceTypes.Count == requestedIds.Length
            && sourceTypes.All(row => string.Equals(row.SourceType?.Trim(), "Premium", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.SourceType?.Trim(), "AVOIR", StringComparison.OrdinalIgnoreCase)))
        {
            return await ConfirmReinsurerRemittanceBatchAsync(requestedIds, traceId, cancellationToken);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var remittances = await _db.RemittanceRefs.AsNoTracking()
            .Where(x => request.RemittanceIds.Contains(x.RemittanceRef1))
            .Select(x => new
            {
                x.RemittanceRef1,
                x.Status,
                x.SourceType,
                x.IdCounterparty,
                x.IdCurrency,
                x.IdPanel,
                x.SourceInvoiceId,
                x.NetPayableAmount
            })
            .ToListAsync(cancellationToken);
        if (remittances.Count != request.RemittanceIds.Count)
            return ServiceResult<RemittanceBatchConfirmationResponseDto>.Failure("One or more selected remittances were not found.", statusCode: StatusCodes.Status404NotFound);

        var incompatible = remittances.Any(x =>
            !string.Equals(x.Status?.Trim(), "Draft", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(x.Status?.Trim(), "AwaitingFinanceDisbursement", StringComparison.OrdinalIgnoreCase));
        if (incompatible)
            return ServiceResult<RemittanceBatchConfirmationResponseDto>.Failure("Only Draft remittances can be confirmed for Finance disbursement.", statusCode: StatusCodes.Status409Conflict);

        var first = remittances[0];
        var allCompatible = remittances.All(x => x.IdCounterparty == first.IdCounterparty && x.IdCurrency == first.IdCurrency);
        if (!allCompatible)
            return ServiceResult<RemittanceBatchConfirmationResponseDto>.Failure("Select remittances for the same beneficiary and currency.");

        decimal signedPremiumTotal = 0m;
        decimal signedAvoirTotal = 0m;
        foreach (var item in remittances)
        {
            var amount = Math.Abs(item.NetPayableAmount ?? 0m);
            if (amount <= 0m)
                return ServiceResult<RemittanceBatchConfirmationResponseDto>.Failure("Selected remittances must have a positive payable amount.");
            var signedAmount = SignedRemittanceAmount(item.SourceType, item.NetPayableAmount);
            if (IsAvoirSource(item.SourceType)
                || string.Equals(item.SourceType?.Trim(), "AVOIR_REFUND", StringComparison.OrdinalIgnoreCase))
            {
                signedAmount = -Math.Abs(item.NetPayableAmount ?? 0m);
                signedAvoirTotal += signedAmount;
            }
            else signedPremiumTotal += signedAmount;
        }
        var netBalance = signedPremiumTotal + signedAvoirTotal;
        var amountToDisburse = Math.Max(netBalance, 0m);
        var settlementMode = netBalance > 0m ? "PAYABLE" : netBalance < 0m ? "CREDIT" : "NETTED";

        decimal? remainingAvoirCredit = null;
        var avoirIds = remittances
            .Where(x => x.SourceInvoiceId.HasValue && (string.Equals(x.SourceType?.Trim(), "AVOIR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.SourceType?.Trim(), "AVOIR_REFUND", StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.SourceInvoiceId!.Value)
            .Distinct()
            .ToArray();
        if (avoirIds.Length > 0)
        {
            var avoirs = await _db.Invoices.AsNoTracking()
                .Where(x => avoirIds.Contains(x.IdInvoice) && x.InvoiceType.Trim().ToUpper() == "AVOIR" && x.Status.Trim().ToUpper() != "CANCELLED")
                .Select(x => new { x.IdInvoice, x.GrossPremium })
                .ToListAsync(cancellationToken);
            var applications = await _db.InvoiceCreditApplications.AsNoTracking()
                .Where(x => x.IsActive && avoirIds.Contains(x.AvoirInvoiceId))
                .GroupBy(x => x.AvoirInvoiceId)
                .Select(group => new { Id = group.Key, Amount = group.Sum(x => x.Amount) })
                .ToDictionaryAsync(x => x.Id, x => x.Amount, cancellationToken);
            var refunds = await _db.AvoirRefundSettlements.AsNoTracking()
                .Where(x => x.IsActive && avoirIds.Contains(x.AvoirInvoiceId))
                .GroupBy(x => x.AvoirInvoiceId)
                .Select(group => new { Id = group.Key, Amount = group.Sum(x => x.Amount) })
                .ToDictionaryAsync(x => x.Id, x => x.Amount, cancellationToken);
            remainingAvoirCredit = avoirs.Sum(avoir => Math.Max(0m,
                (avoir.GrossPremium ?? 0m)
                - applications.GetValueOrDefault(avoir.IdInvoice)
                - refunds.GetValueOrDefault(avoir.IdInvoice)));
        }

        var results = new List<RemittanceConfirmationResponseDto>(request.RemittanceIds.Count);
        foreach (var id in request.RemittanceIds)
        {
            var result = await ConfirmRemittanceForDisbursementCoreAsync(id, cancellationToken, transaction);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ServiceResult<RemittanceBatchConfirmationResponseDto>.Failure(
                    result.Error?.Message ?? "A selected remittance could not be confirmed.",
                    result.Error?.Errors,
                    result.StatusCode,
                    result.Error?.Code);
            }
            results.Add(result.Value!);
        }

        await transaction.CommitAsync(cancellationToken);
        return ServiceResult<RemittanceBatchConfirmationResponseDto>.Success(new RemittanceBatchConfirmationResponseDto
        {
            Items = results,
            SignedPremiumTotal = signedPremiumTotal,
            SignedAvoirTotal = signedAvoirTotal,
            NetBalance = netBalance,
            SettlementMode = settlementMode,
            AmountToDisburse = amountToDisburse,
            RemainingAvoirCredit = remainingAvoirCredit
        });
    }

    private async Task<ServiceResult<RemittanceBatchConfirmationResponseDto>> ConfirmReinsurerRemittanceBatchAsync(
        IReadOnlyCollection<int> remittanceIds, string traceId, CancellationToken cancellationToken)
    {
        static ServiceResult<RemittanceBatchConfirmationResponseDto> Conflict(string code, string message, string trace,
            IReadOnlyCollection<int> ids) =>
            WithTrace(ServiceResult<RemittanceBatchConfirmationResponseDto>.Failure(
                message, ids.Select(id => $"REM-{id}"), StatusCodes.Status409Conflict, code), trace);

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await LockOutgoingWorkflowAsync(cancellationToken);

            var existingClosings = await _db.OutgoingClosings
                .Include(closing => closing.Lines.Where(line => line.IsActive))
                    .ThenInclude(line => line.Remittance)
                .Where(closing => closing.IsActive && closing.Lines.Any(line => line.IsActive && remittanceIds.Contains(line.RemittanceId)))
                .ToListAsync(cancellationToken);
            var existingBatch = existingClosings.FirstOrDefault(closing =>
                closing.Lines.Count == remittanceIds.Count
                && closing.Lines.Select(line => line.RemittanceId).ToHashSet().SetEquals(remittanceIds));
            if (existingBatch is not null)
            {
                var existingItems = existingBatch.Lines.Select(line => new RemittanceConfirmationResponseDto
                {
                    RemittanceId = line.RemittanceId,
                    Status = line.Remittance?.Status ?? existingBatch.Status,
                    FinanceNotificationCreated = true,
                    ConfirmationDate = existingBatch.IssuedAt
                }).ToArray();
                var signedPremium = existingBatch.Lines.Where(line => !IsAvoirSource(line.Remittance?.SourceType)).Sum(line => line.PayableAmount);
                var signedAvoir = existingBatch.Lines.Where(line => IsAvoirSource(line.Remittance?.SourceType)).Sum(line => line.PayableAmount);
                var retryNotificationCreated = await _db.Notifications.AsNoTracking()
                    .AnyAsync(notification => notification.ClosingId == existingBatch.Id
                        && notification.NotificationType == "CLOSING_DISBURSEMENT_REQUIRED", cancellationToken);
                var result = await BuildBatchConfirmationResponseAsync(existingBatch, existingItems, signedPremium,
                    signedAvoir, remittanceIds, retryNotificationCreated, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation("Confirm remittance batch idempotent retry. TraceId={TraceId} BatchId={BatchId} RemittanceIds={RemittanceIds}",
                    traceId, existingBatch.Id, remittanceIds);
                return ServiceResult<RemittanceBatchConfirmationResponseDto>.Success(result);
            }

            var occupiedIds = existingClosings.SelectMany(closing => closing.Lines)
                .Select(line => line.RemittanceId).Distinct().ToArray();
            if (occupiedIds.Length > 0)
                return Conflict("REMITTANCE_ALREADY_BATCHED",
                    $"Selected remittances are already linked to another active batch: {string.Join(", ", occupiedIds.Select(id => $"REM-{id}"))}.",
                    traceId, remittanceIds);

            var lines = await RemittanceDetails().AsTracking()
                .Where(row => remittanceIds.Contains(row.RemittanceRef1))
                .OrderBy(row => row.RemittanceRef1)
                .ToListAsync(cancellationToken);
            if (lines.Count != remittanceIds.Count)
                return WithTrace(ServiceResult<RemittanceBatchConfirmationResponseDto>.Failure(
                    "One or more selected remittances were not found.", statusCode: StatusCodes.Status404NotFound,
                    code: "REMITTANCE_NOT_FOUND"), traceId);

            var references = lines.Select(line => $"REM-{line.RemittanceRef1}").ToArray();
            _logger.LogInformation("Confirm remittance batch rows loaded. TraceId={TraceId} Rows={Rows}", traceId,
                lines.Select(line => new { Id = line.RemittanceRef1, line.SourceType, line.IdCounterparty, line.IdCurrency,
                    line.Status, line.IdDisbursement, line.IdPanel, line.NetPayableAmount }));

            var invalidStatus = lines.FirstOrDefault(line => !line.IsActive
                || !string.Equals(line.Status?.Trim(), "Draft", StringComparison.OrdinalIgnoreCase)
                || line.IdDisbursement.HasValue || line.ClosingIssuedDate.HasValue);
            if (invalidStatus is not null)
                return Conflict("REMITTANCE_NOT_ELIGIBLE",
                    $"{string.Join(", ", references)} must be active Draft remittances not already assigned to a payment batch.",
                    traceId, [invalidStatus.RemittanceRef1]);

            var first = lines[0];
            if (lines.Any(line => line.IdCounterparty != first.IdCounterparty))
                return Conflict("MIXED_REINSURERS", "All selected remittances must belong to the same reinsurer.", traceId, remittanceIds);
            if (lines.Any(line => line.IdCurrency != first.IdCurrency))
                return Conflict("MIXED_CURRENCIES", "All selected remittances must use the same currency.", traceId, remittanceIds);
            var validReinsurer = await _db.Counterparties.AsNoTracking().AnyAsync(counterparty =>
                counterparty.Id == first.IdCounterparty && counterparty.IsActive
                && counterparty.TypeAssignments.Any(type => type.Type.Trim().ToLower() == CounterpartyTypes.Reinsurer.ToLower()),
                cancellationToken);
            if (!validReinsurer)
                return Conflict("INVALID_REINSURER", "The selected beneficiary is not an active reinsurer.", traceId, remittanceIds);

            foreach (var line in lines)
            {
                var panel = line.IdPanelNavigation;
                if (panel is null || panel.IdCounterparty != line.IdCounterparty
                    || !string.Equals(panel.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase)
                    || !(string.Equals(panel.Status?.Trim(), "Confirmed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(panel.Status?.Trim(), "Bound", StringComparison.OrdinalIgnoreCase)))
                    return Conflict("INVALID_REINSURER_PANEL",
                        $"{string.Join(", ", references)} must reference confirmed panels belonging to the selected reinsurer.",
                        traceId, [line.RemittanceRef1]);
                if (line.SourceInvoice is not null && line.SourceInvoice.IdContract != panel.IdContract)
                    return Conflict("SOURCE_CONTRACT_MISMATCH",
                        $"{string.Join(", ", references)} contains a source document unrelated to its reinsurer panel.",
                        traceId, [line.RemittanceRef1]);
                if (line.IdCounterpartyNavigation is not { IsActive: true }
                    || line.IdCurrencyNavigation is not { IsActive: true })
                    return Conflict("INACTIVE_BENEFICIARY_OR_CURRENCY",
                        "The reinsurer and currency must both be active.", traceId, [line.RemittanceRef1]);
                if (line.NetPayableAmount is null || line.NetPayableAmount.Value <= 0m)
                    return Conflict("INVALID_REMITTANCE_AMOUNT",
                        $"{string.Join(", ", references)} must have a positive stored payable magnitude.",
                        traceId, [line.RemittanceRef1]);
            }

            var accountRows = await _db.CounterpartyBankAccounts.AsNoTracking()
                .Where(account => account.IdCounterparty == first.IdCounterparty
                    && account.IdCurrency == first.IdCurrency && account.IsActive)
                .OrderBy(account => account.IdBankAccount)
                .ToListAsync(cancellationToken);
            var accountSelection = BankAccountEligibility.Select(accountRows);
            if (!accountSelection.IsValid)
            {
                var bankName = first.IdCounterpartyNavigation.Name;
                var currencyCode = first.IdCurrencyNavigation.CurrencyCode;
                var code = accountRows.Count switch
                {
                    0 => "ACTIVE_BANK_ACCOUNT_NOT_FOUND",
                    > 1 => "BANK_ACCOUNT_SELECTION_REQUIRED",
                    _ => "BANK_ACCOUNT_PAYMENT_DETAILS_MISSING"
                };
                var message = accountRows.Count switch
                {
                    0 => $"{bankName} has no active {currencyCode} bank account.",
                    > 1 => $"{bankName} has multiple active {currencyCode} bank accounts; an account must be selected explicitly.",
                    _ => $"{bankName}'s active {currencyCode} bank account is missing IBAN or SWIFT/BIC details."
                };
                return Conflict(code, message, traceId, remittanceIds);
            }

            var currency = first.IdCurrencyNavigation;
            var reinsurer = first.IdCounterpartyNavigation;
            var account = accountSelection.Account!;
            var signedPremiumTotal = lines.Where(line => !IsAvoirSource(line.SourceType))
                .Sum(line => Math.Abs(line.NetPayableAmount!.Value));
            var signedAvoirTotal = lines.Where(line => IsAvoirSource(line.SourceType))
                .Sum(line => -Math.Abs(line.NetPayableAmount!.Value));
            var netPayable = signedPremiumTotal + signedAvoirTotal;
            if (netPayable <= 0m)
                return Conflict("NET_AMOUNT_NOT_PAYABLE",
                    "The selected Premium and Avoir remittances do not produce a positive amount payable to the reinsurer.",
                    traceId, remittanceIds);

            var user = CurrentUser();
            if (user is null)
                return WithTrace(ServiceResult<RemittanceBatchConfirmationResponseDto>.Failure(
                    "The authenticated Back Officer user could not be resolved.", statusCode: StatusCodes.Status401Unauthorized,
                    code: "AUTHENTICATED_USER_NOT_FOUND"), traceId);

            var now = DateTime.UtcNow;
            var closing = new OutgoingClosing
            {
                Reference = $"CL-{now:yyyyMMdd}-{Guid.NewGuid():N}",
                IssuedAt = now,
                CreatedBy = user,
                IdCounterparty = first.IdCounterparty,
                IdCurrency = first.IdCurrency,
                TotalAmount = netPayable,
                Status = "AwaitingFinanceDisbursement"
            };
            var precision = PanelRemittanceCalculator.Precision(currency.DecimalPlaces);
            closing.TotalInWords = AmountInWords(netPayable, currency.CurrencyCode, precision);
            foreach (var line in lines)
            {
                var signedAmount = IsAvoirSource(line.SourceType)
                    ? -Math.Abs(line.NetPayableAmount!.Value) : Math.Abs(line.NetPayableAmount!.Value);
                closing.Lines.Add(new OutgoingClosingLine { Remittance = line, PayableAmount = signedAmount });
                line.Status = "AwaitingFinanceDisbursement";
                line.ClosingIssuedDate = DateOnly.FromDateTime(now);
                line.ConfirmedAt = now;
                line.ConfirmedBy = user;
                line.UpdatedAt = now;
                line.UpdatedBy = user;
            }
            closing.PdfContent = [];
            _db.OutgoingClosings.Add(closing);
            _logger.LogInformation("Confirm remittance batch creating closing. TraceId={TraceId} ReinsurerId={ReinsurerId} CurrencyId={CurrencyId} PremiumTotal={PremiumTotal} AvoirTotal={AvoirTotal} Net={Net}",
                traceId, first.IdCounterparty, first.IdCurrency, signedPremiumTotal, Math.Abs(signedAvoirTotal), netPayable);
            await _db.SaveChangesAsync(cancellationToken);
            await NotifyRoleAsync("Finance Officer", "Closing issued - disbursement required",
                $"Closing {closing.Reference} for {reinsurer.Name} is ready for payment ({lines.Count} remittance lines).",
                "OUTGOING_CLOSING", closing.Id, cancellationToken, "CLOSING_DISBURSEMENT_REQUIRED");
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var notificationCreated = await _db.Notifications.AsNoTracking()
                .AnyAsync(notification => notification.ClosingId == closing.Id
                    && notification.NotificationType == "CLOSING_DISBURSEMENT_REQUIRED", cancellationToken);
            var items = lines.Select(line => new RemittanceConfirmationResponseDto
            {
                RemittanceId = line.RemittanceRef1,
                Status = line.Status,
                FinanceNotificationCreated = notificationCreated,
                ConfirmationDate = now
            }).ToArray();
            _logger.LogInformation("Confirm remittance batch committed. TraceId={TraceId} BatchId={BatchId} RemittanceIds={RemittanceIds} NotificationCreated={NotificationCreated}",
                traceId, closing.Id, remittanceIds, notificationCreated);
            return ServiceResult<RemittanceBatchConfirmationResponseDto>.Success(new RemittanceBatchConfirmationResponseDto
            {
                Items = items,
                BatchId = closing.Id,
                BatchReference = closing.Reference,
                ReinsurerId = reinsurer.Id,
                ReinsurerName = reinsurer.Name,
                CurrencyCode = currency.CurrencyCode,
                PremiumTotal = signedPremiumTotal,
                AvoirTotal = Math.Abs(signedAvoirTotal),
                NetPayableAmount = netPayable,
                Status = closing.Status,
                RemittanceReferences = lines.Select(line => $"REM-{line.RemittanceRef1}").ToArray(),
                FinanceNotificationCreated = notificationCreated,
                TraceId = traceId,
                SignedPremiumTotal = signedPremiumTotal,
                SignedAvoirTotal = signedAvoirTotal,
                NetBalance = netPayable,
                SettlementMode = "PAYABLE",
                AmountToDisburse = netPayable
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Confirm remittance batch failed. TraceId={TraceId} RemittanceIds={RemittanceIds} UserId={UserId} InnerException={InnerException}",
                traceId, remittanceIds, CurrentUser(), exception.InnerException?.ToString());
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsAvoirSource(string? sourceType) =>
        string.Equals(sourceType?.Trim(), "AVOIR", StringComparison.OrdinalIgnoreCase);

    private static ServiceResult<T> WithTrace<T>(ServiceResult<T> result, string traceId)
    {
        if (result.Error is not null) result.Error.TraceId = traceId;
        return result;
    }

    private async Task<RemittanceBatchConfirmationResponseDto> BuildBatchConfirmationResponseAsync(
        OutgoingClosing closing, IReadOnlyList<RemittanceConfirmationResponseDto> items,
        decimal signedPremiumTotal, decimal signedAvoirTotal, IReadOnlyCollection<int> remittanceIds,
        bool notificationCreated, CancellationToken cancellationToken)
    {
        var context = await (from row in _db.OutgoingClosings.AsNoTracking()
            join counterparty in _db.Counterparties.AsNoTracking() on row.IdCounterparty equals counterparty.Id
            join currency in _db.Currencies.AsNoTracking() on row.IdCurrency equals currency.IdCurrency
            where row.Id == closing.Id
            select new
            {
                CounterpartyId = row.IdCounterparty,
                ReinsurerName = counterparty.Name,
                CurrencyCode = currency.CurrencyCode
            }).SingleAsync(cancellationToken);
        return new RemittanceBatchConfirmationResponseDto
        {
            Items = items,
            BatchId = closing.Id,
            BatchReference = closing.Reference,
            ReinsurerId = context.CounterpartyId,
            ReinsurerName = context.ReinsurerName,
            CurrencyCode = context.CurrencyCode,
            PremiumTotal = signedPremiumTotal,
            AvoirTotal = Math.Abs(signedAvoirTotal),
            NetPayableAmount = signedPremiumTotal + signedAvoirTotal,
            Status = closing.Status,
            RemittanceReferences = remittanceIds.Select(id => $"REM-{id}").ToArray(),
            FinanceNotificationCreated = notificationCreated,
            SignedPremiumTotal = signedPremiumTotal,
            SignedAvoirTotal = signedAvoirTotal,
            NetBalance = signedPremiumTotal + signedAvoirTotal,
            SettlementMode = signedPremiumTotal + signedAvoirTotal > 0m ? "PAYABLE" : "CREDIT",
            AmountToDisburse = Math.Max(0m, signedPremiumTotal + signedAvoirTotal)
        };
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
                ClosingId = item.ClosingId,
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

    public Task<ServiceResult<IReadOnlyList<FinanceClosingQueueItemDto>>> ListDisbursementQueueAsync(CancellationToken cancellationToken) =>
        ListClosingDisbursementQueueAsync(cancellationToken);

    public async Task<ServiceResult<DisbursementContextDto>> GetDisbursementContextAsync(int remittanceRef, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<DisbursementContextDto>();
        var remittance = await _db.RemittanceRefs.AsNoTracking()
            .Include(x => x.IdCounterpartyNavigation)
            .Include(x => x.IdCurrencyNavigation)
            .Include(x => x.IdPanelNavigation)
            .Include(x => x.SourceInvoice).ThenInclude(x => x!.IdContractNavigation).ThenInclude(x => x.IdCounterpartyNavigation)
            .Include(x => x.AvoirRefundSettlements).ThenInclude(x => x.BankReceipt)
            .FirstOrDefaultAsync(x => x.RemittanceRef1 == remittanceRef, cancellationToken);
        if (remittance is null) return NotFound<DisbursementContextDto>("Remittance was not found.");
        var closingId = await _db.OutgoingClosingLines.AsNoTracking().Where(x => x.RemittanceId == remittanceRef && x.IsActive && x.Closing.IsActive)
            .Select(x => (int?)x.ClosingId).SingleOrDefaultAsync(cancellationToken);

        var panel = remittance.IdPanel.HasValue
            ? await _db.ContractPanels.AsNoTracking().Include(x => x.IdCounterpartyNavigation).FirstOrDefaultAsync(x => x.IdPanel == remittance.IdPanel.Value, cancellationToken)
            : null;
        var contract = panel is null ? remittance.SourceInvoice?.IdContractNavigation : await _db.Contracts.AsNoTracking()
            .Include(x => x.IdCounterpartyNavigation)
            .FirstOrDefaultAsync(x => x.IdContract == panel.IdContract, cancellationToken);
        var invoice = remittance.IdDisbursement.HasValue
            ? await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(x => x.IdDisbursement == remittance.IdDisbursement.Value, cancellationToken)
            : null;
        if (!closingId.HasValue && string.Equals(remittance.Status, "PaymentInProgress", StringComparison.OrdinalIgnoreCase) &&
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
        var isAvoirRefund = string.Equals(remittance.SourceType, "AVOIR_REFUND", StringComparison.OrdinalIgnoreCase)
            && string.Equals(remittance.BeneficiaryType, "CEDENT", StringComparison.OrdinalIgnoreCase);
        if (isAvoirRefund && !validAccount)
            return ServiceResult<DisbursementContextDto>.Failure(
                accountSelection.ValidationMessage ?? "An active bank account for the cedent in the refund currency is required.",
                statusCode: StatusCodes.Status409Conflict,
                code: "ACTIVE_BENEFICIARY_BANK_ACCOUNT_REQUIRED");
        var refundSettlement = isAvoirRefund
            ? remittance.AvoirRefundSettlements.Where(x => x.IsActive).OrderBy(x => x.CreatedAt).FirstOrDefault()
            : null;
        var encaissement = refundSettlement?.BankReceipt;
        if (isAvoirRefund && (remittance.SourceInvoice is null
            || !InvoiceDocumentAccounting.IsAvoir(remittance.SourceInvoice.InvoiceType)
            || refundSettlement is null || encaissement is null
            || refundSettlement.AvoirInvoiceId != remittance.SourceInvoiceId
            || refundSettlement.Amount != remittance.NetPayableAmount
            || remittance.SourceInvoice.IdContractNavigation.IdCounterparty != remittance.IdCounterparty
            || remittance.SourceInvoice.IdCurrency != remittance.IdCurrency))
            return ServiceResult<DisbursementContextDto>.Failure(
                "The Avoir refund is missing its source Credit Note or linked encaissement settlement.",
                statusCode: StatusCodes.Status409Conflict,
                code: "AVOIR_REFUND_SOURCE_INVALID");
        var status = remittance.Status?.Trim() ?? string.Empty;
        var canStart = !closingId.HasValue && string.Equals(status, "AwaitingFinanceDisbursement", StringComparison.OrdinalIgnoreCase) && validAccount && remittance.NetPayableAmount > 0m;
        var canConfirm = !closingId.HasValue && string.Equals(status, "PaymentInProgress", StringComparison.OrdinalIgnoreCase) && invoice is not null;
        var selectedBankAccount = account is null ? null : MapBankAccountSnapshot(account);
        return ServiceResult<DisbursementContextDto>.Success(new DisbursementContextDto
        {
            RemittanceId = remittanceRef,
            RemittanceReference = remittanceRef.ToString(CultureInfo.InvariantCulture),
            SourceType = remittance.SourceType,
            AvoirId = isAvoirRefund ? remittance.SourceInvoiceId : null,
            AvoirNumber = isAvoirRefund ? remittance.SourceInvoice?.InvoiceNumber : null,
            CreditNoteNumber = isAvoirRefund ? remittance.SourceInvoice?.InvoiceNumber : null,
            CreditNoteDownloadUrl = isAvoirRefund && remittance.SourceInvoiceId.HasValue
                ? $"/api/finance/remittances/{remittanceRef}/credit-note" : null,
            EncaissementId = encaissement?.IdBankReceipt,
            EncaissementReference = encaissement is null ? null
                : encaissement.BusinessReference ?? encaissement.BankReference ?? $"BR-{encaissement.IdBankReceipt}",
            BeneficiaryId = remittance.IdCounterparty,
            BeneficiaryName = remittance.IdCounterpartyNavigation?.Name,
            GrossAmount = remittance.PremiumSignedLine,
            Currency = remittance.IdCurrencyNavigation?.CurrencyCode,
            ClosingId = closingId,
            ClosingContextUrl = closingId.HasValue ? $"/api/finance/closings/{closingId}/disbursement-context" : null,
            ContractId = contract?.IdContract,
            ContractName = contract?.Name,
            Umr = contract?.Umr,
            CedentName = contract?.IdCounterpartyNavigation?.Name,
            PanelId = panel?.IdPanel,
            ReinsurerId = remittance.IdCounterparty,
            ReinsurerName = remittance.IdCounterpartyNavigation?.Name ?? string.Empty,
            BeneficiaryType = remittance.BeneficiaryType ?? (remittance.IdPanel.HasValue ? "REINSURER" : "CEDENT"),
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
        var batchId = await _db.OutgoingClosingLines.AsNoTracking().Where(x => x.RemittanceId == remittanceRef && x.IsActive && x.Closing.IsActive)
            .Select(x => (int?)x.ClosingId).SingleOrDefaultAsync(cancellationToken);
        if (batchId.HasValue) return ServiceResult<StartDisbursementResponseDto>.Failure($"Remittance belongs to closing {batchId}. Start payment through /api/finance/closings/{batchId}/start-disbursement.", statusCode: 409, code: "CLOSING_BATCH_REQUIRED");
        var user = CurrentUser();
        if (user is null) return Unauthorized<StartDisbursementResponseDto>();
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        await LockOutgoingWorkflowAsync(cancellationToken);
        var remittance = await _db.RemittanceRefs.Include(x => x.IdCurrencyNavigation)
            .Include(x => x.SourceInvoice).ThenInclude(x => x!.IdContractNavigation).ThenInclude(x => x.IdCounterpartyNavigation)
            .FirstOrDefaultAsync(x => x.RemittanceRef1 == remittanceRef, cancellationToken);
        if (remittance is null) return NotFound<StartDisbursementResponseDto>("Remittance was not found.");
        if (string.Equals(remittance.SourceType?.Trim(), "AVOIR", StringComparison.OrdinalIgnoreCase))
            return Failure<StartDisbursementResponseDto>("Avoir remittances must be netted through a reinsurer closing before disbursement.", status: StatusCodes.Status409Conflict);
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
        if (!remittance.NetPayableAmount.HasValue || remittance.NetPayableAmount <= 0m) return Failure<StartDisbursementResponseDto>("The remittance positive net payable amount is required.");
        var isCedentRefund = string.Equals(remittance.SourceType, "AVOIR_REFUND", StringComparison.OrdinalIgnoreCase)
            && string.Equals(remittance.BeneficiaryType, "CEDENT", StringComparison.OrdinalIgnoreCase);
        Contract? paymentContract = null;
        ContractPanel? panel = null;
        if (isCedentRefund)
        {
            var refund = await _db.AvoirRefundSettlements.AsNoTracking()
                .SingleOrDefaultAsync(x => x.IsActive && x.RemittanceId == remittanceRef, cancellationToken);
            paymentContract = remittance.SourceInvoice?.IdContractNavigation;
            var validCedent = await _db.Counterparties.AsNoTracking().AnyAsync(counterparty =>
                counterparty.Id == remittance.IdCounterparty && counterparty.IsActive
                && counterparty.TypeAssignments.Any(type => type.Type.Trim().ToLower() == CounterpartyTypes.Cedent.ToLower()), cancellationToken);
            if (!remittance.SourceInvoiceId.HasValue || remittance.SourceInvoice is null
                || !InvoiceDocumentAccounting.IsAvoir(remittance.SourceInvoice.InvoiceType)
                || refund is null || refund.AvoirInvoiceId != remittance.SourceInvoiceId || refund.Amount != remittance.NetPayableAmount
                || paymentContract is null || paymentContract.IdCounterparty != remittance.IdCounterparty || !validCedent)
                return Failure<StartDisbursementResponseDto>("The cedent refund does not have a valid Avoir, contract and beneficiary relationship.");
        }
        else
        {
            if (!remittance.IdPanel.HasValue) return Failure<StartDisbursementResponseDto>("A reinsurer panel is required for this remittance.");
            panel = await _db.ContractPanels.AsNoTracking()
                .Include(x => x.IdCounterpartyNavigation).ThenInclude(x => x.TypeAssignments)
                .Include(x => x.IdContractNavigation).ThenInclude(x => x.IdCounterpartyNavigation)
                .FirstOrDefaultAsync(x => x.IdPanel == remittance.IdPanel.Value, cancellationToken);
            if (panel is null || panel.IdCounterparty != remittance.IdCounterparty || !string.Equals(panel.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase)) return Failure<StartDisbursementResponseDto>("The remittance does not reference a valid reinsurer panel.");
            if (panel.IdCounterpartyNavigation is null || !panel.IdCounterpartyNavigation.IsActive ||
                !panel.IdCounterpartyNavigation.TypeAssignments.Any(type => type.Type.Trim().Equals(CounterpartyTypes.Reinsurer, StringComparison.OrdinalIgnoreCase)))
                return Failure<StartDisbursementResponseDto>("The remittance reinsurer is not active or is not eligible for payment.");
            paymentContract = panel.IdContractNavigation;
        }
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
            ContractNameSnapshot = paymentContract?.Name,
            UmrSnapshot = paymentContract?.Umr,
            CedentNameSnapshot = paymentContract?.IdCounterpartyNavigation?.Name,
            ReinsurerNameSnapshot = isCedentRefund ? null : panel?.IdCounterpartyNavigation?.Name,
            ReinsurerAddressSnapshot = isCedentRefund ? null : panel?.IdCounterpartyNavigation?.AddressLine,
            ReinsurerCitySnapshot = isCedentRefund ? null : panel?.IdCounterpartyNavigation?.City,
            ReinsurerCountrySnapshot = isCedentRefund ? null : panel?.IdCounterpartyNavigation?.Country,
            ReinsurerTaxIdSnapshot = isCedentRefund ? null : panel?.IdCounterpartyNavigation?.TaxIdentificationNumber,
            PanelReferenceSnapshot = panel?.PremiumReference
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
            IdContract = paymentContract!.IdContract,
            IdCurrency = remittance.IdCurrency,
            IdCounterparty = remittance.IdCounterparty,
            IdPanel = panel?.IdPanel,
            RemittanceRef = remittanceRef,
            IdDisbursement = disbursement.IdDisbursement,
            InvoiceNumber = $"PAY-{now:yyyy}-{disbursement.IdDisbursement:000000}",
            InvoiceType = isCedentRefund ? "CEDENT_AVOIR_REFUND" : "REINSURER_DISBURSEMENT",
            InvoiceDate = DateOnly.FromDateTime(now),
            GrossPremium = disbursement.TotalAmount,
            NetPremium = disbursement.TotalAmount,
            RecoveryStatus = InvoiceRecoveryCalculator.Unsettled,
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
                RecoveryStatus = InvoiceRecoveryCalculator.Unsettled,
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
        var batchId = await _db.OutgoingClosings.AsNoTracking().Where(x => x.IdDisbursement == id && x.IsActive).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (batchId.HasValue) return ServiceResult<ConfirmTransferResponseDto>.Failure($"Disbursement belongs to closing {batchId}. Confirm the entire closing through /api/finance/closings/{batchId}/confirm-transfer.", statusCode: 409, code: "CLOSING_BATCH_REQUIRED");
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
        var batchId = await _db.OutgoingClosings.AsNoTracking().Where(x => x.IdDisbursement == disbursementId && x.IsActive).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (batchId.HasValue) return await GetFinanceClosingPdfAsync(batchId.Value, cancellationToken);
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
        remittance = await _db.RemittanceRefs.AsNoTracking().Include(x => x.SourceInvoice)
            .FirstOrDefaultAsync(x => x.RemittanceRef1 == remittance.RemittanceRef1, cancellationToken) ?? remittance;
        var panel = remittance.IdPanel.HasValue
            ? await _db.ContractPanels.AsNoTracking().FirstOrDefaultAsync(x => x.IdPanel == remittance.IdPanel.Value, cancellationToken)
            : null;
        var contract = panel is not null
            ? await _db.Contracts.AsNoTracking().Include(x => x.IdCounterpartyNavigation).FirstOrDefaultAsync(x => x.IdContract == panel.IdContract, cancellationToken)
            : remittance.SourceInvoiceId.HasValue
                ? await _db.Contracts.AsNoTracking().Include(x => x.IdCounterpartyNavigation).FirstOrDefaultAsync(x => x.IdContract == remittance.SourceInvoice!.IdContract, cancellationToken)
                : null;
        var invoice = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(x => x.IdDisbursement == disbursementId, cancellationToken);
        if (invoice is null) return NotFound<PrintablePaymentDocumentDto>("The invoice for this disbursement has not been generated.", "DISBURSEMENT_INVOICE_NOT_FOUND");

        var lines = new[]
        {
            string.Equals(remittance.SourceType, "AVOIR_REFUND", StringComparison.OrdinalIgnoreCase)
                ? "IRIS - CEDENT AVOIR REFUND PAYMENT DOCUMENT"
                : "IRIS - REINSURER DISBURSEMENT PAYMENT DOCUMENT",
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
            $"Beneficiary type: {remittance.BeneficiaryType ?? (remittance.IdPanel.HasValue ? "REINSURER" : "CEDENT")}",
            $"Beneficiary: {(string.Equals(remittance.BeneficiaryType, "CEDENT", StringComparison.OrdinalIgnoreCase) ? disbursement.IdCounterpartyNavigation?.Name : disbursement.ReinsurerNameSnapshot ?? disbursement.IdCounterpartyNavigation?.Name)}",
            $"Reinsurer: {(string.Equals(remittance.BeneficiaryType, "CEDENT", StringComparison.OrdinalIgnoreCase) ? string.Empty : disbursement.ReinsurerNameSnapshot ?? disbursement.IdCounterpartyNavigation?.Name)}",
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

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetAvoirRefundCreditNoteAsync(int remittanceRef, CancellationToken cancellationToken)
    {
        if (!CanFinance()) return Forbidden<PrintablePaymentDocumentDto>();
        var remittance = await _db.RemittanceRefs.AsNoTracking()
            .Include(x => x.IdCounterpartyNavigation)
            .Include(x => x.IdCurrencyNavigation)
            .Include(x => x.SourceInvoice).ThenInclude(x => x!.IdContractNavigation)
                .ThenInclude(x => x.IdCounterpartyNavigation)
            .Include(x => x.AvoirRefundSettlements).ThenInclude(x => x.BankReceipt)
            .FirstOrDefaultAsync(x => x.RemittanceRef1 == remittanceRef, cancellationToken);
        if (remittance is null) return NotFound<PrintablePaymentDocumentDto>("Remittance was not found.");
        if (!string.Equals(remittance.SourceType, "AVOIR_REFUND", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(remittance.BeneficiaryType, "CEDENT", StringComparison.OrdinalIgnoreCase)
            || remittance.SourceInvoice is null || !InvoiceDocumentAccounting.IsAvoir(remittance.SourceInvoice.InvoiceType))
            return Failure<PrintablePaymentDocumentDto>("The remittance is not linked to a cedent Avoir refund.", status: StatusCodes.Status409Conflict);

        var settlement = remittance.AvoirRefundSettlements.Where(x => x.IsActive).OrderBy(x => x.CreatedAt).FirstOrDefault();
        if (settlement is null || settlement.AvoirInvoiceId != remittance.SourceInvoiceId
            || settlement.Amount != remittance.NetPayableAmount
            || remittance.SourceInvoice.IdContractNavigation.IdCounterparty != remittance.IdCounterparty
            || remittance.SourceInvoice.IdCurrency != remittance.IdCurrency)
            return Failure<PrintablePaymentDocumentDto>("The Avoir refund source or encaissement settlement is inconsistent.", status: StatusCodes.Status409Conflict);
        var receipt = settlement?.BankReceipt;
        var avoir = remittance.SourceInvoice;
        var contract = avoir.IdContractNavigation;
        var currency = remittance.IdCurrencyNavigation?.CurrencyCode ?? avoir.IdCurrencyNavigation?.CurrencyCode ?? string.Empty;
        var lines = new[]
        {
            "AVOIR / NOTE DE CREDIT",
            $"Credit Note number: {avoir.InvoiceNumber}",
            $"Credit Note date: {avoir.InvoiceDate:yyyy-MM-dd}",
            $"Remittance reference: {remittance.RemittanceRef1}",
            $"Status: {remittance.Status}",
            string.Empty,
            "BENEFICIARY",
            $"Cedent: {remittance.IdCounterpartyNavigation?.Name ?? contract?.IdCounterpartyNavigation?.Name}",
            $"Contract: {contract?.Name}",
            $"UMR: {contract?.Umr}",
            $"Encaissement: {(receipt is null ? string.Empty : receipt.BusinessReference ?? receipt.BankReference ?? $"BR-{receipt.IdBankReceipt}")}",
            $"Avoir: {avoir.InvoiceNumber}",
            $"Amount to refund: {remittance.NetPayableAmount ?? 0m:0.####} {currency}",
            string.Empty,
            _invoiceBranding.CompanyName,
            _invoiceBranding.AddressLine1,
            _invoiceBranding.AddressLine2,
            _invoiceBranding.Phone,
            _invoiceBranding.TaxIdentifier,
            _invoiceBranding.CommercialRegister
        };
        return ServiceResult<PrintablePaymentDocumentDto>.Success(new PrintablePaymentDocumentDto
        {
            FileName = $"CreditNote-{avoir.InvoiceNumber}.pdf",
            Content = BuildPdf(lines),
            ContentType = "application/pdf"
        });
    }

    public async Task<ServiceResult<PagedResponseDto<InvoiceResponseDto>>> ListInvoicesAsync(InvoiceQueryDto query, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<PagedResponseDto<InvoiceResponseDto>>();
        if (query.PageNumber < 1 || query.PageSize < 1)
            return Failure<PagedResponseDto<InvoiceResponseDto>>("pageNumber and pageSize must be greater than or equal to 1.", status: StatusCodes.Status400BadRequest);
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

        static string RedactSqlParameters(string sql) => string.Join(Environment.NewLine,
            sql.Split(Environment.NewLine, StringSplitOptions.None)
                .Where(line => !line.TrimStart().StartsWith("DECLARE ", StringComparison.OrdinalIgnoreCase)));

        static string? NormalizePaymentStatus(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal) switch
            {
                "unsettled" or "unpaired" => InvoiceRecoveryCalculator.Unsettled,
                "partiallysettled" or "partial" => InvoiceRecoveryCalculator.PartiallySettled,
                "settled" or "paired" => InvoiceRecoveryCalculator.Settled,
                _ => null
            };
        }

        var q = _db.Invoices.AsNoTracking();
        if (query.ContractId.HasValue) q = q.Where(x => x.IdContract == query.ContractId.Value);
        if (query.CounterpartyId.HasValue) q = q.Where(x => x.IdCounterparty == query.CounterpartyId.Value);
        if (query.CedentId.HasValue) q = q.Where(x => x.IdContractNavigation.IdCounterparty == query.CedentId.Value);
        if (query.PanelId.HasValue) q = q.Where(x => x.IdPanel == query.PanelId.Value || x.PanelAllocations.Any(line => line.IdPanel == query.PanelId.Value));
        var invoiceType = FilterValue(query.InvoiceType);
        if (invoiceType is not null)
        {
            var normalizedInvoiceType = invoiceType.ToUpperInvariant();
            var allowedListTypes = new[] { InvoiceTypeStandard, InvoiceTypeFees, InvoiceTypeAvoir, CedentPremiumInvoiceType, "REINSURER_PAYABLE" };
            if (!allowedListTypes.Contains(normalizedInvoiceType, StringComparer.Ordinal))
                return Failure<PagedResponseDto<InvoiceResponseDto>>("invoiceType must be INVOICE, INVOICE_FEES or AVOIR.", status: StatusCodes.Status400BadRequest);
            q = q.Where(x => x.InvoiceType.ToUpper() == normalizedInvoiceType);
        }
        var requestedPaymentStatus = NormalizePaymentStatus(FilterValue(query.PaymentStatus) ?? FilterValue(query.PairingStatus));
        var suppliedPaymentStatus = FilterValue(query.PaymentStatus) ?? FilterValue(query.PairingStatus);
        if (suppliedPaymentStatus is not null && requestedPaymentStatus is null)
            return Failure<PagedResponseDto<InvoiceResponseDto>>("paymentStatus must be UNSETTLED, PARTIALLY_SETTLED or SETTLED.", status: StatusCodes.Status400BadRequest);
        if (requestedPaymentStatus is not null)
        {
            q = requestedPaymentStatus == InvoiceRecoveryCalculator.Unsettled
                ? q.Where(x => _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m)
                    + (x.InvoiceType == InvoiceTypeAvoir
                        ? _db.InvoiceCreditApplications.Where(a => a.IsActive && a.AvoirInvoiceId == x.IdInvoice).Sum(a => a.Amount)
                        : _db.InvoiceCreditApplications.Where(a => a.IsActive && a.TargetInvoiceId == x.IdInvoice).Sum(a => a.Amount)) <= 0m)
                : requestedPaymentStatus == InvoiceRecoveryCalculator.PartiallySettled
                    ? q.Where(x => _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m)
                        + (x.InvoiceType == InvoiceTypeAvoir
                            ? _db.InvoiceCreditApplications.Where(a => a.IsActive && a.AvoirInvoiceId == x.IdInvoice).Sum(a => a.Amount)
                            : _db.InvoiceCreditApplications.Where(a => a.IsActive && a.TargetInvoiceId == x.IdInvoice).Sum(a => a.Amount)) > 0m
                        && _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m)
                            + (x.InvoiceType == InvoiceTypeAvoir
                                ? _db.InvoiceCreditApplications.Where(a => a.IsActive && a.AvoirInvoiceId == x.IdInvoice).Sum(a => a.Amount)
                                : _db.InvoiceCreditApplications.Where(a => a.IsActive && a.TargetInvoiceId == x.IdInvoice).Sum(a => a.Amount)) < (x.GrossPremium ?? 0m))
                    : q.Where(x => _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m)
                        + (x.InvoiceType == InvoiceTypeAvoir
                            ? _db.InvoiceCreditApplications.Where(a => a.IsActive && a.AvoirInvoiceId == x.IdInvoice).Sum(a => a.Amount)
                            : _db.InvoiceCreditApplications.Where(a => a.IsActive && a.TargetInvoiceId == x.IdInvoice).Sum(a => a.Amount)) >= (x.GrossPremium ?? 0m));
        }
        var currencyFilter = FilterValue(query.CurrencyId);
        if (currencyFilter is not null)
        {
            if (!int.TryParse(currencyFilter, NumberStyles.None, CultureInfo.InvariantCulture, out var currencyId) || currencyId <= 0)
                return Failure<PagedResponseDto<InvoiceResponseDto>>("currencyId must be a positive integer.", status: StatusCodes.Status400BadRequest);
            q = q.Where(x => x.IdCurrency == currencyId);
        }
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
            "paymentstatus" or "pairingstatus" => descending
                ? q.OrderByDescending(x => _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) <= 0m
                    ? 0
                    : _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m) ? 2 : 1)
                : q.OrderBy(x => _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) <= 0m
                    ? 0
                    : _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m) ? 2 : 1),
            "invoicetype" => descending ? q.OrderByDescending(x => x.InvoiceType) : q.OrderBy(x => x.InvoiceType),
            _ => descending ? q.OrderByDescending(x => x.InvoiceDate) : q.OrderBy(x => x.InvoiceDate)
        };
        var page = query.PageNumber;
        var size = Math.Min(query.PageSize, 100);
        var pageQuery = ordered.ThenByDescending(x => x.IdInvoice)
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
                DocumentNumber = x.InvoiceNumber,
                 InvoiceType = x.InvoiceType.ToUpper(),
                Sequence = x.SequenceNumber,
                Amount = x.GrossPremium ?? 0m,
                 SignedAmount = x.InvoiceType.ToUpper() == InvoiceTypeAvoir ? -(x.GrossPremium ?? 0m) : (x.GrossPremium ?? 0m),
                 IsAvoir = x.InvoiceType.ToUpper() == InvoiceTypeAvoir,
                InvoiceDate = x.InvoiceDate,
                InvoiceAmount = x.GrossPremium ?? 0m,
                SettledAmount = _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m),
                RemainingAmount = (x.GrossPremium ?? 0m) > _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m)
                    ? (x.GrossPremium ?? 0m) - _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) : 0m,
                PaymentStatus = _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) <= 0m
                    ? InvoiceRecoveryCalculator.Unsettled
                    : _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m)
                        ? InvoiceRecoveryCalculator.Settled
                        : InvoiceRecoveryCalculator.PartiallySettled,
                GrossAmount = x.GrossPremium ?? 0m,
                NetPremium = x.NetPremium,
                NetAmount = x.NetPremium,
                RecoveryStatus = _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) <= 0m
                    ? InvoiceRecoveryCalculator.Unsettled
                    : _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m)
                        ? InvoiceRecoveryCalculator.Settled
                        : InvoiceRecoveryCalculator.PartiallySettled,
                PairingStatus = _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) <= 0m
                    ? InvoiceRecoveryCalculator.Unsettled
                    : _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) >= (x.GrossPremium ?? 0m)
                        ? InvoiceRecoveryCalculator.Settled
                        : InvoiceRecoveryCalculator.PartiallySettled,
                PairedAmount = _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m),
                OutstandingAmount = (x.GrossPremium ?? 0m) > _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m)
                    ? (x.GrossPremium ?? 0m) - _db.ReceiptAllocations.Where(a => a.IsActive && a.IdInvoice == x.IdInvoice).Sum(a => a.AllocatedAmount ?? 0m) : 0m,
                Label = x.Label ?? string.Empty,
                Status = x.Status,
                ReinsurerName = x.IdCounterpartyNavigation != null ? x.IdCounterpartyNavigation.Name : x.PanelAllocations.Select(line => line.IdCounterpartyNavigation.Name).FirstOrDefault(),
                Allocations = x.PanelAllocations.OrderBy(line => line.IdPanel).Select(line => new InvoicePanelAllocationResponseDto
                {
                    PanelId = line.IdPanel,
                    ReinsurerId = line.IdCounterparty,
                    ReinsurerCode = line.IdCounterpartyNavigation.LegacyCompanyCode,
                    ReinsurerName = line.ReinsurerNameSnapshot,
                    SharePercentage = line.SignedSharePctSnapshot ?? 0m,
                    AllocationRatioPercentage = line.AllocationRatio * 100m,
                    AllocatedGrossPremium = line.GrossPremium,
                    CedentCommissionRate = line.CedentCommissionPct ?? 0m,
                    CedentCommissionPercentage = line.CedentCommissionPct ?? 0m,
                    CedentCommissionAmount = line.CedentCommissionAmount,
                    AllocatedNetPremium = line.NetAmount,
                    IrisBrokerageRate = line.BrokeragePct ?? 0m,
                    IrisBrokeragePercentage = line.BrokeragePct ?? 0m,
                    IrisBrokerageAmount = line.BrokerageAmount,
                    OtherDeductions = line.OtherDeductions,
                    OtherDeductionsAmount = line.OtherDeductions,
                    OtherDeductionsPercentage = line.OtherDeductionsPercentage,
                    NetPayableAmount = line.NetAmount - line.BrokerageAmount - line.OtherDeductions,
                    SignedNetPayableAmount = x.InvoiceType.ToUpper() == InvoiceTypeAvoir || x.InvoiceType.ToUpper() == "CREDIT_NOTE"
                        ? -(line.NetAmount - line.BrokerageAmount - line.OtherDeductions)
                        : line.NetAmount - line.BrokerageAmount - line.OtherDeductions,
                    AllocationAmount = line.NetAmount,
                    SignedAllocationAmount = x.InvoiceType.ToUpper() == InvoiceTypeAvoir || x.InvoiceType.ToUpper() == "CREDIT_NOTE"
                        ? -line.NetAmount : line.NetAmount,
                    Currency = line.CurrencyCodeSnapshot
                }).ToList()
             });
        var requestId = _http.HttpContext?.TraceIdentifier;
        _logger.LogDebug("Invoice list page query generated. RequestId={RequestId} PageNumber={PageNumber} PageSize={PageSize} Sql={Sql}",
            requestId, page, size, RedactSqlParameters(pageQuery.ToQueryString()));
        var items = await pageQuery.ToListAsync(cancellationToken);
        NormalizeInvoiceAllocationPayables(items);

        var listedInvoiceIds = items.Select(item => item.IdInvoice).ToArray();
        if (listedInvoiceIds.Length > 0)
        {
            var creditApplicationQuery = _db.InvoiceCreditApplications.AsNoTracking()
                .Where(application => application.IsActive
                    && (listedInvoiceIds.Contains(application.TargetInvoiceId) || listedInvoiceIds.Contains(application.AvoirInvoiceId)));
            List<InvoiceCreditApplication> creditApplications;
            try
            {
                creditApplications = await creditApplicationQuery.ToListAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,
                    "Invoice list credit-application query failed. RequestId={RequestId} InvoiceCount={InvoiceCount} Sql={Sql}",
                    requestId, listedInvoiceIds.Length, RedactSqlParameters(creditApplicationQuery.ToQueryString()));
                throw;
            }
            foreach (var item in items)
            {
                var appliedCredit = item.IsAvoir
                    ? creditApplications.Where(application => application.AvoirInvoiceId == item.IdInvoice).Sum(application => application.Amount)
                    : creditApplications.Where(application => application.TargetInvoiceId == item.IdInvoice).Sum(application => application.Amount);
                var settled = item.SettledAmount + appliedCredit;
                var recovery = InvoiceRecoveryCalculator.Calculate(item.InvoiceAmount, settled);
                item.SettledAmount = item.PairedAmount = recovery.PairedAmount;
                item.RemainingAmount = item.OutstandingAmount = recovery.OutstandingAmount;
                item.PaymentStatus = item.RecoveryStatus = item.PairingStatus = recovery.Status;
            }
        }

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
            .FirstOrDefaultAsync(item => item.IdContract == contractId, cancellationToken);
        if (contract is null) return NotFound<InvoiceContextDto>("Contract was not found.");
        var status = ContractConstants.NormalizeStatus(contract.Status);
        var expectedGrossPremium = contract.ContractFac?.SumInsured is decimal sumInsured
            && contract.ContractFac.PremiumRate is decimal premiumRate
            ? FacultativeFinancialCalculator.CalculateGrossPremium(
                sumInsured, premiumRate, contract.IdCurrencyNavigation?.DecimalPlaces)
            : (decimal?)null;
        _logger.LogInformation(
            "Invoice context financial source values. RequestId={RequestId} ContractId={ContractId} StoredGrossPremium={StoredGrossPremium} CedentCommissionPercentage={CedentCommissionPercentage} ContractSignedSharePercentage={ContractSignedSharePercentage} SumInsured={SumInsured} PremiumRatePercentage={PremiumRatePercentage} ExpectedGrossPremium={ExpectedGrossPremium}",
            _http.HttpContext?.TraceIdentifier, contractId, contract.GrossPremium,
            contract.CedentCommission, contract.SignedSharePctCedent,
            contract.ContractFac?.SumInsured, contract.ContractFac?.PremiumRate, expectedGrossPremium);
        if (contract.GrossPremium.HasValue && expectedGrossPremium.HasValue
            && contract.GrossPremium.Value != expectedGrossPremium.Value)
        {
            _logger.LogWarning(
                "Persisted Facultative contract gross premium differs from its percentage-based source values. ContractId={ContractId} StoredGrossPremium={StoredGrossPremium} ExpectedGrossPremium={ExpectedGrossPremium}",
                contractId, contract.GrossPremium.Value, expectedGrossPremium.Value);
        }

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

        if (!PremiumDebitNoteFinancials.TryCalculateInvoiceablePremium(
                contract, out var invoiceFinancials, out var invoiceFinancialError))
            return Failure<InvoiceContextDto>("Contract financial data is invalid.", [invoiceFinancialError!]);
        var contractGrossPremium = invoiceFinancials!.ContractGrossPremium;
        var cedentCommissionAmount = invoiceFinancials.CedentCommissionAmount;
        var invoiceableNetPremium = invoiceFinancials.InvoiceableNetPremium;
        var contractDocuments = await _db.Invoices.AsNoTracking()
            .Where(invoice => invoice.IdContract == contractId)
            .Include(invoice => invoice.IdContractNavigation)
                .ThenInclude(contract => contract.IdCounterpartyNavigation)
            .Include(invoice => invoice.IdCurrencyNavigation)
            .Include(invoice => invoice.ReceiptAllocations)
            .Include(invoice => invoice.CreditApplicationsAsTarget)
            .Include(invoice => invoice.CreditApplicationsAsAvoir)
            .AsSplitQuery()
            .OrderBy(invoice => invoice.InvoiceDate)
            .ThenBy(invoice => invoice.SequenceNumber)
            .ThenBy(invoice => invoice.IdInvoice)
            .ToListAsync(cancellationToken);
        var activeContractDocuments = contractDocuments.Where(InvoiceDocumentAccounting.IsActiveDocument).ToList();
        var previousInvoices = activeContractDocuments.Select(ToInvoice).ToList();
        var documentTotals = InvoiceDocumentAccounting.CalculateTotals(contractDocuments);
        var alreadyInvoicedAmount = InvoiceDocumentAccounting.NetInvoiced(contractDocuments);
        var remainingInvoiceableAmount = Math.Max(0m, invoiceableNetPremium - alreadyInvoicedAmount);
        var availableCreditBalance = InvoiceDocumentAccounting.AvailableAvoirBalance(
            contractGrossPremium, contract.CedentCommission, documentTotals.TotalAvoirAmount, invoiceFinancials.CurrencyDecimalPlaces);
        var hasActiveInvoice = activeContractDocuments.Any(invoice => !InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType));

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
                : invoiceableNetPremium <= 0m || remainingInvoiceableAmount <= 0m
                    ? "FULLY_INVOICED"
                    : panels.Count == 0
                        ? "NO_CONFIRMED_REINSURERS"
                        : null;

        _logger.LogInformation(
            "Contract invoice context loaded. RequestId={RequestId} ContractId={ContractId} ActiveInvoiceCount={ActiveInvoiceCount} TotalInvoicedAmount={TotalInvoicedAmount} TotalAvoirAmount={TotalAvoirAmount} AvailableCreditBalance={AvailableCreditBalance}",
            _http.HttpContext?.TraceIdentifier, contractId,
            activeContractDocuments.Count(invoice => !InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType)),
            documentTotals.TotalInvoicedAmount, documentTotals.TotalAvoirAmount, availableCreditBalance);

        return ServiceResult<InvoiceContextDto>.Success(new InvoiceContextDto
        {
            ContractId = contractId,
            ContractGrossPremium = contractGrossPremium,
            CedentCommissionPct = contract.CedentCommission,
            CedentCommissionPercentage = invoiceFinancials.CedentCommissionPercentage,
            CedentCommissionAmount = cedentCommissionAmount,
            ContractSignedSharePercentage = contract.SignedSharePctCedent ?? 0m,
            InvoiceableNetPremium = invoiceableNetPremium,
            CurrencyDecimalPlaces = invoiceFinancials.CurrencyDecimalPlaces,
            CurrencyCode = contract.IdCurrencyNavigation?.CurrencyCode ?? string.Empty,
            Status = status,
            Currency = contract.IdCurrencyNavigation?.CurrencyCode ?? string.Empty,
            PairedAmount = pairedAmount,
            AlreadyInvoicedAmount = alreadyInvoicedAmount,
            AvailableAmount = remainingInvoiceableAmount,
            InvoiceableGrossPremium = contractGrossPremium,
            RemainingInvoiceableAmount = remainingInvoiceableAmount,
            RemainingInvoiceAmount = remainingInvoiceableAmount,
            TotalInvoicedAmount = documentTotals.TotalInvoicedAmount,
            TotalAvoirAmount = documentTotals.TotalAvoirAmount,
            AvailableCreditBalance = availableCreditBalance,
            CanCreateInvoice = blockingReason is null,
            BlockingReason = blockingReason,
            CanCreateAvoir = status == ContractConstants.BoundStatus && hasActiveInvoice && availableCreditBalance > 0m,
            AvoirBlockingReason = status != ContractConstants.BoundStatus
                ? "CONTRACT_NOT_BOUND"
                : !hasActiveInvoice
                    ? "NO_ACTIVE_INVOICES"
                    : availableCreditBalance <= 0m
                        ? "NO_AVAILABLE_CREDIT_BALANCE"
                        : null,
            PreviousInvoices = previousInvoices,
            ReceiptAllocations = allocationRows,
            ReinsurerPanels = panels
        });
    }

    public async Task<ServiceResult<PremiumDebitNotePreviewDto>> PreviewPremiumInvoiceAsync(int contractId, PremiumDebitNoteRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<PremiumDebitNotePreviewDto>();
        var traceId = _http.HttpContext?.TraceIdentifier ?? "unavailable";
        var invoiceType = InvoiceDocumentAccounting.NormalizeType(request.InvoiceType);
        var processingMode = InvoiceDocumentAccounting.NormalizeProcessingMode(request.ProcessingMode);
        var amount = request.EffectiveGrossAmount;
        _logger.LogInformation(
            "Premium invoice preview stage=Started TraceId={TraceId} ContractId={ContractId} UserId={UserId} InvoiceType={InvoiceType} ProcessingMode={ProcessingMode} Amount={Amount} InvoiceDate={InvoiceDate}",
            traceId, contractId, CurrentUser(), invoiceType, processingMode, amount, request.InvoiceDate);
        if (request.Amount.HasValue && request.GrossAmount != 0m && request.Amount.Value != request.GrossAmount)
            return Failure<PremiumDebitNotePreviewDto>("Invoice validation failed.", ["amount: Provide one consistent invoice amount."]);
        if (invoiceType is not (InvoiceTypeStandard or InvoiceTypeFees or InvoiceTypeAvoir))
            return Failure<PremiumDebitNotePreviewDto>("Invoice type is required and must be INVOICE, INVOICE_FEES or AVOIR.", status: StatusCodes.Status400BadRequest);
        if (amount <= 0m) return Failure<PremiumDebitNotePreviewDto>("Invoice validation failed.", ["amount: Invoice amount must be greater than zero."]);
        var isAvoir = invoiceType == InvoiceTypeAvoir;
        if (isAvoir && processingMode is not (InvoiceDocumentAccounting.PairWithEncaissement or InvoiceDocumentAccounting.DirectToRemittances))
            return Failure<PremiumDebitNotePreviewDto>("Avoir validation failed.", ["processingMode: Select PAIR_WITH_ENCAISSEMENT or DIRECT_TO_REMITTANCES."], status: StatusCodes.Status400BadRequest);
        if (!isAvoir && !string.IsNullOrWhiteSpace(processingMode))
            return Failure<PremiumDebitNotePreviewDto>("processingMode is only supported for AVOIR documents.", status: StatusCodes.Status400BadRequest);
        var data = await LoadPremiumInvoiceDataAsync(contractId, cancellationToken);
        _logger.LogInformation(
            "Premium invoice preview stage=ContractDataLoaded TraceId={TraceId} ContractId={ContractId} Found={Found} ConfirmedPanelCount={PanelCount}",
            traceId, contractId, data.Error is null, data.Panels?.Count ?? 0);
        if (data.Error is not null) return ServiceResult<PremiumDebitNotePreviewDto>.Failure(data.Error.Message, data.Error.Errors, data.ErrorStatusCode);
        var remaining = Math.Max(0m, data.InvoiceableNet - data.AlreadyInvoiced);
        var invoiceableGross = data.InvoiceableGross;
        if (isAvoir && !data.HasActiveInvoiceSource)
            return Failure<PremiumDebitNotePreviewDto>("An Avoir requires at least one active Invoice or Invoice Fees document.");
        if (isAvoir && processingMode == InvoiceDocumentAccounting.DirectToRemittances
            && await ValidateDirectAvoirPrerequisitesAsync(data, null, cancellationToken) is { } prerequisiteError)
            return Failure<PremiumDebitNotePreviewDto>(prerequisiteError, status: StatusCodes.Status400BadRequest);
        if (isAvoir && amount > data.AvailableCreditBalance)
            return Failure<PremiumDebitNotePreviewDto>(FormatAvoirBalanceError(
                data.AvailableCreditBalance, data.Contract!.IdCurrencyNavigation?.CurrencyCode, data.CurrencyDecimalPlaces));
        if (!isAvoir && remaining <= 0m) return Failure<PremiumDebitNotePreviewDto>("Invoice validation failed.", ["grossAmount: No invoiceable premium remains for this contract."]);
        if (!isAvoir && amount > remaining) return Failure<PremiumDebitNotePreviewDto>("Invoice validation failed.", ["amount: Invoice amount exceeds the remaining invoiceable net premium."]);
        PremiumDebitNoteAllocationCalculation calculation;
        try
        {
            calculation = PremiumDebitNoteAllocationCalculator.Calculate(data.Contract!, data.Panels!, amount, data.CurrencyDecimalPlaces);
        }
        catch (ArgumentException exception)
        {
            return Failure<PremiumDebitNotePreviewDto>("Invoice allocation validation failed.", [exception.Message]);
        }
        if (isAvoir)
        {
            SetAvoirAllocationSigns(calculation.Allocations);
            var allocationError = ValidateAvoirPanelAllocations(data, calculation.Allocations, amount);
            if (allocationError is not null) return Failure<PremiumDebitNotePreviewDto>(allocationError);
        }
        _logger.LogInformation(
            "Premium invoice preview stage=AllocationValidated TraceId={TraceId} ContractId={ContractId} InvoiceType={InvoiceType} ProcessingMode={ProcessingMode} AllocationCount={AllocationCount}",
            traceId, contractId, invoiceType, processingMode, calculation.Allocations.Count);
        return ServiceResult<PremiumDebitNotePreviewDto>.Success(new PremiumDebitNotePreviewDto
        {
            ContractId = contractId,
            ContractGrossPremium = data.InvoiceableGross,
            InvoiceableGrossPremium = invoiceableGross,
            InvoiceableNetPremium = data.InvoiceableNet,
            CedentCommissionAmount = data.CedentCommissionAmount,
            RemainingInvoiceableAmount = remaining,
            CurrencyCode = data.Contract!.IdCurrencyNavigation?.CurrencyCode ?? string.Empty,
            AlreadyInvoicedAmount = data.AlreadyInvoiced,
            GrossAmount = amount,
            RemainingAmount = isAvoir
                ? Math.Max(0m, data.InvoiceableNet - (data.AlreadyInvoiced - amount))
                : Math.Max(0m, data.InvoiceableNet - data.AlreadyInvoiced - amount),
            InvoiceType = invoiceType,
            ProcessingMode = isAvoir ? processingMode : null,
            SignedAmount = isAvoir ? -amount : amount,
            IsAvoir = isAvoir,
            IsReconciled = calculation.TotalAllocatedNetPremium == amount,
            ReconciliationDifference = calculation.TotalAllocatedNetPremium - amount,
            Allocations = calculation.Allocations
        });
    }

    public async Task<ServiceResult<PremiumDebitNoteResponseDto>> CreatePremiumInvoiceAsync(int contractId, PremiumDebitNoteRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<PremiumDebitNoteResponseDto>();
        var user = CurrentUser();
        if (user is null) return Unauthorized<PremiumDebitNoteResponseDto>();
        var traceId = _http.HttpContext?.TraceIdentifier ?? "unavailable";
        var invoiceType = InvoiceDocumentAccounting.NormalizeType(request.InvoiceType);
        var processingMode = InvoiceDocumentAccounting.NormalizeProcessingMode(request.ProcessingMode);
        var amount = request.EffectiveGrossAmount;
        _logger.LogInformation(
            "Premium invoice creation stage=Started TraceId={TraceId} ContractId={ContractId} UserId={UserId} InvoiceType={InvoiceType} ProcessingMode={ProcessingMode} Amount={Amount} InvoiceDate={InvoiceDate}",
            traceId, contractId, user, invoiceType, processingMode, amount, request.InvoiceDate);
        if (request.Amount.HasValue && request.GrossAmount != 0m && request.Amount.Value != request.GrossAmount)
            return Failure<PremiumDebitNoteResponseDto>("Invoice validation failed.", ["amount: Provide one consistent invoice amount."]);
        if (invoiceType is not (InvoiceTypeStandard or InvoiceTypeFees or InvoiceTypeAvoir))
            return Failure<PremiumDebitNoteResponseDto>("Invoice type is required and must be INVOICE, INVOICE_FEES or AVOIR.", status: StatusCodes.Status400BadRequest);
        if (amount <= 0m) return Failure<PremiumDebitNoteResponseDto>("Invoice validation failed.", ["amount: Invoice amount must be greater than zero."]);
        var isAvoir = invoiceType == InvoiceTypeAvoir;
        if (isAvoir && processingMode is not (InvoiceDocumentAccounting.PairWithEncaissement or InvoiceDocumentAccounting.DirectToRemittances))
            return Failure<PremiumDebitNoteResponseDto>("Avoir validation failed.", ["processingMode: Select PAIR_WITH_ENCAISSEMENT or DIRECT_TO_REMITTANCES."], status: StatusCodes.Status400BadRequest);
        if (!isAvoir && !string.IsNullOrWhiteSpace(processingMode))
            return Failure<PremiumDebitNoteResponseDto>("processingMode is only supported for AVOIR documents.", status: StatusCodes.Status400BadRequest);
        if (isAvoir && processingMode == InvoiceDocumentAccounting.DirectToRemittances && string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Failure<PremiumDebitNoteResponseDto>("A valid idempotencyKey is required for direct Avoir processing.", status: StatusCodes.Status400BadRequest);

        if (isAvoir && processingMode == InvoiceDocumentAccounting.DirectToRemittances
            && !await _db.AspNetUsers.AsNoTracking().AnyAsync(item => item.Id == user, cancellationToken))
            return Failure<PremiumDebitNoteResponseDto>("The authenticated Back Officer does not exist in the user directory.", status: StatusCodes.Status409Conflict);

        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(invoice => invoice.IdempotencyKey == request.IdempotencyKey && invoice.InvoiceType == invoiceType && invoice.IdContract == contractId, cancellationToken);
            if (existing is not null)
            {
                if (existing.GrossPremium != amount
                    || (isAvoir && !string.Equals(existing.ProcessingMode ?? InvoiceDocumentAccounting.PairWithEncaissement,
                        processingMode, StringComparison.OrdinalIgnoreCase)))
                    return Failure<PremiumDebitNoteResponseDto>("The idempotency key was already used with different Avoir data.", status: StatusCodes.Status409Conflict);
                return await MapContractInvoiceAsync(existing.IdInvoice, cancellationToken);
            }
        }

        var data = await LoadPremiumInvoiceDataAsync(contractId, cancellationToken);
        _logger.LogInformation(
            "Premium invoice creation stage=ContractDataLoaded TraceId={TraceId} ContractId={ContractId} Found={Found} ConfirmedPanelCount={PanelCount}",
            traceId, contractId, data.Error is null, data.Panels?.Count ?? 0);
        if (data.Error is not null) return ServiceResult<PremiumDebitNoteResponseDto>.Failure(data.Error.Message, data.Error.Errors, data.ErrorStatusCode);
        var remaining = Math.Max(0m, data.InvoiceableNet - data.AlreadyInvoiced);
        if (isAvoir && !data.HasActiveInvoiceSource)
            return Failure<PremiumDebitNoteResponseDto>("An Avoir requires at least one active Invoice or Invoice Fees document.");
        if (isAvoir && processingMode == InvoiceDocumentAccounting.DirectToRemittances
            && await ValidateDirectAvoirPrerequisitesAsync(data, user, cancellationToken) is { } prerequisiteError)
            return Failure<PremiumDebitNoteResponseDto>(prerequisiteError, status: StatusCodes.Status409Conflict);
        if (isAvoir && amount > data.AvailableCreditBalance)
            return Failure<PremiumDebitNoteResponseDto>(FormatAvoirBalanceError(
                data.AvailableCreditBalance, data.Contract!.IdCurrencyNavigation?.CurrencyCode, data.CurrencyDecimalPlaces));
        if (!isAvoir && remaining <= 0m) return Failure<PremiumDebitNoteResponseDto>("Invoice validation failed.", ["grossAmount: No invoiceable premium remains for this contract."]);
        if (!isAvoir && amount > remaining) return Failure<PremiumDebitNoteResponseDto>("Invoice validation failed.", ["amount: Invoice amount exceeds the remaining invoiceable net premium."]);

        PremiumDebitNoteAllocationCalculation calculation;
        try
        {
            calculation = PremiumDebitNoteAllocationCalculator.Calculate(data.Contract!, data.Panels!, amount, data.CurrencyDecimalPlaces);
        }
        catch (ArgumentException exception)
        {
            return Failure<PremiumDebitNoteResponseDto>("Invoice allocation validation failed.", [exception.Message]);
        }
        if (isAvoir)
        {
            SetAvoirAllocationSigns(calculation.Allocations);
            var allocationError = ValidateAvoirPanelAllocations(data, calculation.Allocations, amount);
            if (allocationError is not null) return Failure<PremiumDebitNoteResponseDto>(allocationError);
        }
        _logger.LogInformation(
            "Premium invoice creation stage=AllocationValidated TraceId={TraceId} ContractId={ContractId} InvoiceType={InvoiceType} ProcessingMode={ProcessingMode} AllocationCount={AllocationCount}",
            traceId, contractId, invoiceType, processingMode, calculation.Allocations.Count);
        var now = DateTime.UtcNow;
        var invoiceDate = request.InvoiceDate == default ? DateOnly.FromDateTime(now) : request.InvoiceDate;
        var sequenceType = isAvoir ? "AV" : "INV";
        var sequence = await AllocateInvoiceNumberSequenceAsync(invoiceDate.Year, sequenceType, cancellationToken);
        if (sequence > 999999)
            return Failure<PremiumDebitNoteResponseDto>($"The invoice-number sequence for {invoiceDate.Year} has reached its six-digit limit.", status: StatusCodes.Status409Conflict);
        var entity = new Invoice
        {
            IdContract = contractId,
            IdCurrency = data.Contract!.IdCurrency,
            InvoiceNumber = isAvoir
                ? CedentInvoiceNumber.FormatAvoir(invoiceDate.Year, sequence)
                : CedentInvoiceNumber.Format(invoiceDate.Year, sequence),
            InvoiceType = invoiceType,
            ProcessingMode = isAvoir ? processingMode : null,
            InvoiceDate = invoiceDate,
            GrossPremium = amount,
            NetPremium = isAvoir ? amount : calculation.TotalNetAmount,
            RecoveryStatus = InvoiceRecoveryCalculator.Unsettled,
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
            _logger.LogInformation(
                "Premium invoice creation stage=InvoiceHeaderSaveStarted TraceId={TraceId} ContractId={ContractId} UserId={UserId} InvoiceType={InvoiceType}",
                traceId, contractId, user, invoiceType);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Premium invoice creation stage=InvoiceHeaderSaved TraceId={TraceId} ContractId={ContractId} InvoiceId={InvoiceId}",
                traceId, contractId, entity.IdInvoice);
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
                    OtherDeductionsPercentage = allocation.OtherDeductionsPercentage,
                    NetAmount = allocation.NetAmount,
                    CurrencyCodeSnapshot = data.Contract.IdCurrencyNavigation.CurrencyCode,
                    CreatedAt = now
                });
            }
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Premium invoice creation stage=AllocationsSaved TraceId={TraceId} ContractId={ContractId} InvoiceId={InvoiceId} AllocationCount={AllocationCount}",
                traceId, contractId, entity.IdInvoice, calculation.Allocations.Count);
            if (isAvoir && processingMode == InvoiceDocumentAccounting.DirectToRemittances)
            {
                _logger.LogInformation(
                    "Premium invoice creation stage=DirectRemittanceGenerationStarted TraceId={TraceId} ContractId={ContractId} InvoiceId={InvoiceId} UserId={UserId}",
                    traceId, contractId, entity.IdInvoice, user);
                await CreateDirectAvoirRemittancesAsync(entity, data.Contract, user, cancellationToken);
                _logger.LogInformation(
                    "Premium invoice creation stage=DirectRemittancesGenerated TraceId={TraceId} ContractId={ContractId} InvoiceId={InvoiceId}",
                    traceId, contractId, entity.IdInvoice);
            }
            _logger.LogInformation(
                "Premium invoice creation stage=TransactionCommitStarted TraceId={TraceId} ContractId={ContractId} InvoiceId={InvoiceId}",
                traceId, contractId, entity.IdInvoice);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation(
                "Premium invoice creation stage=TransactionCommitted TraceId={TraceId} ContractId={ContractId} InvoiceId={InvoiceId}",
                traceId, contractId, entity.IdInvoice);
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
        catch (OutgoingRemittanceValidationException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            _logger.LogWarning(exception,
                "Premium invoice direct-remittance validation failed and transaction rolled back. TraceId={TraceId} ContractId={ContractId} UserId={UserId} InvoiceType={InvoiceType} ProcessingMode={ProcessingMode}",
                traceId, contractId, user, invoiceType, processingMode);
            return Failure<PremiumDebitNoteResponseDto>(exception.Message, status: StatusCodes.Status409Conflict);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            _logger.LogError(exception,
                "Premium invoice creation failed and transaction rolled back. TraceId={TraceId} ContractId={ContractId} UserId={UserId} InvoiceType={InvoiceType} ProcessingMode={ProcessingMode} InnerException={InnerException}",
                traceId, contractId, user, invoiceType, processingMode, exception.InnerException?.ToString());
            throw;
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

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetContractFinancialNoteAsync(int contractId, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<PrintablePaymentDocumentDto>();
        var traceId = _http.HttpContext?.TraceIdentifier;
        try
        {
            var contract = await _db.Contracts.AsNoTracking()
                .Include(item => item.IdCounterpartyNavigation)
                .Include(item => item.IdCurrencyNavigation)
                .Include(item => item.ContractFac)
                .Include(item => item.ContractTreatyProp).ThenInclude(item => item.ContractTreatyPropQuotaShare)
                .Include(item => item.ContractTreatyProp).ThenInclude(item => item.ContractTreatyPropSurplu)
                .Include(item => item.ContractTreatyNonprops)
                .Include(item => item.ContractCoverageSelections)
                    .ThenInclude(selection => selection.IdCoverageItemNavigation)
                .FirstOrDefaultAsync(item => item.IdContract == contractId, cancellationToken);
            if (contract is null)
                return Failure<PrintablePaymentDocumentDto>(
                    "Contract was not found.", status: StatusCodes.Status404NotFound,
                    code: "CONTRACT_NOT_FOUND", traceId: traceId);
            if (!string.Equals(ContractConstants.NormalizeStatus(contract.Status), ContractConstants.BoundStatus, StringComparison.OrdinalIgnoreCase))
                return Failure<PrintablePaymentDocumentDto>(
                    "A financial note is available only for a Bound contract.",
                    status: StatusCodes.Status409Conflict,
                    code: "CONTRACT_NOT_BOUND");
            if (contract.IdCounterpartyNavigation is null)
                return Failure<PrintablePaymentDocumentDto>("The Bound contract has no cedent information.", status: StatusCodes.Status409Conflict, code: "CONTRACT_CEDENT_MISSING");
            if (string.IsNullOrWhiteSpace(contract.IdCurrencyNavigation?.CurrencyCode))
                return Failure<PrintablePaymentDocumentDto>("The Bound contract has no currency information.", status: StatusCodes.Status409Conflict, code: "CONTRACT_CURRENCY_MISSING");
            if (string.IsNullOrWhiteSpace(contract.Umr))
                return Failure<PrintablePaymentDocumentDto>("The Bound contract has no UMR reference.", status: StatusCodes.Status409Conflict, code: "CONTRACT_UMR_MISSING");

            var grossPremium = PremiumDebitNoteFinancials.ResolveContractGrossPremium(contract);
            if (grossPremium <= 0m)
                return Failure<PrintablePaymentDocumentDto>("The Bound contract has no positive gross premium.", status: StatusCodes.Status409Conflict, code: "CONTRACT_GROSS_PREMIUM_MISSING");

            var precision = PanelRemittanceCalculator.Precision(contract.IdCurrencyNavigation?.DecimalPlaces);
            var cedentCommissionAmount = contract.NetPremiumCedent.HasValue
                ? PremiumDebitNoteFinancials.RoundMoney(grossPremium - contract.NetPremiumCedent.Value, precision)
                : PremiumDebitNoteFinancials.CalculateCedentCommissionAmount(grossPremium, contract.CedentCommission, precision);
            if (cedentCommissionAmount < 0m || cedentCommissionAmount > grossPremium)
                return Failure<PrintablePaymentDocumentDto>("The persisted cedent commission is inconsistent with the contract gross premium.", status: StatusCodes.Status409Conflict, code: "CONTRACT_COMMISSION_INVALID");

            var baseContractAmount = PremiumDebitNoteFinancials.RoundMoney(grossPremium - cedentCommissionAmount, precision);
            var reference = $"DN-{contract.Umr.Trim()}";

            var configuredAccounts = _irisBankAccounts.Accounts ?? [];
            var activeAccountOptions = configuredAccounts
                .Select((account, index) => (Account: account, Index: index))
                .Where(item => item.Account.IsActive)
                .OrderBy(item => item.Account.DisplayOrder ?? item.Index)
                .ThenBy(item => item.Index)
                .ToArray();
            var missingFields = _irisBankAccounts.GetFinancialNoteConfigurationErrors();
            var bankAccounts = activeAccountOptions
                .Select(item => item.Account)
                .Select(account => new IrisInvoiceBankAccount(
                    account.AccountHolder,
                    account.BankName,
                    account.Currency,
                    account.Rib,
                    account.Iban,
                    account.SwiftBic,
                    account.Domiciliation))
                .ToList();
            _logger.LogInformation(
                "Contract financial-note account configuration. TraceId={TraceId} ContractId={ContractId} Environment={Environment} Section={Section} ConfiguredCount={ConfiguredCount} MatchingActiveCount={ActiveCount} AccountMetadata={AccountMetadata} Template={TemplateVersion}",
                traceId,
                contractId,
                _hostingEnvironment?.EnvironmentName ?? "Unknown",
                IrisBankAccountsOptions.SectionName,
                configuredAccounts.Count,
                bankAccounts.Count,
                string.Join("; ", activeAccountOptions.Select(item =>
                    $"Currency={item.Account.Currency},DisplayOrder={item.Account.DisplayOrder ?? item.Index}," +
                    $"AccountHolderPresent={!string.IsNullOrWhiteSpace(item.Account.AccountHolder)}," +
                    $"BankNamePresent={!string.IsNullOrWhiteSpace(item.Account.BankName)}," +
                    $"RoutingPresent={!string.IsNullOrWhiteSpace(item.Account.Rib) || !string.IsNullOrWhiteSpace(item.Account.Iban)}," +
                    $"SwiftBicPresent={!string.IsNullOrWhiteSpace(item.Account.SwiftBic)}")),
                DebitNotePdfTemplateVersion);
            if (missingFields.Count > 0)
                return Failure<PrintablePaymentDocumentDto>(
                    "The IRIS bank accounts required for the financial note are not completely configured.",
                    errors: missingFields,
                    status: StatusCodes.Status503ServiceUnavailable,
                    code: "IRIS_BANK_ACCOUNT_NOT_CONFIGURED",
                    traceId: traceId);

            var mainCover = contract.ContractCoverageSelections
                .Where(selection => selection.IdCoverageItemNavigation is not null && selection.IsPrimary)
                .Select(selection => selection.IdCoverageItemNavigation!.CoverageName)
                .FirstOrDefault();
            var pdf = BuildFrenchContractDebitNotePdf(
                contract, contract.IdCounterpartyNavigation, ResolveDebitNoteContractType(contract), mainCover,
                reference, grossPremium, contract.CedentCommission ?? 0m,
                cedentCommissionAmount, baseContractAmount,
                precision, _invoiceBranding, LoadIrisInvoiceLogo(), bankAccounts);
            return ServiceResult<PrintablePaymentDocumentDto>.Success(new PrintablePaymentDocumentDto
            {
                FileName = $"DebitNote_{SanitizeDocumentFileName(reference)}.pdf",
                Content = pdf,
                ContentType = "application/pdf",
                DocumentType = "DEBIT_NOTE",
                DocumentReference = reference
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Contract financial-note PDF generation failed. TraceId={TraceId} Method={Method} Path={Path} ContractId={ContractId} Template={TemplateVersion}",
                traceId,
                _http.HttpContext?.Request.Method,
                _http.HttpContext?.Request.Path.Value,
                contractId,
                DebitNotePdfTemplateVersion);
            return Failure<PrintablePaymentDocumentDto>(
                "The financial note could not be generated.",
                status: StatusCodes.Status500InternalServerError,
                code: "FINANCIAL_NOTE_GENERATION_FAILED",
                traceId: traceId);
        }
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
            .Where(invoice => invoice.IdContract == contractId)
            .Include(invoice => invoice.IdContractNavigation)
                .ThenInclude(contract => contract.IdCounterpartyNavigation)
            .Include(invoice => invoice.IdCurrencyNavigation)
            .Include(invoice => invoice.ReceiptAllocations)
            .Include(invoice => invoice.CreditApplicationsAsTarget)
            .Include(invoice => invoice.CreditApplicationsAsAvoir)
            .Include(invoice => invoice.PanelAllocations).ThenInclude(allocation => allocation.IdCounterpartyNavigation)
            .AsSplitQuery()
            .OrderBy(invoice => invoice.InvoiceDate)
            .ThenBy(invoice => invoice.SequenceNumber)
            .ThenBy(invoice => invoice.CreatedAt)
            .ThenBy(invoice => invoice.IdInvoice)
            .ToListAsync(cancellationToken);
        invoices = invoices.Where(InvoiceDocumentAccounting.IsActiveDocument).ToList();
        var contract = await _db.Contracts.AsNoTracking().Include(item => item.ContractFac).Include(item => item.IdCurrencyNavigation)
            .SingleOrDefaultAsync(item => item.IdContract == contractId, cancellationToken);
        if (contract is null) return NotFound<IReadOnlyList<PremiumDebitNoteResponseDto>>("Contract was not found.");
        var invoiceable = PremiumDebitNoteFinancials.ResolveContractGrossPremium(contract);
        var currencyPrecision = PanelRemittanceCalculator.Precision(contract.IdCurrencyNavigation?.DecimalPlaces);
        var cedentCommissionAmount = PremiumDebitNoteFinancials.CalculateCedentCommissionAmount(invoiceable, contract.CedentCommission, currencyPrecision);
        if (!PremiumDebitNoteFinancials.TryCalculateInvoiceablePremium(
                contract, out var signedInvoiceable, out var invoiceableError))
            return Failure<IReadOnlyList<PremiumDebitNoteResponseDto>>(
                "Contract financial data is invalid.", [invoiceableError!]);
        var invoiceableNet = signedInvoiceable!.InvoiceableNetPremium;
        var runningInvoiced = 0m;
        var runningTotalInvoiced = 0m;
        var runningTotalAvoir = 0m;
        var avoirBase = InvoiceDocumentAccounting.AvoirBase(invoiceable, contract.CedentCommission, currencyPrecision);
        var result = new List<PremiumDebitNoteResponseDto>();
        foreach (var invoice in invoices)
        {
            var previouslyInvoiced = runningInvoiced;
            if (InvoiceDocumentAccounting.IsActiveDocument(invoice))
            {
                runningInvoiced += InvoiceDocumentAccounting.SignedAmount(invoice);
                if (InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType))
                    runningTotalAvoir += invoice.GrossPremium ?? 0m;
                else
                    runningTotalInvoiced += invoice.GrossPremium ?? 0m;
            }
            var item = MapContractInvoice(invoice, invoiceable, invoiceableNet, cedentCommissionAmount,
                previouslyInvoiced, Math.Max(0m, invoiceableNet - runningInvoiced));
            item.TotalInvoicedAmount = runningTotalInvoiced;
            item.TotalAvoirAmount = runningTotalAvoir;
            item.AvailableCreditBalance = Math.Max(0m, avoirBase - runningTotalAvoir);
            item.RemainingInvoiceAmount = Math.Max(0m, invoiceableNet - runningTotalInvoiced + runningTotalAvoir);
            result.Add(item);
        }
        _logger.LogInformation(
            "Contract invoice history loaded. RequestId={RequestId} ContractId={ContractId} InvoiceCount={InvoiceCount} AvoirCount={AvoirCount}",
            _http.HttpContext?.TraceIdentifier, contractId,
            result.Count(item => !item.IsAvoir), result.Count(item => item.IsAvoir));
        return ServiceResult<IReadOnlyList<PremiumDebitNoteResponseDto>>.Success(result);
    }

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetPremiumInvoiceDocumentAsync(int invoiceId, CancellationToken cancellationToken)
    {
        return await GetCedentDocumentPdfAsync(invoiceId, expectAvoir: false, cancellationToken);
    }

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetAvoirCreditNoteDocumentAsync(int avoirId, CancellationToken cancellationToken)
    {
        return await GetCedentDocumentPdfAsync(avoirId, expectAvoir: true, cancellationToken);
    }

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetAvoirFinancialCreditNoteDocumentAsync(
        int avoirId, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<PrintablePaymentDocumentDto>();

        var avoir = await _db.Invoices.AsNoTracking()
            .Include(item => item.IdContractNavigation).ThenInclude(contract => contract.IdCounterpartyNavigation)
            .Include(item => item.IdContractNavigation).ThenInclude(contract => contract.ContractFac)
            .Include(item => item.IdContractNavigation).ThenInclude(contract => contract.ContractTreatyProp)
                .ThenInclude(treaty => treaty!.ContractTreatyPropQuotaShare)
            .Include(item => item.IdContractNavigation).ThenInclude(contract => contract.ContractTreatyProp)
                .ThenInclude(treaty => treaty!.ContractTreatyPropSurplu)
            .Include(item => item.IdContractNavigation).ThenInclude(contract => contract.ContractTreatyNonprops)
            .Include(item => item.IdContractNavigation).ThenInclude(contract => contract.ContractCoverageSelections)
                .ThenInclude(selection => selection.IdCoverageItemNavigation)
            .Include(item => item.IdCurrencyNavigation)
            .FirstOrDefaultAsync(item => item.IdInvoice == avoirId, cancellationToken);
        if (avoir is null)
            return Failure<PrintablePaymentDocumentDto>(
                "Avoir was not found.", status: StatusCodes.Status404NotFound, code: "AVOIR_NOT_FOUND");
        if (!InvoiceDocumentAccounting.IsAvoir(avoir.InvoiceType))
            return Failure<PrintablePaymentDocumentDto>(
                "The selected document is not an Avoir.", status: StatusCodes.Status409Conflict,
                code: "INVOICE_DOCUMENT_TYPE_MISMATCH");
        if (!InvoiceDocumentAccounting.IsActiveDocument(avoir))
            return Failure<PrintablePaymentDocumentDto>(
                "A Credit Note is unavailable for an inactive or cancelled Avoir.",
                status: StatusCodes.Status409Conflict, code: "INVOICE_DOCUMENT_INACTIVE");
        if (avoir.IdContractNavigation is null || avoir.IdContractNavigation.IdCounterpartyNavigation is null)
            return Failure<PrintablePaymentDocumentDto>(
                "The Avoir is missing its contract or cedent information.",
                status: StatusCodes.Status409Conflict, code: "AVOIR_CEDENT_DATA_MISSING");
        if (string.IsNullOrWhiteSpace(avoir.IdCurrencyNavigation?.CurrencyCode))
            return Failure<PrintablePaymentDocumentDto>(
                "The Avoir has no currency information.",
                status: StatusCodes.Status409Conflict, code: "AVOIR_CURRENCY_MISSING");

        var persistedAmount = avoir.GrossPremium ?? avoir.NetPremium ?? 0m;
        var displayAmount = Math.Abs(persistedAmount);
        if (displayAmount <= 0m)
            return Failure<PrintablePaymentDocumentDto>(
                "The Avoir has no persisted positive amount.",
                status: StatusCodes.Status409Conflict, code: "AVOIR_AMOUNT_MISSING");

        var contract = avoir.IdContractNavigation;
        var cedent = contract.IdCounterpartyNavigation;
        var mainCover = contract.ContractCoverageSelections
            .Where(selection => selection.IdCoverageItemNavigation is not null &&
                CoverageConstants.IsMainCoverLevel(selection.IdCoverageItemNavigation.CoverageLevel))
            .OrderByDescending(selection => selection.IsPrimary)
            .ThenBy(selection => selection.IdCoverageItem)
            .Select(selection => selection.IdCoverageItemNavigation!.CoverageName)
            .FirstOrDefault();
        var precision = PanelRemittanceCalculator.Precision(avoir.IdCurrencyNavigation.DecimalPlaces);

        _logger.LogInformation(
            "Generating per-Avoir Credit Note with template avoir-credit-note-v1. AvoirId={AvoirId} ContractId={ContractId} TraceId={TraceId}",
            avoir.IdInvoice, contract.IdContract, _http.HttpContext?.TraceIdentifier);
        var number = SanitizeDocumentFileName(avoir.InvoiceNumber);
        var contractType = ResolveDebitNoteContractType(contract);
        return ServiceResult<PrintablePaymentDocumentDto>.Success(new PrintablePaymentDocumentDto
        {
            FileName = $"CreditNote_{number}.pdf",
            Content = BuildFrenchAvoirCreditNotePdf(
                contract, cedent, contractType, mainCover, avoir.Label, avoir.InvoiceNumber, avoir.InvoiceDate,
                persistedAmount, avoir.IdCurrencyNavigation.CurrencyCode, precision, _invoiceBranding, LoadIrisInvoiceLogo()),
            ContentType = "application/pdf",
            DocumentType = "CREDIT_NOTE",
            DocumentReference = avoir.InvoiceNumber
        });
    }

    private async Task<ServiceResult<PrintablePaymentDocumentDto>> GetCedentDocumentPdfAsync(
        int documentId,
        bool expectAvoir,
        CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<PrintablePaymentDocumentDto>();
        var invoice = await _db.Invoices.AsNoTracking()
            .Include(item => item.IdContractNavigation).ThenInclude(contract => contract.IdCounterpartyNavigation)
            .Include(item => item.IdContractNavigation).ThenInclude(contract => contract.ContractFac)
            .Include(item => item.IdContractNavigation).ThenInclude(contract => contract.ContractCoverageSelections)
                .ThenInclude(selection => selection.IdCoverageItemNavigation)
            .Include(item => item.IdCurrencyNavigation)
            .Include(item => item.PanelAllocations)
            .FirstOrDefaultAsync(item => item.IdInvoice == documentId, cancellationToken);
        if (invoice is null)
            return ServiceResult<PrintablePaymentDocumentDto>.Failure(
                expectAvoir ? "Avoir was not found." : "Invoice was not found.",
                statusCode: StatusCodes.Status404NotFound,
                code: expectAvoir ? "AVOIR_NOT_FOUND" : "INVOICE_NOT_FOUND");

        if (!InvoiceDocumentAccounting.IsContractReceivableType(invoice.InvoiceType)
            && !InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType))
            return Failure<PrintablePaymentDocumentDto>(
                "The selected document is not a cedent invoice.", status: StatusCodes.Status409Conflict,
                code: "INVOICE_DOCUMENT_TYPE_MISMATCH");

        var isAvoir = InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType);
        if (isAvoir != expectAvoir)
            return Failure<PrintablePaymentDocumentDto>(
                expectAvoir ? "The selected document is not an Avoir." : "The selected document is an Avoir, not an invoice.",
                status: StatusCodes.Status409Conflict,
                code: "INVOICE_DOCUMENT_TYPE_MISMATCH");

        if (invoice.IdContractNavigation is null || invoice.IdContractNavigation.IdCounterpartyNavigation is null)
            return Failure<PrintablePaymentDocumentDto>(
                "The invoice is missing its contract or cedent information.",
                status: StatusCodes.Status409Conflict,
                code: "INVOICE_CEDENT_DATA_MISSING");

        if (!InvoiceDocumentAccounting.IsActiveDocument(invoice))
            return Failure<PrintablePaymentDocumentDto>(
                "A debit note is unavailable for an inactive or cancelled document.",
                status: StatusCodes.Status409Conflict,
                code: "INVOICE_DOCUMENT_INACTIVE");

        var contract = invoice.IdContractNavigation;
        var cedent = contract.IdCounterpartyNavigation;
        var mainCover = contract.ContractCoverageSelections
            .Where(selection => selection.IdCoverageItemNavigation is not null &&
                CoverageConstants.IsMainCoverLevel(selection.IdCoverageItemNavigation.CoverageLevel))
            .OrderByDescending(selection => selection.IsPrimary)
            .ThenBy(selection => selection.IdCoverageItem)
            .Select(selection => selection.IdCoverageItemNavigation!.CoverageName)
            .FirstOrDefault();
        var invoiceAmount = Math.Abs(invoice.GrossPremium ?? invoice.NetPremium ?? 0m);
        if (invoiceAmount <= 0m)
            return Failure<PrintablePaymentDocumentDto>(
                "The invoice has no persisted positive amount.", status: StatusCodes.Status409Conflict,
                code: "INVOICE_AMOUNT_MISSING");

        var precision = Math.Clamp(invoice.IdCurrencyNavigation?.DecimalPlaces ?? 2, 0, 4);
        var sign = isAvoir ? -1m : 1m;
        // Individual invoices and Avoirs do not require IRIS collection accounts.
        // The strict three-account configuration is validated only for contract notes.
        var bankAccounts = _irisBankAccounts.Accounts
            .Where(account => account.IsActive)
            .OrderBy(account => account.DisplayOrder ?? int.MaxValue)
            .Select(account => new IrisInvoiceBankAccount(
                account.AccountHolder,
                account.BankName,
                account.Currency,
                account.Rib,
                account.Iban,
                account.SwiftBic,
                account.Domiciliation))
            .Where(account => !string.IsNullOrWhiteSpace(account.AccountHolder)
                || !string.IsNullOrWhiteSpace(account.BankName)
                || !string.IsNullOrWhiteSpace(account.CurrencyCode)
                || !string.IsNullOrWhiteSpace(account.Rib)
                || !string.IsNullOrWhiteSpace(account.Iban)
                || !string.IsNullOrWhiteSpace(account.SwiftBic)
                || !string.IsNullOrWhiteSpace(account.Domiciliation))
            .ToArray();
        _logger.LogInformation(
            "Cedent document PDF optional configured bank accounts loaded. DocumentId={DocumentId}, DocumentType={DocumentType}, ActiveAccountCount={AccountCount}",
            documentId, isAvoir ? "AVOIR" : "INVOICE", bankAccounts.Length);
        _logger.LogInformation("Generating {DocumentType} document with template iris-accounting-v1 for financial document {DocumentId}",
            isAvoir ? "AVOIR" : "INVOICE", documentId);
        var logo = LoadIrisInvoiceLogo();
        var documentReference = invoice.InvoiceNumber;
        var number = SanitizeDocumentFileName(documentReference);
        var signedDocumentAmount = sign * invoiceAmount;
        var documentModel = new IrisInvoiceAvoirPdfModel(
            isAvoir ? IrisAccountingDocumentType.Avoir : IrisAccountingDocumentType.Invoice,
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            cedent.Name,
            cedent.AddressLine,
            cedent.City,
            cedent.Country,
            cedent.ContactName,
            contract.Name,
            contract.Umr,
            invoice.Label,
            mainCover,
            contract.InceptionDate,
            contract.ExpiryDate,
            signedDocumentAmount,
            invoice.IdCurrencyNavigation?.CurrencyCode ?? string.Empty,
            precision,
            _invoiceBranding,
            logo,
            bankAccounts);
        return ServiceResult<PrintablePaymentDocumentDto>.Success(new PrintablePaymentDocumentDto
        {
            FileName = $"{(isAvoir ? "Avoir" : "Facture")}_{number}.pdf",
            Content = BuildIrisInvoiceAvoirPdf(documentModel),
            ContentType = "application/pdf"
        });
    }

    private static string SanitizeDocumentFileName(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Document"
            : new string(value.Trim().Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());

    private byte[]? LoadIrisInvoiceLogo()
    {
        var roots = new[] { _hostingEnvironment?.ContentRootPath, AppContext.BaseDirectory }
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var path = Path.Combine(root!, "Assets", "Branding", "iris-logo.png");
            if (File.Exists(path)) return File.ReadAllBytes(path);
        }

        throw new FileNotFoundException("The required IRIS invoice logo asset was not found in the content root or application output.");
    }

    private async Task<string?> ValidateDirectAvoirPrerequisitesAsync(
        PremiumDebitNoteData data,
        string? authenticatedUserId,
        CancellationToken cancellationToken)
    {
        var contract = data.Contract;
        if (contract is null) return "The selected contract is unavailable.";
        if (contract.ContractFac is null) return "Facultative contract details are missing.";
        if (contract.IdCurrencyNavigation is not { IsActive: true }) return "The contract currency is missing or inactive.";

        var cedent = await _db.Counterparties.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == contract.IdCounterparty && item.IsActive, cancellationToken);
        if (cedent is null) return "The contract cedent is missing or inactive.";

        var panelIds = data.Panels?.Select(panel => panel.IdPanel).ToArray() ?? [];
        if (panelIds.Length == 0) return "At least one confirmed reinsurer panel is required for direct Avoir processing.";

        var counterpartyIds = data.Panels!.Select(panel => panel.IdCounterparty).Distinct().ToArray();
        var activeCounterparties = await _db.Counterparties.AsNoTracking()
            .Where(item => counterpartyIds.Contains(item.Id) && item.IsActive)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var reinsurerAssignments = await _db.CounterpartyTypeAssignments.AsNoTracking()
            .Where(item => counterpartyIds.Contains(item.CounterpartyId))
            .Select(item => new { item.CounterpartyId, item.Type })
            .ToListAsync(cancellationToken);
        var validReinsurers = reinsurerAssignments
            .Where(item => string.Equals(item.Type?.Trim(), CounterpartyTypes.Reinsurer, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.CounterpartyId)
            .ToHashSet();

        foreach (var panel in data.Panels!)
        {
            if (panel.IdCounterpartyNavigation is null || !activeCounterparties.Contains(panel.IdCounterparty))
                return $"Confirmed panel {panel.IdPanel} has no active reinsurer counterparty.";
            if (!validReinsurers.Contains(panel.IdCounterparty))
                return $"Confirmed panel {panel.IdPanel} counterparty is not configured as a reinsurer.";
        }

        if (!string.IsNullOrWhiteSpace(authenticatedUserId)
            && !await _db.AspNetUsers.AsNoTracking().AnyAsync(item => item.Id == authenticatedUserId, cancellationToken))
            return "The authenticated Back Officer does not exist in the user directory.";

        return null;
    }

    private async Task<PremiumDebitNoteData> LoadPremiumInvoiceDataAsync(
        int contractId,
        CancellationToken cancellationToken)
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
        var currencyPrecision = PanelRemittanceCalculator.Precision(contract.IdCurrencyNavigation?.DecimalPlaces);
        decimal cedentCommissionAmount;
        decimal invoiceableNet;
        if (!PremiumDebitNoteFinancials.TryCalculateInvoiceablePremium(
                contract, out var invoiceFinancials, out var invoiceFinancialError))
            return PremiumDebitNoteData.Failed("Contract financial data is invalid.", StatusCodes.Status400BadRequest, [invoiceFinancialError!]);

        cedentCommissionAmount = invoiceFinancials!.CedentCommissionAmount;
        invoiceableNet = invoiceFinancials.InvoiceableNetPremium;
        if (panels.Count == 0 || panels.Sum(item => item.SignedSharePct ?? 0m) <= 0m)
            return PremiumDebitNoteData.Failed("The Bound contract has no confirmed reinsurers with a positive signed share.", StatusCodes.Status400BadRequest);
        var priorDocuments = await _db.Invoices.AsNoTracking()
            .Where(item => item.IdContract == contractId)
            .Include(item => item.PanelAllocations)
            .ToListAsync(cancellationToken);
        var activeDocuments = priorDocuments.Where(InvoiceDocumentAccounting.IsActiveDocument).ToList();
        var totals = InvoiceDocumentAccounting.CalculateTotals(activeDocuments);
        var hasInvoiceSource = activeDocuments.Any(item => !InvoiceDocumentAccounting.IsAvoir(item.InvoiceType));
        var availableCreditBalance = InvoiceDocumentAccounting.AvailableAvoirBalance(
            invoiceableGross, contract.CedentCommission, totals.TotalAvoirAmount, currencyPrecision);
        if (invoiceableGross <= 0m)
            return PremiumDebitNoteData.Failed("The contract gross premium is missing or zero.", StatusCodes.Status400BadRequest);
        var panelCreditBalances = CalculatePanelCreditBalances(contract, panels, activeDocuments);
        return new PremiumDebitNoteData(contract, panels, invoiceableGross, invoiceableNet, cedentCommissionAmount,
            currencyPrecision, InvoiceDocumentAccounting.NetInvoiced(activeDocuments),
            totals.TotalInvoicedAmount, totals.TotalAvoirAmount, availableCreditBalance, panelCreditBalances,
            hasInvoiceSource, null, StatusCodes.Status200OK);
    }

    private static IReadOnlyDictionary<int, decimal> CalculatePanelCreditBalances(
        Contract contract, IReadOnlyList<ContractPanel> confirmedPanels, IReadOnlyList<Invoice> activeDocuments)
    {
        var panelIds = confirmedPanels.Select(panel => panel.IdPanel).ToHashSet();
        var balances = confirmedPanels.ToDictionary(panel => panel.IdPanel, _ => 0m);
        foreach (var document in activeDocuments)
        {
            var sign = InvoiceDocumentAccounting.IsAvoir(document.InvoiceType) ? -1m : 1m;
            if (document.PanelAllocations.Count > 0)
            {
                foreach (var line in document.PanelAllocations.Where(line => panelIds.Contains(line.IdPanel)))
                    balances[line.IdPanel] += sign * (line.GrossPremium - line.CedentCommissionAmount);
                continue;
            }

            var amount = document.GrossPremium ?? 0m;
            if (amount <= 0m) continue;
            foreach (var line in PremiumDebitNoteAllocationCalculator.Calculate(contract, confirmedPanels, amount).Allocations)
                balances[line.PanelId] += sign * line.AllocationAmount;
        }

        return balances.ToDictionary(pair => pair.Key, pair => Math.Max(0m, pair.Value));
    }

    private static void SetAvoirAllocationSigns(IReadOnlyList<PremiumDebitNoteAllocationDto> allocations)
    {
        foreach (var allocation in allocations)
        {
            allocation.SignedAllocationAmount = -allocation.AllocationAmount;
            allocation.SignedNetPayableAmount = -Math.Abs(allocation.NetPayableAmount);
        }
    }

    private static string? ValidateAvoirPanelAllocations(
        PremiumDebitNoteData data, IReadOnlyList<PremiumDebitNoteAllocationDto> allocations, decimal avoirAmount)
    {
        if (allocations.Count == 0)
            return "Avoir allocation preview must contain at least one confirmed reinsurer panel.";
        if (allocations.Any(line => line.AllocationAmount < 0m))
            return "Avoir panel allocation amounts cannot be negative.";
        if (allocations.Sum(line => line.AllocationAmount) != avoirAmount)
            return "Avoir panel allocation amounts must total exactly the Avoir amount.";
        if (allocations.Any(line => line.NetPayableAmount < 0m || line.PanelId <= 0 || line.ReinsurerId <= 0))
            return "Every Avoir allocation must have a valid panel, reinsurer, and non-negative net-payable amount.";

        foreach (var line in allocations)
        {
            var eligiblePanel = data.Panels?.Any(panel => panel.IdPanel == line.PanelId
                && string.Equals(panel.Role?.Trim(), "Reinsurer", StringComparison.OrdinalIgnoreCase)
                && string.Equals(panel.Status?.Trim(), ContractConstants.ConfirmedPanelStatus, StringComparison.OrdinalIgnoreCase)) == true;
            if (!eligiblePanel)
                return "Avoir allocations may only reference confirmed reinsurer panels belonging to the selected contract.";
            if (line.AllocationAmount > data.PanelCreditBalances.GetValueOrDefault(line.PanelId))
                return $"The Avoir allocation for reinsurer {line.ReinsurerName} exceeds its available invoiced allocation.";
        }

        return null;
    }

    private async Task<ServiceResult<PremiumDebitNoteResponseDto>> MapContractInvoiceAsync(int invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices.AsNoTracking()
            .Include(item => item.IdCurrencyNavigation)
            .Include(item => item.ReceiptAllocations)
            .Include(item => item.PanelAllocations)
                .ThenInclude(item => item.IdCounterpartyNavigation)
            .Include(item => item.CreditApplicationsAsTarget)
            .Include(item => item.CreditApplicationsAsAvoir)
            .FirstOrDefaultAsync(item => item.IdInvoice == invoiceId, cancellationToken);
        if (invoice is null || !InvoiceDocumentAccounting.IsContractReceivableType(invoice.InvoiceType))
            return NotFound<PremiumDebitNoteResponseDto>("Invoice was not found.");
        var priorDocuments = await _db.Invoices.AsNoTracking()
            .Where(item => item.IdContract == invoice.IdContract && item.IdInvoice < invoice.IdInvoice)
            .ToListAsync(cancellationToken);
        var already = InvoiceDocumentAccounting.NetInvoiced(priorDocuments);
        var contract = await _db.Contracts.AsNoTracking()
            .Include(item => item.ContractFac)
            .Include(item => item.IdCurrencyNavigation)
            .SingleOrDefaultAsync(item => item.IdContract == invoice.IdContract, cancellationToken);
        if (contract is null) return NotFound<PremiumDebitNoteResponseDto>("Contract was not found.");
        var invoiceable = PremiumDebitNoteFinancials.ResolveContractGrossPremium(contract);
        var currencyPrecision = PanelRemittanceCalculator.Precision(contract.IdCurrencyNavigation?.DecimalPlaces);
        var cedentCommissionAmount = PremiumDebitNoteFinancials.CalculateCedentCommissionAmount(invoiceable, contract.CedentCommission, currencyPrecision);
        if (!PremiumDebitNoteFinancials.TryCalculateInvoiceablePremium(
                contract, out var invoiceFinancials, out var invoiceFinancialError))
            return Failure<PremiumDebitNoteResponseDto>("Contract financial data is invalid.", [invoiceFinancialError!]);
        var invoiceableNet = invoiceFinancials!.InvoiceableNetPremium;
        var contractDocuments = await _db.Invoices.AsNoTracking()
            .Where(item => item.IdContract == invoice.IdContract).ToListAsync(cancellationToken);
        var totals = InvoiceDocumentAccounting.CalculateTotals(contractDocuments);
        var availableCreditBalance = InvoiceDocumentAccounting.AvailableAvoirBalance(
            invoiceable, contract.CedentCommission, totals.TotalAvoirAmount, currencyPrecision);
        var response = MapContractInvoice(invoice, invoiceable, invoiceableNet, cedentCommissionAmount, already,
            Math.Max(0m, invoiceableNet - InvoiceDocumentAccounting.NetInvoiced(contractDocuments)));
        response.ProcessingMode = InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType)
            ? string.Equals(invoice.ProcessingMode, InvoiceDocumentAccounting.DirectToRemittances, StringComparison.OrdinalIgnoreCase)
                ? InvoiceDocumentAccounting.DirectToRemittances
                : InvoiceDocumentAccounting.PairWithEncaissement
            : null;
        response.PairingRequired = InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType)
            && response.ProcessingMode == InvoiceDocumentAccounting.PairWithEncaissement;
        if (response.ProcessingMode == InvoiceDocumentAccounting.DirectToRemittances)
        {
            var directRemittances = await RemittanceDetails()
                .Where(row => row.IsActive && row.SourceInvoiceId == invoice.IdInvoice && row.SourceType == "AVOIR"
                    && row.IdReceiptAllocation == null && row.IdPanel != null)
                .OrderBy(row => row.RemittanceRef1).ToListAsync(cancellationToken);
            response.CreatedRemittances = directRemittances.Select(MapOutgoingRemittance).ToArray();
        }
        response.TotalInvoicedAmount = totals.TotalInvoicedAmount;
        response.TotalAvoirAmount = totals.TotalAvoirAmount;
        response.AvailableCreditBalance = availableCreditBalance;
        response.RemainingInvoiceAmount = Math.Max(0m,
            invoiceableNet - InvoiceDocumentAccounting.NetInvoiced(contractDocuments));
        return ServiceResult<PremiumDebitNoteResponseDto>.Success(response);
    }

    private static PremiumDebitNoteResponseDto MapContractInvoice(Invoice invoice, decimal invoiceableGross, decimal invoiceableNet,
        decimal cedentCommissionAmount, decimal already, decimal remaining)
    {
        var settledAmount = invoice.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m);
        settledAmount += InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType)
            ? invoice.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
            : invoice.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount);
        var paymentStatus = InvoiceRecoveryCalculator.GetInvoicePaymentStatus(invoice.GrossPremium ?? 0m, settledAmount);

        return new()
        {
            IdInvoice = invoice.IdInvoice,
            InvoiceId = invoice.IdInvoice,
            IdContract = invoice.IdContract,
            ContractId = invoice.IdContract,
            InvoiceNumber = invoice.InvoiceNumber,
            DocumentNumber = invoice.InvoiceNumber,
            Sequence = ResolveInvoiceSequence(invoice),
            InvoiceType = InvoiceDocumentAccounting.NormalizeStoredType(invoice.InvoiceType),
            ProcessingMode = InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType)
                ? string.Equals(invoice.ProcessingMode, InvoiceDocumentAccounting.DirectToRemittances, StringComparison.OrdinalIgnoreCase)
                    ? InvoiceDocumentAccounting.DirectToRemittances
                    : InvoiceDocumentAccounting.PairWithEncaissement
                : null,
            Amount = invoice.GrossPremium ?? 0m,
            SignedAmount = InvoiceDocumentAccounting.SignedAmount(invoice),
            IsAvoir = InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType),
            IsReconciled = invoice.PanelAllocations.Sum(item => item.GrossPremium - item.CedentCommissionAmount) == (invoice.GrossPremium ?? 0m),
            ReconciliationDifference = invoice.PanelAllocations.Sum(item => item.GrossPremium - item.CedentCommissionAmount) - (invoice.GrossPremium ?? 0m),
            InvoiceDate = invoice.InvoiceDate,
            ContractGrossPremium = invoiceableGross,
            InvoiceableGrossPremium = invoiceableGross,
            InvoiceableNetPremium = invoiceableNet,
            CedentCommissionAmount = cedentCommissionAmount,
            GrossAmount = invoice.GrossPremium ?? 0m,
            AlreadyInvoicedAmount = already,
            RemainingAmount = remaining,
            TotalNetAmount = invoice.NetPremium ?? 0m,
            Currency = invoice.IdCurrencyNavigation?.CurrencyCode ?? string.Empty,
            CurrencyCode = invoice.IdCurrencyNavigation?.CurrencyCode ?? string.Empty,
            Status = invoice.Status,
            PaymentStatus = paymentStatus,
            RecoveryStatus = paymentStatus,
            Label = invoice.Label ?? string.Empty,
            Allocations = invoice.PanelAllocations.Select(item => new PremiumDebitNoteAllocationDto
            {
                PanelId = item.IdPanel,
                ReinsurerId = item.IdCounterparty,
                ReinsurerCode = item.IdCounterpartyNavigation?.LegacyCompanyCode,
                ReinsurerName = string.IsNullOrWhiteSpace(item.ReinsurerNameSnapshot) ? item.IdCounterpartyNavigation?.Name ?? string.Empty : item.ReinsurerNameSnapshot,
                SignedSharePct = item.SignedSharePctSnapshot,
                SharePercentage = item.SignedSharePctSnapshot ?? 0m,
                SignedSharePercentage = item.SignedSharePctSnapshot ?? 0m,
                AllocationRatio = item.AllocationRatio,
                AllocationRatioPercentage = item.AllocationRatio * 100m,
                GrossPremium = item.GrossPremium,
                AllocatedGrossPremium = item.GrossPremium,
                AllocationAmount = item.GrossPremium - item.CedentCommissionAmount,
                SignedAllocationAmount = InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType)
                    ? -(item.GrossPremium - item.CedentCommissionAmount)
                    : item.GrossPremium - item.CedentCommissionAmount,
                CedentCommissionPct = item.CedentCommissionPct,
                CedentCommissionRate = item.CedentCommissionPct ?? 0m,
                CedentCommissionPercentage = item.CedentCommissionPct ?? 0m,
                CedentCommissionAmount = item.CedentCommissionAmount,
                AllocatedNetPremium = item.NetAmount,
                BrokeragePct = item.BrokeragePct ?? 0m,
                IrisBrokeragePercentage = item.BrokeragePct ?? 0m,
                IrisBrokerageRate = item.BrokeragePct ?? 0m,
                BrokerageAmount = item.BrokerageAmount,
                IrisBrokerageAmount = item.BrokerageAmount,
                OtherDeductions = item.OtherDeductions,
                OtherDeductionsAmount = item.OtherDeductions,
                NetAmount = item.NetAmount,
                NetPayableAmount = PremiumDebitNoteAllocationCalculator.NetPayableFromPersistedAllocation(item),
                SignedNetPayableAmount = InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType)
                    ? -Math.Abs(PremiumDebitNoteAllocationCalculator.NetPayableFromPersistedAllocation(item))
                    : PremiumDebitNoteAllocationCalculator.NetPayableFromPersistedAllocation(item),
                CurrencyCode = item.CurrencyCodeSnapshot,
                Currency = item.CurrencyCodeSnapshot
            }).ToArray(),
            PairingRequired = InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType)
                && !string.Equals(invoice.ProcessingMode, InvoiceDocumentAccounting.DirectToRemittances, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static int? ResolveInvoiceSequence(Invoice invoice)
    {
        var number = invoice.InvoiceNumber;
        if (number is { Length: >= 14 } && (number.StartsWith("INV-", StringComparison.Ordinal) || number.StartsWith("AV-", StringComparison.Ordinal)) &&
            int.TryParse(number.AsSpan(number.Length - 6), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
            return sequence;

        return invoice.SequenceNumber;
    }

    private async Task<int> AllocateInvoiceNumberSequenceAsync(int year, string documentType, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            MERGE dbo.INVOICE_NUMBER_SEQUENCE WITH (HOLDLOCK) AS target
            USING (VALUES (@invoice_year, @document_type)) AS source (invoice_year, document_type)
                ON target.invoice_year = source.invoice_year AND target.document_type = source.document_type
            WHEN MATCHED THEN
                UPDATE SET last_sequence = target.last_sequence + 1
            WHEN NOT MATCHED THEN
                INSERT (invoice_year, document_type, last_sequence) VALUES (source.invoice_year, source.document_type, 1)
            OUTPUT inserted.last_sequence;
            """;
        var yearParameter = command.CreateParameter();
        yearParameter.ParameterName = "@invoice_year";
        yearParameter.DbType = DbType.Int32;
        yearParameter.Value = year;
        command.Parameters.Add(yearParameter);
        var typeParameter = command.CreateParameter();
        typeParameter.ParameterName = "@document_type";
        typeParameter.DbType = DbType.String;
        typeParameter.Size = 10;
        typeParameter.Value = documentType;
        command.Parameters.Add(typeParameter);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static string FormatAvoirBalanceError(decimal balance, string? currencyCode, int currencyDecimalPlaces)
    {
        var precision = PanelRemittanceCalculator.Precision(currencyDecimalPlaces);
        var amount = balance.ToString($"N{precision}", CultureInfo.InvariantCulture);
        return $"The Avoir amount cannot exceed the available credit balance of {amount} {currencyCode ?? string.Empty}.".TrimEnd();
    }

    private sealed record PremiumDebitNoteData(
        Contract? Contract,
        IReadOnlyList<ContractPanel>? Panels,
        decimal InvoiceableGross,
        decimal InvoiceableNet,
        decimal CedentCommissionAmount,
        int CurrencyDecimalPlaces,
        decimal AlreadyInvoiced,
        decimal TotalInvoicedAmount,
        decimal TotalAvoirAmount,
        decimal AvailableCreditBalance,
        IReadOnlyDictionary<int, decimal> PanelCreditBalances,
        bool HasActiveInvoiceSource,
        ApiErrorResponseDto? Error,
        int ErrorStatusCode)
    {
        public static PremiumDebitNoteData Failed(string message, int statusCode, IEnumerable<string>? errors = null) => new(
            null, null, 0m, 0m, 0m, 2, 0m, 0m, 0m, 0m, new Dictionary<int, decimal>(), false,
            new ApiErrorResponseDto { Message = message, Errors = errors ?? [], Code = statusCode == StatusCodes.Status404NotFound ? "NOT_FOUND" : null },
            statusCode);
    }


    public async Task<ServiceResult<InvoiceResponseDto>> CreateInvoiceAsync(InvoiceRequestDto request, CancellationToken cancellationToken)
    {
        if (!CanBack()) return Forbidden<InvoiceResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<InvoiceResponseDto>();
        var invoiceType = InvoiceDocumentAccounting.NormalizeType(request.InvoiceType);
        if (invoiceType is not (InvoiceTypeStandard or InvoiceTypeFees or InvoiceTypeAvoir))
            return Failure<InvoiceResponseDto>("invoiceType is required and must be INVOICE, INVOICE_FEES or AVOIR.", status: StatusCodes.Status400BadRequest);
        if (invoiceType == InvoiceTypeAvoir)
        {
            var result = await CreatePremiumInvoiceAsync(request.ContractId, new PremiumDebitNoteRequestDto
            {
                InvoiceType = invoiceType,
                ProcessingMode = request.ProcessingMode ?? string.Empty,
                GrossAmount = request.Amount ?? request.NetPremium ?? 0m,
                InvoiceDate = request.InvoiceDate,
                Label = request.Label,
                IdempotencyKey = request.IdempotencyKey
            }, cancellationToken);
            if (!result.Succeeded)
                return Failure<InvoiceResponseDto>(result.Error?.Message ?? "The Avoir could not be created.", result.Error?.Errors, result.StatusCode);
            var createdAvoir = await GetInvoiceAsync(result.Value!.InvoiceId, cancellationToken);
            return createdAvoir.Succeeded
                ? ServiceResult<InvoiceResponseDto>.Success(createdAvoir.Value!, StatusCodes.Status201Created)
                : createdAvoir;
        }
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
        var sequence = await AllocateInvoiceNumberSequenceAsync(invoiceDate.Year, "INV", cancellationToken);
        if (sequence > 999999)
            return Failure<InvoiceResponseDto>($"The invoice-number sequence for {invoiceDate.Year} has reached its six-digit limit.", status: StatusCodes.Status409Conflict);
        var entity = new Invoice
        {
            IdContract = request.ContractId,
            IdCurrency = allocation.IdCurrency,
            IdReceiptAllocation = allocation.IdReceiptAllocation,
            IdCounterparty = panel.IdCounterparty,
            IdPanel = panel.IdPanel,
            RemittanceRef = request.RemittanceRef,
            InvoiceNumber = CedentInvoiceNumber.Format(invoiceDate.Year, sequence),
            InvoiceType = invoiceType,
            InvoiceDate = invoiceDate,
            GrossPremium = amount,
            RecoveryStatus = InvoiceRecoveryCalculator.Unsettled,
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
        if (invoice is null) return NotFound<InvoiceResponseDto>("Invoice was not found.");
        NormalizeInvoiceAllocationPayables([invoice]);
        if (InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType))
        {
            invoice.ProcessingMode = string.Equals(invoice.ProcessingMode, InvoiceDocumentAccounting.DirectToRemittances, StringComparison.OrdinalIgnoreCase)
                ? InvoiceDocumentAccounting.DirectToRemittances
                : InvoiceDocumentAccounting.PairWithEncaissement;
            invoice.PairingRequired = invoice.ProcessingMode == InvoiceDocumentAccounting.PairWithEncaissement;
            if (!invoice.PairingRequired)
            {
                var directRows = await RemittanceDetails()
                    .Where(row => row.IsActive && row.SourceInvoiceId == id && row.SourceType == "AVOIR"
                        && row.IdReceiptAllocation == null && row.IdPanel != null)
                    .OrderBy(row => row.RemittanceRef1).ToListAsync(cancellationToken);
                invoice.CreatedRemittances = directRows.Select(MapOutgoingRemittance).ToArray();
            }
        }
        return ServiceResult<InvoiceResponseDto>.Success(invoice);
    }

    public async Task<ServiceResult<FinancialHistoryDto>> GetFinancialHistoryAsync(int contractId, CancellationToken cancellationToken)
    {
        if (!CanFinanceOrBack()) return Forbidden<FinancialHistoryDto>();
        if (!await _db.Contracts.AnyAsync(x => x.IdContract == contractId, cancellationToken)) return NotFound<FinancialHistoryDto>("Contract was not found.");
        var receiptIds = await _db.ReceiptAllocations.Where(x => x.IdPanel.HasValue && _db.ContractPanels.Any(p => p.IdPanel == x.IdPanel && p.IdContract == contractId)).Select(x => x.IdBankReceipt).Distinct().ToListAsync(cancellationToken);
        var receipts = await _db.BankReceipts.AsNoTracking().Include(x => x.IdCounterpartyNavigation).Include(x => x.IdCurrencyNavigation).Include(x => x.ReceiptAllocations).Where(x => receiptIds.Contains(x.IdBankReceipt)).Select(ToReceiptExpression).ToListAsync(cancellationToken);
        var remittanceEntities = await RemittanceDetails().Where(x => x.IsActive && x.IdPanelNavigation != null
            && x.IdPanelNavigation.IdContract == contractId
            && x.IdPanelNavigation.Role != null && x.IdPanelNavigation.Role.Trim().ToUpper() == "REINSURER"
            && x.IdPanelNavigation.IdCounterparty == x.IdCounterparty).ToListAsync(cancellationToken);
        var remittances = remittanceEntities.Select(MapOutgoingRemittance).ToList();
        var invoices = await _db.Invoices.AsNoTracking().Where(x => x.IdContract == contractId).Select(ToInvoiceExpression).ToListAsync(cancellationToken);
        NormalizeInvoiceAllocationPayables(invoices);
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

    private async Task NotifyRoleAsync(string position, string title, string message, string entityType, int entityId, CancellationToken cancellationToken, string? notificationType = null, string? targetRouteOverride = null)
    {
        var users = await _db.AspNetUsers.Where(x => x.Position != null && x.Position.Trim().ToLower() == position.ToLower()).Select(x => x.Id).ToListAsync(cancellationToken);
        var creator = CurrentUser();
        var targetRoute = targetRouteOverride ?? (entityType == "OUTGOING_CLOSING" ? $"/workspace/reassurance/finance-officer/disbursement-queue?closingId={entityId}" : entityType == "BANK_RECEIPT" ? $"/back-office/bank-receipts/{entityId}/pairing" : entityType == "REMITTANCE_REF" ? "/finance/disbursement-queue" : "/finance/disbursements");
        foreach (var id in users) _db.Notifications.Add(new Notification { RecipientUserId = id, RecipientRole = position, NotificationType = notificationType ?? (entityType == "BANK_RECEIPT" ? "ReceiptRequiresPairing" : entityType), Title = title, Message = message, EntityType = entityType, EntityId = entityId, ClosingId = entityType == "OUTGOING_CLOSING" ? entityId : null, TargetRoute = targetRoute, CreatedAt = DateTime.UtcNow, CreatedBy = creator });
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
    private static readonly System.Linq.Expressions.Expression<Func<BankReceipt, BankReceiptResponseDto>> ToReceiptExpression = x => new() { IdBankReceipt = x.IdBankReceipt, BankAccountId = x.IdBankAccount, CounterpartyId = x.IdCounterparty, CounterpartyName = x.IdCounterpartyNavigation.Name, BankName = x.IdBankAccountNavigation.BankName, MaskedIban = x.IdBankAccountNavigation.Iban == null || x.IdBankAccountNavigation.Iban == "" ? null : x.IdBankAccountNavigation.Iban.Length <= 4 ? x.IdBankAccountNavigation.Iban : x.IdBankAccountNavigation.Iban.Substring(0, 2) + "****" + x.IdBankAccountNavigation.Iban.Substring(x.IdBankAccountNavigation.Iban.Length - 4, 4), ReceptionDate = x.ReceptionDate, OperationDate = x.OperationDate, ReceiptMonth = x.OperationDate.Year.ToString() + "-" + (x.OperationDate.Month < 10 ? "0" : "") + x.OperationDate.Month.ToString(), AmountReceived = x.AmountReceived, CurrencyId = x.IdCurrency, CurrencyCode = x.IdCurrencyNavigation.CurrencyCode, AppliedExchangeRate = x.AppliedExchangeRate, AvailableAmount = x.AmountReceived - x.ReceiptAllocations.Where(a => a.IsActive).Sum(a => a.AllocatedAmount ?? 0m), AllocatedAmount = x.ReceiptAllocations.Where(a => a.IsActive).Sum(a => a.AllocatedAmount ?? 0m), PairingStatus = x.AmountReceived > 0m && x.ReceiptAllocations.Where(a => a.IsActive).Sum(a => a.AllocatedAmount ?? 0m) >= x.AmountReceived ? "Paired" : "Unpaired", SettlementMethod = x.SettlementMethod, BankReference = x.BankReference, SettlementLabel = x.SettlementLabel, BusinessReference = x.BusinessReference, DeclaredInvoiceNumber = x.DeclaredInvoiceNumber, DeclaredInvoiceMonth = x.DeclaredInvoiceMonth, Reason = x.Reason };
    private static BankReceiptResponseDto ToReceiptEntity(BankReceipt x) => new() { IdBankReceipt = x.IdBankReceipt, BankAccountId = x.IdBankAccount, CounterpartyId = x.IdCounterparty, CounterpartyName = x.IdCounterpartyNavigation?.Name ?? string.Empty, BankName = x.IdBankAccountNavigation?.BankName ?? string.Empty, MaskedIban = MaskIban(x.IdBankAccountNavigation?.Iban), ReceptionDate = x.ReceptionDate, OperationDate = x.OperationDate, ReceiptMonth = x.OperationDate.ToString("yyyy-MM"), AmountReceived = x.AmountReceived, CurrencyId = x.IdCurrency, CurrencyCode = x.IdCurrencyNavigation?.CurrencyCode ?? string.Empty, AppliedExchangeRate = x.AppliedExchangeRate, AvailableAmount = x.AmountReceived - x.ReceiptAllocations.Where(a => a.IsActive).Sum(a => a.AllocatedAmount ?? 0m), AllocatedAmount = x.ReceiptAllocations.Where(a => a.IsActive).Sum(a => a.AllocatedAmount ?? 0m), PairingStatus = InvoiceRecoveryCalculator.GetReceiptPairingStatus(x.AmountReceived, x.ReceiptAllocations.Where(a => a.IsActive).Sum(a => a.AllocatedAmount ?? 0m)), SettlementMethod = x.SettlementMethod, BankReference = x.BankReference, SettlementLabel = x.SettlementLabel, BusinessReference = x.BusinessReference, DeclaredInvoiceNumber = x.DeclaredInvoiceNumber, DeclaredInvoiceMonth = x.DeclaredInvoiceMonth, Reason = x.Reason };
    private static RemittanceResponseDto ToRemittance(RemittanceRef x) => new() { RemittanceRef = x.RemittanceRef1, RemittanceReference = x.RemittanceRef1.ToString(), ReceiptAllocationId = x.IdReceiptAllocation, DisbursementId = x.IdDisbursement, PanelId = x.IdPanel, ContractId = x.IdPanelNavigation?.IdContract ?? x.SourceInvoice?.IdContract, ContractName = x.IdPanelNavigation?.IdContractNavigation?.Name ?? x.SourceInvoice?.IdContractNavigation?.Name, Umr = x.IdPanelNavigation?.IdContractNavigation?.Umr ?? x.SourceInvoice?.IdContractNavigation?.Umr, CedentName = x.IdPanelNavigation?.IdContractNavigation?.IdCounterpartyNavigation?.Name ?? x.SourceInvoice?.IdContractNavigation?.IdCounterpartyNavigation?.Name, CounterpartyId = x.IdCounterparty, CounterpartyName = x.IdCounterpartyNavigation?.Name ?? string.Empty, BeneficiaryId = x.IdCounterparty, BeneficiaryName = x.IdCounterpartyNavigation?.Name, BeneficiaryType = x.BeneficiaryType ?? (x.IdPanel.HasValue ? "REINSURER" : "CEDENT"), ReinsurerId = x.IdCounterparty, ReinsurerName = x.IdCounterpartyNavigation?.Name ?? string.Empty, GrossPremium = x.PremiumSignedLine, OtherDeductions = x.Deductions, NetPremiumDue = x.NetPremiumDue, NetPayableAmount = x.NetPayableAmount, CurrencyId = x.IdCurrency, CurrencyCode = x.IdCurrencyNavigation?.CurrencyCode ?? string.Empty, Status = x.Status, Description = x.Description, ConfirmedAt = x.ConfirmedAt, ConfirmedBy = x.ConfirmedBy };
    private static DisbursementResponseDto ToDisbursement(Disbursement x, RemittanceRef? remittance) => new() { IdDisbursement = x.IdDisbursement, RemittanceRef = remittance?.RemittanceRef1 ?? 0, CounterpartyId = x.IdCounterparty, CounterpartyName = x.IdCounterpartyNavigation?.Name ?? remittance?.IdCounterpartyNavigation?.Name ?? string.Empty, TotalAmount = x.TotalAmount, CurrencyId = x.IdBankAccountNavigation?.IdCurrency ?? remittance?.IdCurrency ?? 0, CurrencyCode = x.IdBankAccountNavigation?.IdCurrencyNavigation?.CurrencyCode ?? remittance?.IdCurrencyNavigation?.CurrencyCode ?? string.Empty, OperationDate = x.OperationDate, SignatureStatus = x.SignatureStatus };
    private static InvoiceResponseDto ToInvoice(Invoice x)
    {
        var gross = x.GrossPremium ?? 0m;
        var paired = x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m);
        paired += InvoiceDocumentAccounting.IsAvoir(x.InvoiceType)
            ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
            : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount);
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
            DocumentNumber = x.InvoiceNumber,
            InvoiceType = InvoiceDocumentAccounting.NormalizeStoredType(x.InvoiceType),
            ProcessingMode = x.ProcessingMode,
            Sequence = x.SequenceNumber,
            Amount = gross,
            SignedAmount = InvoiceDocumentAccounting.SignedAmount(x),
            IsAvoir = InvoiceDocumentAccounting.IsAvoir(x.InvoiceType),
            InvoiceDate = x.InvoiceDate,
            InvoiceAmount = gross,
            SettledAmount = recovery.PairedAmount,
            RemainingAmount = recovery.OutstandingAmount,
            PaymentStatus = recovery.Status,
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
        DocumentNumber = x.InvoiceNumber,
        InvoiceType = x.InvoiceType,
        ProcessingMode = x.ProcessingMode,
        Sequence = x.SequenceNumber,
        Amount = x.GrossPremium ?? 0m,
        SignedAmount = x.InvoiceType.ToUpper() == InvoiceTypeAvoir || x.InvoiceType.ToUpper() == "CREDIT_NOTE"
            ? -(x.GrossPremium ?? 0m) : (x.GrossPremium ?? 0m),
        IsAvoir = x.InvoiceType.ToUpper() == InvoiceTypeAvoir || x.InvoiceType.ToUpper() == "CREDIT_NOTE",
        InvoiceDate = x.InvoiceDate,
        Allocations = x.PanelAllocations.OrderBy(line => line.IdPanel).Select(line => new InvoicePanelAllocationResponseDto
        {
            PanelId = line.IdPanel,
            ReinsurerId = line.IdCounterparty,
            ReinsurerCode = line.IdCounterpartyNavigation.LegacyCompanyCode,
            ReinsurerName = line.ReinsurerNameSnapshot,
            SharePercentage = line.SignedSharePctSnapshot ?? 0m,
            AllocationRatioPercentage = line.AllocationRatio * 100m,
            AllocatedGrossPremium = line.GrossPremium,
            CedentCommissionRate = line.CedentCommissionPct ?? 0m,
            CedentCommissionPercentage = line.CedentCommissionPct ?? 0m,
            CedentCommissionAmount = line.CedentCommissionAmount,
            AllocatedNetPremium = line.NetAmount,
            IrisBrokerageRate = line.BrokeragePct ?? 0m,
            IrisBrokeragePercentage = line.BrokeragePct ?? 0m,
            IrisBrokerageAmount = line.BrokerageAmount,
            OtherDeductions = line.OtherDeductions,
            OtherDeductionsAmount = line.OtherDeductions,
            OtherDeductionsPercentage = line.OtherDeductionsPercentage,
            NetPayableAmount = line.NetAmount - line.BrokerageAmount - line.OtherDeductions,
            SignedNetPayableAmount = x.InvoiceType == InvoiceTypeAvoir || x.InvoiceType == "CREDIT_NOTE"
                ? -(line.NetAmount - line.BrokerageAmount - line.OtherDeductions)
                : line.NetAmount - line.BrokerageAmount - line.OtherDeductions,
            AllocationAmount = line.NetAmount,
            SignedAllocationAmount = x.InvoiceType == InvoiceTypeAvoir || x.InvoiceType == "CREDIT_NOTE"
                ? -line.NetAmount : line.NetAmount,
            Currency = line.CurrencyCodeSnapshot
        }).ToList(),
        InvoiceAmount = x.GrossPremium ?? 0m,
        SettledAmount = x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
            + (x.InvoiceType == InvoiceTypeAvoir
                ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount)),
        RemainingAmount = (x.GrossPremium ?? 0m) > x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
            + (x.InvoiceType == InvoiceTypeAvoir
                ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount))
            ? (x.GrossPremium ?? 0m) - x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                - (x.InvoiceType == InvoiceTypeAvoir
                    ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                    : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount))
            : 0m,
        PaymentStatus = x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
            + (x.InvoiceType == InvoiceTypeAvoir
                ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount)) <= 0m
            ? InvoiceRecoveryCalculator.Unsettled
            : x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                + (x.InvoiceType == InvoiceTypeAvoir
                    ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                    : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount)) >= (x.GrossPremium ?? 0m)
                ? InvoiceRecoveryCalculator.Settled
                : InvoiceRecoveryCalculator.PartiallySettled,
        GrossAmount = x.GrossPremium ?? 0m,
        NetPremium = x.NetPremium,
        NetAmount = x.NetPremium,
        PairedAmount = x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
            + (x.InvoiceType == InvoiceTypeAvoir
                ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount)),
        OutstandingAmount = (x.GrossPremium ?? 0m) > x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
            + (x.InvoiceType == InvoiceTypeAvoir
                ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount))
            ? (x.GrossPremium ?? 0m) - x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                - (x.InvoiceType == InvoiceTypeAvoir
                    ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                    : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount))
            : 0m,
        RecoveryStatus = x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
            + (x.InvoiceType == InvoiceTypeAvoir
                ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount)) <= 0m
            ? InvoiceRecoveryCalculator.Unsettled
            : x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                + (x.InvoiceType == InvoiceTypeAvoir
                    ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                    : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount)) >= (x.GrossPremium ?? 0m)
                ? InvoiceRecoveryCalculator.Settled
                : InvoiceRecoveryCalculator.PartiallySettled,
        PairingStatus = x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
            + (x.InvoiceType == InvoiceTypeAvoir
                ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount)) <= 0m
            ? InvoiceRecoveryCalculator.Unsettled
            : x.ReceiptAllocations.Where(a => a.IsActive).Sum(allocation => allocation.AllocatedAmount ?? 0m)
                + (x.InvoiceType == InvoiceTypeAvoir
                    ? x.CreditApplicationsAsAvoir.Where(application => application.IsActive).Sum(application => application.Amount)
                    : x.CreditApplicationsAsTarget.Where(application => application.IsActive).Sum(application => application.Amount)) >= (x.GrossPremium ?? 0m)
                ? InvoiceRecoveryCalculator.Settled
                : InvoiceRecoveryCalculator.PartiallySettled,
        Status = x.Status,
        Label = x.Label ?? string.Empty
    };

    private static void NormalizeInvoiceAllocationPayables(IEnumerable<InvoiceResponseDto> invoices)
    {
        foreach (var invoice in invoices)
        {
            var isAvoir = InvoiceDocumentAccounting.IsAvoir(invoice.InvoiceType);
            foreach (var allocation in invoice.Allocations)
            {
                var payable = PremiumDebitNoteAllocationCalculator.CalculateNetPayable(
                    allocation.AllocatedNetPremium, allocation.IrisBrokerageAmount, allocation.OtherDeductions);
                allocation.NetPayableAmount = payable;
                allocation.SignedNetPayableAmount = isAvoir ? -Math.Abs(payable) : Math.Abs(payable);
            }
        }
    }
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
    private ServiceResult<T> Failure<T>(string message, IEnumerable<string>? errors = null, int status = StatusCodes.Status400BadRequest, string? code = null, string? traceId = null)
    {
        var result = ServiceResult<T>.Failure(message, errors, status, code);
        if (result.Error is not null) result.Error.TraceId = traceId ?? _http.HttpContext?.TraceIdentifier;
        return result;
    }
}
