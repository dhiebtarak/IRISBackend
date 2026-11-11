using System.Data;
using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;
namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    public async Task<ServiceResult<BankAccountEditResponseDto>> GetBankAccountAsync(int counterpartyId, int bankAccountId, CancellationToken ct)
    {
        if (!CanFinance()) return Forbidden<BankAccountEditResponseDto>();
        var account = await _db.CounterpartyBankAccounts.AsNoTracking().Include(x => x.IdCurrencyNavigation).Include(x => x.IdCounterpartyNavigation)
            .SingleOrDefaultAsync(x => x.IdCounterparty == counterpartyId && x.IdBankAccount == bankAccountId, ct);
        return account is null ? NotFound<BankAccountEditResponseDto>("Bank account was not found.") :
            ServiceResult<BankAccountEditResponseDto>.Success(new() { Account = MapBankAccount(account), Iban = account.Iban, Rib = account.Rib });
    }

    public async Task<ServiceResult<BankAccountResponseDto>> CreateBankAccountAsync(int counterpartyId, BankAccountRequestDto request, CancellationToken ct)
    {
        if (!CanFinance()) return Forbidden<BankAccountResponseDto>();
        var actor = CurrentUser(); if (actor is null) return Unauthorized<BankAccountResponseDto>();
        if (request.CounterpartyId.HasValue && request.CounterpartyId != counterpartyId) return Failure<BankAccountResponseDto>("CounterpartyId does not match the selected counterparty.");
        if (!await _db.Counterparties.AnyAsync(x => x.Id == counterpartyId, ct)) return NotFound<BankAccountResponseDto>("Counterparty was not found.");
        var validation = new IRISBackend.Validators.BankAccountRequestValidator().Validate(request);
        if (!validation.IsValid) return ServiceResult<BankAccountResponseDto>.Failure("Bank account validation failed.", validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
        if (!await _db.Currencies.AnyAsync(x => x.IdCurrency == request.CurrencyId, ct)) return NotFound<BankAccountResponseDto>("Currency was not found.");
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockCounterpartyAccountsAsync(counterpartyId, ct);
        if (await DuplicateBankIdentityAsync(counterpartyId, request.Iban, null, ct)) return DuplicateAccount();
        var account = new CounterpartyBankAccount { IdCounterparty = counterpartyId, CreatedAt = DateTime.UtcNow, CreatedBy = actor };
        ApplyAccountInput(account, request, actor);
        account.UpdatedAt = null; account.UpdatedBy = null;
        _db.CounterpartyBankAccounts.Add(account); await _db.SaveChangesAsync(ct);
        await ReconcileBankDefaultAsync(counterpartyId, request.CurrencyId, request.IsActive && request.IsDefault ? account.IdBankAccount : null, actor, ct);
        await tx.CommitAsync(ct);
        return ServiceResult<BankAccountResponseDto>.Success(await AccountResponseAsync(account, ct), StatusCodes.Status201Created);
    }

    public async Task<ServiceResult<BankAccountResponseDto>> UpdateBankAccountAsync(int counterpartyId, int bankAccountId, BankAccountRequestDto request, CancellationToken ct)
    {
        if (!CanFinance()) return Forbidden<BankAccountResponseDto>();
        var actor = CurrentUser(); if (actor is null) return Unauthorized<BankAccountResponseDto>();
        if (request.CounterpartyId.HasValue && request.CounterpartyId != counterpartyId) return Failure<BankAccountResponseDto>("Bank accounts cannot be reassigned to another counterparty.");
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockCounterpartyAccountsAsync(counterpartyId, ct);
        var account = await _db.CounterpartyBankAccounts.SingleOrDefaultAsync(x => x.IdCounterparty == counterpartyId && x.IdBankAccount == bankAccountId, ct);
        if (account is null) return NotFound<BankAccountResponseDto>("Bank account was not found.");
        var validation = new IRISBackend.Validators.BankAccountRequestValidator().Validate(request);
        if (!validation.IsValid) return ServiceResult<BankAccountResponseDto>.Failure("Bank account validation failed.", validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
        if (!await _db.Currencies.AnyAsync(x => x.IdCurrency == request.CurrencyId, ct)) return NotFound<BankAccountResponseDto>("Currency was not found.");
        if (await DuplicateBankIdentityAsync(counterpartyId, request.Iban, bankAccountId, ct)) return DuplicateAccount();
        var oldCurrency = account.IdCurrency;
        if (oldCurrency != request.CurrencyId && (await _db.BankReceipts.AnyAsync(x => x.IdBankAccount == bankAccountId, ct) || await _db.Disbursements.AnyAsync(x => x.IdBankAccount == bankAccountId, ct)))
            return Failure<BankAccountResponseDto>("The currency of an account referenced by a financial transaction cannot be changed.", status: 409);
        ApplyAccountInput(account, request, actor); await _db.SaveChangesAsync(ct);
        if (oldCurrency != request.CurrencyId) await ReconcileBankDefaultAsync(counterpartyId, oldCurrency, null, actor, ct);
        await ReconcileBankDefaultAsync(counterpartyId, request.CurrencyId, request.IsActive && request.IsDefault ? bankAccountId : null, actor, ct);
        await tx.CommitAsync(ct);
        return ServiceResult<BankAccountResponseDto>.Success(await AccountResponseAsync(account, ct));
    }

    public async Task<ServiceResult<bool>> SetBankAccountStatusAsync(int counterpartyId, int bankAccountId, bool isActive, CancellationToken ct)
    {
        if (!CanFinance()) return Forbidden<bool>();
        var actor = CurrentUser(); if (actor is null) return Unauthorized<bool>();
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockCounterpartyAccountsAsync(counterpartyId, ct);
        var account = await _db.CounterpartyBankAccounts.SingleOrDefaultAsync(x => x.IdCounterparty == counterpartyId && x.IdBankAccount == bankAccountId, ct);
        if (account is null) return NotFound<bool>("Bank account was not found.");
        account.IsActive = isActive; if (!isActive) account.IsDefault = false; account.UpdatedAt = DateTime.UtcNow; account.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct); await ReconcileBankDefaultAsync(counterpartyId, account.IdCurrency, null, actor, ct);
        await tx.CommitAsync(ct); return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<BankAccountResponseDto>> SetBankAccountDefaultAsync(int counterpartyId, int bankAccountId, CancellationToken ct)
    {
        if (!CanFinance()) return Forbidden<BankAccountResponseDto>();
        var actor = CurrentUser(); if (actor is null) return Unauthorized<BankAccountResponseDto>();
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockCounterpartyAccountsAsync(counterpartyId, ct);
        var account = await _db.CounterpartyBankAccounts.SingleOrDefaultAsync(x => x.IdCounterparty == counterpartyId && x.IdBankAccount == bankAccountId, ct);
        if (account is null) return NotFound<BankAccountResponseDto>("Bank account was not found.");
        if (!account.IsActive) return Failure<BankAccountResponseDto>("Only an active account can be the default.");
        await ReconcileBankDefaultAsync(counterpartyId, account.IdCurrency, bankAccountId, actor, ct);
        await tx.CommitAsync(ct); return ServiceResult<BankAccountResponseDto>.Success(await AccountResponseAsync(account, ct));
    }

    private async Task LockCounterpartyAccountsAsync(int id, CancellationToken ct)
    {
        var resource = $"IRIS:CounterpartyBankAccounts:{id}";
        await _db.Database.ExecuteSqlInterpolatedAsync($@"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51000,'Bank accounts are busy; retry the operation.',1;", ct);
    }
    private async Task<bool> DuplicateBankIdentityAsync(int counterpartyId, string? iban, int? exceptId, CancellationToken ct)
    {
        var normalized = NormalizeIban(iban);
        if (string.IsNullOrEmpty(normalized)) return false;
        return await _db.CounterpartyBankAccounts.AnyAsync(x => x.IdCounterparty == counterpartyId && (!exceptId.HasValue || x.IdBankAccount != exceptId)
            && x.Iban != null && x.Iban.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "").ToUpper() == normalized, ct);
    }
    private static ServiceResult<BankAccountResponseDto> DuplicateAccount() => ServiceResult<BankAccountResponseDto>.Failure(
        "This counterparty already has an account with the same IBAN.", ["Iban: This account already exists for the selected counterparty."], 409, "DUPLICATE_BANK_ACCOUNT");
    private static void ApplyAccountInput(CounterpartyBankAccount account, BankAccountRequestDto request, string actor)
    {
        account.IdCurrency = request.CurrencyId; account.BankName = request.BankName.Trim(); account.BankCity = request.BankCity.Trim(); account.BankCountry = request.BankCountry.Trim();
        account.AgencyCode = Optional(request.AgencyCode); account.Rib = Optional(request.Rib); account.Iban = NormalizeIban(request.Iban); account.SwiftBic = NormalizeBic(request.SwiftBic);
        account.HasCorrespondentBank = request.HasCorrespondentBank; account.CorrespondentBankName = request.HasCorrespondentBank ? Optional(request.CorrespondentBankName) : null;
        account.CorrespondentSwiftBic = request.HasCorrespondentBank ? NormalizeBic(request.CorrespondentSwiftBic) : null; account.AccountLabel = Optional(request.AccountLabel);
        account.IsActive = request.IsActive; account.IsDefault = false; account.UpdatedAt = DateTime.UtcNow; account.UpdatedBy = actor;
    }
    private async Task ReconcileBankDefaultAsync(int counterpartyId, int currencyId, int? preferredId, string actor, CancellationToken ct)
    {
        var accounts = await _db.CounterpartyBankAccounts.Where(x => x.IdCounterparty == counterpartyId && x.IdCurrency == currencyId && x.IsActive).OrderBy(x => x.IdBankAccount).ToListAsync(ct);
        var chosen = preferredId.HasValue ? accounts.Single(x => x.IdBankAccount == preferredId) : accounts.FirstOrDefault(x => x.IsDefault) ?? accounts.FirstOrDefault();
        foreach (var account in accounts.Where(x => x.IsDefault && x != chosen)) { account.IsDefault = false; account.UpdatedAt = DateTime.UtcNow; account.UpdatedBy = actor; }
        // Clear before setting so SQL Server's existing filtered unique index is never violated.
        await _db.SaveChangesAsync(ct);
        if (chosen != null && !chosen.IsDefault) { chosen.IsDefault = true; chosen.UpdatedAt = DateTime.UtcNow; chosen.UpdatedBy = actor; await _db.SaveChangesAsync(ct); }
    }
    private async Task<BankAccountResponseDto> AccountResponseAsync(CounterpartyBankAccount account, CancellationToken ct)
    {
        await _db.Entry(account).Reference(x => x.IdCounterpartyNavigation).LoadAsync(ct);
        await _db.Entry(account).Reference(x => x.IdCurrencyNavigation).LoadAsync(ct); return MapBankAccount(account);
    }
}
