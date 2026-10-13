using System.Security.Claims;
using IRISBackend.Models;
using IRISBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/iris-bank-accounts")]
public sealed class IrisBankAccountsController : ControllerBase
{
    private readonly IRISBackendContext _db;

    public IrisBankAccountsController(IRISBackendContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!CanManage()) return Forbid();
        var accounts = await _db.IrisBankAccounts.AsNoTracking()
            .OrderBy(account => account.DisplayOrder).ThenBy(account => account.IdIrisBankAccount)
            .Select(account => new IrisBankAccountResponse(
                account.IdIrisBankAccount, account.BankName, account.IdCurrency,
                account.IdCurrencyNavigation.CurrencyCode, account.Iban, account.BicSwift,
                account.DisplayOrder, account.IsActive, account.CreatedAt, account.UpdatedAt))
            .ToListAsync(cancellationToken);
        return Ok(accounts);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] IrisBankAccountRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage()) return Forbid();
        if (!TryValidate(request, out var error)) return BadRequest(new { message = error });
        if (!await _db.Currencies.AnyAsync(currency => currency.IdCurrency == request.CurrencyId && currency.IsActive, cancellationToken))
            return BadRequest(new { message = "Currency must identify an active currency." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var account = new IrisBankAccount
        {
            BankName = request.BankName.Trim(), IdCurrency = request.CurrencyId,
            Iban = NormalizeIban(request.Iban), BicSwift = request.BicSwift.Trim().ToUpperInvariant(),
            DisplayOrder = request.DisplayOrder, IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow, CreatedBy = userId
        };
        _db.IrisBankAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(List), new IrisBankAccountResponse(account.IdIrisBankAccount,
            account.BankName, account.IdCurrency, await _db.Currencies.Where(c => c.IdCurrency == account.IdCurrency).Select(c => c.CurrencyCode).SingleAsync(cancellationToken),
            account.Iban, account.BicSwift, account.DisplayOrder, account.IsActive, account.CreatedAt, account.UpdatedAt));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] IrisBankAccountRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage()) return Forbid();
        if (!TryValidate(request, out var error)) return BadRequest(new { message = error });
        if (!await _db.Currencies.AnyAsync(currency => currency.IdCurrency == request.CurrencyId && currency.IsActive, cancellationToken))
            return BadRequest(new { message = "Currency must identify an active currency." });
        var account = await _db.IrisBankAccounts.SingleOrDefaultAsync(item => item.IdIrisBankAccount == id, cancellationToken);
        if (account is null) return NotFound();
        account.BankName = request.BankName.Trim();
        account.IdCurrency = request.CurrencyId;
        account.Iban = NormalizeIban(request.Iban);
        account.BicSwift = request.BicSwift.Trim().ToUpperInvariant();
        account.DisplayOrder = request.DisplayOrder;
        account.IsActive = request.IsActive;
        account.UpdatedAt = DateTime.UtcNow;
        account.UpdatedBy = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _db.SaveChangesAsync(cancellationToken);
        var currencyCode = await _db.Currencies.Where(c => c.IdCurrency == account.IdCurrency).Select(c => c.CurrencyCode).SingleAsync(cancellationToken);
        return Ok(new IrisBankAccountResponse(account.IdIrisBankAccount, account.BankName,
            account.IdCurrency, currencyCode, account.Iban, account.BicSwift,
            account.DisplayOrder, account.IsActive, account.CreatedAt, account.UpdatedAt));
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] IrisBankAccountStatusRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage()) return Forbid();
        var account = await _db.IrisBankAccounts.SingleOrDefaultAsync(item => item.IdIrisBankAccount == id, cancellationToken);
        if (account is null) return NotFound();
        account.IsActive = request.IsActive;
        account.UpdatedAt = DateTime.UtcNow;
        account.UpdatedBy = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private bool CanManage() => User.IsInRole(IdentityRoles.Admin) || FinancialAccessRules.IsFinanceOfficer(User);
    private static string NormalizeIban(string value) => string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
    private static bool TryValidate(IrisBankAccountRequest request, out string error)
    {
        if (string.IsNullOrWhiteSpace(request.BankName)) error = "Bank name is required.";
        else if (request.CurrencyId <= 0) error = "Currency is required.";
        else if (string.IsNullOrWhiteSpace(request.Iban) || NormalizeIban(request.Iban).Length is < 15 or > 34 || NormalizeIban(request.Iban).Any(character => !char.IsLetterOrDigit(character))) error = "IBAN must contain 15 to 34 alphanumeric characters.";
        else if (!System.Text.RegularExpressions.Regex.IsMatch(request.BicSwift.Trim(), "^[A-Za-z]{4}[A-Za-z]{2}[A-Za-z0-9]{2}([A-Za-z0-9]{3})?$")) error = "BIC/SWIFT must be a valid 8- or 11-character value.";
        else { error = string.Empty; return true; }
        return false;
    }
}

public sealed record IrisBankAccountRequest(string BankName, int CurrencyId, string Iban, string BicSwift, int DisplayOrder = 0, bool IsActive = true);
public sealed record IrisBankAccountStatusRequest(bool IsActive);
public sealed record IrisBankAccountResponse(int Id, string BankName, int CurrencyId, string CurrencyCode,
    string Iban, string BicSwift, int DisplayOrder, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);
