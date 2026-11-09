using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using IRISBackend.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using IRISBackend.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using SecurityClaim = System.Security.Claims.Claim;

namespace IRISBackend.Tests;

public sealed class FinanceWorkspaceImprovementsTests : IAsyncLifetime
{
    private readonly string database = "IRIS_FinanceWorkspace_Tests_" + Guid.NewGuid().ToString("N");
    private IRISBackendContext db = null!; private Counterparty first = null!; private Counterparty other = null!;
    private Currency currency = null!; private Currency euro = null!; private FinancialWorkflowService service = null!;
    private readonly QueryCounter counter = new();
    public async Task InitializeAsync()
    {
        db = new IRISBackendContext(new DbContextOptionsBuilder<IRISBackendContext>().UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5").AddInterceptors(counter).Options);
        await db.Database.EnsureCreatedAsync();
        first = new Counterparty { Name = "Acme Cedent", LegacyCompanyCode = "ACME-BIZ", Email = "accounts@acme.example.test", Type = "Cedent", Status = "Active", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        other = new Counterparty { Name = "Other Cedent", Type = "Cedent", Status = "Active", IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        currency = new Currency { CurrencyCode = "USD", Name = "US Dollar", DecimalPlaces = 2, IsActive = true }; euro = new Currency { CurrencyCode = "EUR", Name = "Euro", DecimalPlaces = 2, IsActive = true };
        db.AddRange(first, other, currency, euro, new AspNetUser { Id = "finance-test", FullName = "Finance Test", Department = "Reassurance", Position = "Finance officer", UserName = "finance-test", Email = "finance-test@example.test" }); await db.SaveChangesAsync();
        db.CounterpartyTypeAssignments.Add(new CounterpartyTypeAssignment { CounterpartyId = first.Id, Type = "Cedent" }); await db.SaveChangesAsync();
        service = Service(ModuleAccessCatalog.Encaissement, ModuleAccessCatalog.Decaissement, ModuleAccessCatalog.CounterpartyBankAccounts);
    }
    public async Task DisposeAsync() { await db.Database.EnsureDeletedAsync(); await db.DisposeAsync(); }
    private FinancialWorkflowService Service(params string[] modules) => new(db, new RequestAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new SecurityClaim(ClaimTypes.NameIdentifier, "finance-test") }.Concat(modules.Select(m => new SecurityClaim(ModuleAccessCatalog.ClaimType, m))), "test")) } }, NullLogger<FinancialWorkflowService>.Instance);
    private BankAccountRequestDto Account(string suffix = "01", int? currencyId = null) => new() { CounterpartyId = first.Id, CurrencyId = currencyId ?? currency.IdCurrency, BankName = "Test Bank", BankCity = "Test City", BankCountry = "Tunisia", Iban = "TN59000000000000000000" + suffix, SwiftBic = "ABCDEF12", AccountLabel = "Account " + suffix, IsActive = true };
    private async Task<BankAccountResponseDto> Create(string suffix = "01", int? currencyId = null) { var result = await service.CreateBankAccountAsync(first.Id, Account(suffix, currencyId), default); Assert.True(result.Succeeded, result.Error?.Message); return result.Value!; }
    private BankReceiptRequestDto Receipt(int accountId, DateOnly? date = null, decimal amount = 100) => new() { CounterpartyId = first.Id, BankAccountId = accountId, OperationDate = date ?? new DateOnly(2026, 9, 23), AmountReceived = amount, SettlementMethod = "BankTransfer", BankReference = "BANK-TEST", BusinessReference = "ENC-TEST", SettlementLabel = "Premium settlement" };
    [Fact]
    public async Task ExactCalendarDayPersistsAndRoundTripsIndependentlyOfReceptionDate()
    {
        var account = await Create(); var result = await service.CreateReceiptAsync(Receipt(account.IdBankAccount, new(2026, 3, 29)), default);
        Assert.True(result.Succeeded, result.Error?.Message); var saved = await db.BankReceipts.AsNoTracking().SingleAsync();
        Assert.Equal(new DateOnly(2026, 3, 29), saved.OperationDate); Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), saved.ReceptionDate);
        var read = await service.GetReceiptAsync(saved.IdBankReceipt, default); Assert.Equal(saved.OperationDate, read.Value!.OperationDate);
        var json = JsonSerializer.Serialize(read.Value); Assert.Contains("\"OperationDate\":\"2026-03-29\"", json);
    }
    [Fact]
    public async Task MissingDefaultOrMonthOnlyDatesAreRejected()
    {
        var account = await Create(); var request = Receipt(account.IdBankAccount); request.OperationDate = null; request.ReceiptMonth = "2026-09";
        Assert.Equal(400, (await service.CreateReceiptAsync(request, default)).StatusCode);
        request.OperationDate = default(DateOnly); Assert.Equal(400, (await service.CreateReceiptAsync(request, default)).StatusCode); Assert.Empty(await db.BankReceipts.ToListAsync());
    }
    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("2026-09")]
    [InlineData("2026-03-29T23:00:00Z")]
    public void NonCalendarDateJsonCannotBindToDateOnly(string value) { Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BankReceiptRequestDto>("{\"OperationDate\":\"" + value + "\"}")); }
    [Fact] public async Task CrossCounterpartyReceiptAccountMismatchIsRejected() { var account = await Create(); var request = Receipt(account.IdBankAccount); request.CounterpartyId = other.Id; Assert.Equal(400, (await service.CreateReceiptAsync(request, default)).StatusCode); Assert.Empty(await db.BankReceipts.ToListAsync()); }
    [Fact]
    public async Task MultipleIndependentAccountsAndNormalizedDuplicateProtection()
    {
        var one = await Create(); var two = await Create("02"); Assert.NotEqual(one.IdBankAccount, two.IdBankAccount); Assert.Equal(2, await db.CounterpartyBankAccounts.CountAsync());
        var duplicate = Account(); duplicate.Iban = string.Join(" ", Account().Iban!.ToLowerInvariant().Chunk(4).Select(chars => new string(chars)));
        var denied = await service.CreateBankAccountAsync(first.Id, duplicate, default); Assert.Equal(409, denied.StatusCode); Assert.Equal("DUPLICATE_BANK_ACCOUNT", denied.Error!.Code);
        var unrelated = Account(); unrelated.CounterpartyId = other.Id; Assert.True((await service.CreateBankAccountAsync(other.Id, unrelated, default)).Succeeded);
        Assert.Equal(2, (await service.GetBankAccountsAsync(first.Id, false, default)).Value!.Count);
    }
    [Fact]
    public async Task DefaultReplacementIsTransactionalAndPerCurrency()
    {
        var one = await Create(); var two = await Create("02"); var eur = await Create("03", euro.IdCurrency);
        Assert.True((await service.SetBankAccountDefaultAsync(first.Id, two.IdBankAccount, default)).Succeeded);
        var accounts = await db.CounterpartyBankAccounts.AsNoTracking().ToListAsync(); Assert.Single(accounts, a => a.IsDefault && a.IdCurrency == currency.IdCurrency); Assert.True(accounts.Single(a => a.IdBankAccount == two.IdBankAccount).IsDefault); Assert.True(accounts.Single(a => a.IdBankAccount == eur.IdBankAccount).IsDefault);
        Assert.False(accounts.Single(a => a.IdBankAccount == one.IdBankAccount).IsDefault);
    }
    [Fact]
    public async Task DeactivatingReferencedDefaultKeepsReceiptAndChoosesLowestActiveReplacement()
    {
        var one = await Create(); var two = await Create("02"); var created = await service.CreateReceiptAsync(Receipt(one.IdBankAccount), default); Assert.True(created.Succeeded);
        Assert.True((await service.SetBankAccountStatusAsync(first.Id, one.IdBankAccount, false, default)).Succeeded);
        var account = await db.CounterpartyBankAccounts.AsNoTracking().SingleAsync(a => a.IdBankAccount == one.IdBankAccount); Assert.False(account.IsActive); Assert.False(account.IsDefault);
        Assert.True((await db.CounterpartyBankAccounts.AsNoTracking().SingleAsync(a => a.IdBankAccount == two.IdBankAccount)).IsDefault); Assert.Equal(one.IdBankAccount, (await db.BankReceipts.AsNoTracking().SingleAsync()).IdBankAccount);
    }
    [Fact]
    public async Task EditingDetailsUsesAuthorizedDetailAndDoesNotReassignOrChangeReferencedCurrency()
    {
        var one = await Create(); var detail = await service.GetBankAccountAsync(first.Id, one.IdBankAccount, default); Assert.True(detail.Succeeded); Assert.NotNull(detail.Value!.Iban);
        Assert.Equal(404, (await service.GetBankAccountAsync(other.Id, one.IdBankAccount, default)).StatusCode);
        var request = Account(); request.CounterpartyId = other.Id; Assert.Equal(400, (await service.UpdateBankAccountAsync(first.Id, one.IdBankAccount, request, default)).StatusCode);
        Assert.True((await service.CreateReceiptAsync(Receipt(one.IdBankAccount), default)).Succeeded);
        request = Account(currencyId: euro.IdCurrency); Assert.Equal(409, (await service.UpdateBankAccountAsync(first.Id, one.IdBankAccount, request, default)).StatusCode);
    }
    [Fact]
    public async Task ListMasksIdentifiersAndNeverReturnsFullValues()
    {
        var input = Account(); input.Rib = "TEST-RIB-PRIVATE-000001"; Assert.True((await service.CreateBankAccountAsync(first.Id, input, default)).Succeeded);
        var result = await service.GetBankAccountsAsync(first.Id, false, default); var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain(input.Iban!, json); Assert.DoesNotContain(input.Rib, json); Assert.Contains("****", json);
    }
    [Fact]
    public async Task CombinedReceiptFiltersAreInclusiveSortedPagedAndUseTwoQueries()
    {
        var account = await Create(); foreach (var row in new[] { (new DateOnly(2026, 9, 22), 50m), (new DateOnly(2026, 9, 23), 100m), (new DateOnly(2026, 9, 24), 150m), (new DateOnly(2026, 9, 25), 200m) }) Assert.True((await service.CreateReceiptAsync(Receipt(account.IdBankAccount, row.Item1, row.Item2), default)).Succeeded);
        counter.Count = 0;
        var result = await service.ListReceiptsAsync(new() { CounterpartyId = first.Id, CurrencyId = currency.IdCurrency, Search = "premium", OperationFrom = new(2026, 9, 23), OperationTo = new(2026, 9, 24), MinAmount = 100, MaxAmount = 150, PageSize = 1, PageNumber = 2, SortBy = "amountReceived", SortDirection = "asc" }, false, default);
        Assert.True(result.Succeeded, result.Error?.Message); Assert.Equal(2, result.Value!.TotalCount); Assert.Equal(150, result.Value.Items.Single().AmountReceived); Assert.Equal(2, counter.Count);
        var none = await service.ListReceiptsAsync(new() { CounterpartyId = other.Id }, false, default); Assert.Empty(none.Value!.Items);
    }
    [Fact]
    public async Task ReceiptReferencesAndInvalidRangesWork()
    {
        var account = await Create(); var receipt = await service.CreateReceiptAsync(Receipt(account.IdBankAccount), default);
        foreach (var term in new[] { "enc-test", "bank-test", "premium", "BR-" + receipt.Value!.IdBankReceipt }) Assert.Single((await service.ListReceiptsAsync(new() { Search = term }, false, default)).Value!.Items);
        Assert.Equal(400, (await service.ListReceiptsAsync(new() { OperationFrom = new(2026, 9, 24), OperationTo = new(2026, 9, 23) }, false, default)).StatusCode);
        Assert.Equal(400, (await service.ListReceiptsAsync(new() { MinAmount = 200, MaxAmount = 100 }, false, default)).StatusCode);
    }
    [Fact]
    public async Task DirectorySearchResolvesBusinessCodeNameAndEmailCaseInsensitively()
    {
        var repository = new CounterpartyRepository(db);
        foreach (var search in new[] { "ACME", "acme-biz", "ACCOUNTS@ACME" }) { var result = await repository.ListAsync(new() { Search = search }, default); Assert.Single(result.Items); Assert.Equal(first.Id, result.Items.Single().Id); }
    }
    [Fact]
    public async Task DefaultAndExplicitSelectionAcceptMultipleAccountsButRejectForeignId()
    {
        var one = await Create(); var two = await Create("02"); var accounts = await db.CounterpartyBankAccounts.Where(a => a.IdCounterparty == first.Id && a.IsActive).ToListAsync();
        Assert.Equal(one.IdBankAccount, BankAccountEligibility.Select(accounts).Account!.IdBankAccount);
        Assert.Equal(two.IdBankAccount, BankAccountEligibility.Select(accounts, two.IdBankAccount).Account!.IdBankAccount);
        Assert.False(BankAccountEligibility.Select(accounts, 999999).IsValid);
    }
    [Fact]
    public async Task ForbiddenRolesAndMissingResourcesAreRejected()
    {
        Assert.Equal(403, (await Service(ModuleAccessCatalog.Contracts).CreateReceiptAsync(new(), default)).StatusCode);
        var missingCounterparty = Account(); missingCounterparty.CounterpartyId = 999999; Assert.Equal(404, (await service.CreateBankAccountAsync(999999, missingCounterparty, default)).StatusCode);
        var missing = Account(currencyId: 999999); Assert.Equal(404, (await service.CreateBankAccountAsync(first.Id, missing, default)).StatusCode);
    }
    [Fact]
    public async Task MissingCurrencyAndInvalidIdentifiersReturnFieldValidationErrors()
    {
        var request = Account(currencyId: 0); request.Iban = "invalid";
        var result = await service.CreateBankAccountAsync(first.Id, request, default);
        Assert.Equal(400, result.StatusCode); Assert.Contains(result.Error!.Errors, e => e.StartsWith("CurrencyId:")); Assert.Contains(result.Error.Errors, e => e.StartsWith("Iban:"));
        Assert.Empty(await db.CounterpartyBankAccounts.ToListAsync());
    }
    [Fact]
    public async Task InactiveAccountsDoNotBecomeDefaultUntilActivated()
    {
        var request = Account(); request.IsActive = false; request.IsDefault = true;
        var result = await service.CreateBankAccountAsync(first.Id, request, default); Assert.True(result.Succeeded, result.Error?.Message); Assert.False(result.Value!.IsActive); Assert.False(result.Value.IsDefault);
        Assert.Empty((await service.GetBankAccountsAsync(first.Id, true, default)).Value!);
        Assert.True((await service.SetBankAccountStatusAsync(first.Id, result.Value.IdBankAccount, true, default)).Succeeded);
        var account = await db.CounterpartyBankAccounts.AsNoTracking().SingleAsync(); Assert.True(account.IsActive); Assert.True(account.IsDefault); Assert.Equal("finance-test", account.CreatedBy);
    }
    [Fact]
    public async Task FailedDefaultReplacementRollsBackThePreviousDefault()
    {
        var one = await Create(); var two = await Create("02");
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TestRejectDefault ON dbo.COUNTERPARTY_BANK_ACCOUNT AFTER UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Is_default=1) THROW 51000,'Test default failure',1; END");
        await Assert.ThrowsAsync<DbUpdateException>(() => service.SetBankAccountDefaultAsync(first.Id, two.IdBankAccount, default));
        var accounts = await db.CounterpartyBankAccounts.AsNoTracking().ToListAsync(); Assert.True(accounts.Single(x => x.IdBankAccount == one.IdBankAccount).IsDefault); Assert.False(accounts.Single(x => x.IdBankAccount == two.IdBankAccount).IsDefault);
    }
    [Fact]
    public async Task ConcurrentAccountCreationSerializesDuplicateAndDefaultDecisions()
    {
        var options = new DbContextOptionsBuilder<IRISBackendContext>().UseSqlServer(db.Database.GetConnectionString()).Options;
        await using var firstDb = new IRISBackendContext(options); await using var secondDb = new IRISBackendContext(options);
        FinancialWorkflowService With(IRISBackendContext context) => new(context, new RequestAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new SecurityClaim(ClaimTypes.NameIdentifier, "finance-test"), new SecurityClaim(ModuleAccessCatalog.ClaimType, ModuleAccessCatalog.CounterpartyBankAccounts)], "test")) } }, NullLogger<FinancialWorkflowService>.Instance);
        var results = await Task.WhenAll(With(firstDb).CreateBankAccountAsync(first.Id, Account(), default), With(secondDb).CreateBankAccountAsync(first.Id, Account(), default));
        Assert.Single(results, x => x.Succeeded); Assert.Single(results, x => x.StatusCode == 409); Assert.Equal(1, await db.CounterpartyBankAccounts.CountAsync(x => x.IsActive && x.IsDefault));
    }
    [Fact]
    public async Task RealHttpAuthorizationAndDateBindingReturn401403400And201()
    {
        var account = await Create();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<IRISBackendContext>(o => o.UseSqlServer(db.Database.GetConnectionString())); builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<IFinancialWorkflowService, FinancialWorkflowService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(FinanceController).Assembly);
        builder.Services.AddAuthentication("FinanceTest").AddScheme<AuthenticationSchemeOptions, FinanceTestAuthenticationHandler>("FinanceTest", _ => { });
        builder.Services.AddAuthorization(options => { foreach (var module in ModuleAccessCatalog.All) options.AddPolicy(ModuleAccessCatalog.Policy(module), policy => policy.RequireAuthenticatedUser().RequireClaim(ModuleAccessCatalog.ClaimType, module)); });
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(); using var client = new HttpClient { BaseAddress = new Uri(address) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/finance/bank-receipts")).StatusCode);
            client.DefaultRequestHeaders.Add("X-Test-Role", "front"); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/finance/bank-receipts")).StatusCode);
            client.DefaultRequestHeaders.Remove("X-Test-Role"); client.DefaultRequestHeaders.Add("X-Test-Role", "finance");
            var invalid = new StringContent("{\"bankAccountId\":" + account.IdBankAccount + ",\"operationDate\":\"2026-02-30\",\"amountReceived\":100,\"settlementMethod\":\"BankTransfer\"}", Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/finance/bank-receipts", invalid)).StatusCode);
            var valid = await client.PostAsJsonAsync("/api/finance/bank-receipts", Receipt(account.IdBankAccount, new(2026, 9, 23))); Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
            using var json = JsonDocument.Parse(await valid.Content.ReadAsStringAsync()); Assert.Equal("2026-09-23", json.RootElement.GetProperty("operationDate").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/finance/bank-receipts?operationFrom=2026-09-24&operationTo=2026-09-23")).StatusCode);
        }
        finally { await app.StopAsync(); }
    }
    public sealed class FinanceTestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString(); if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
            var modules = role == "finance" ? new[] { ModuleAccessCatalog.Encaissement, ModuleAccessCatalog.Decaissement, ModuleAccessCatalog.CounterpartyBankAccounts } : new[] { ModuleAccessCatalog.Contracts };
            var claims = new[] { new SecurityClaim(ClaimTypes.NameIdentifier, "finance-test"), new SecurityClaim(ClaimTypes.Role, "Employee") }.Concat(modules.Select(m => new SecurityClaim(ModuleAccessCatalog.ClaimType, m)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
    private sealed class RequestAccessor : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Count++; return ValueTask.FromResult(result); }
    }
}
