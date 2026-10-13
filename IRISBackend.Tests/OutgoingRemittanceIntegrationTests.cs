using System.Security.Claims;
using System.Text;
using System.Data.Common;
using System.Text.Json;
using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Xunit;
using SecurityClaim = System.Security.Claims.Claim;

namespace IRISBackend.Tests;

public sealed class OutgoingRemittanceIntegrationTests : IAsyncLifetime
{
    private readonly string _database = $"IRISBackend_OutgoingTests_{Guid.NewGuid():N}";
    private IRISBackendContext _db = null!;
    private BankReceipt _receipt = null!;
    private Invoice _invoice = null!;
    private ContractPanel[] _panels = [];

    public async Task InitializeAsync()
    {
        _db = Context();
        await _db.Database.EnsureCreatedAsync();
        var user = new AspNetUser { Id = "back-user", FullName = "Back User", Department = "Reassurance", Position = "Back Officer", UserName = "back", Email = "back@example.test" };
        var finance = new AspNetUser { Id = "finance-user", FullName = "Finance User", Department = "Reassurance", Position = "Finance Officer", UserName = "finance", Email = "finance@example.test" };
        var currency = new Currency { CurrencyCode = "TND", Name = "Tunisian dinar", DecimalPlaces = 3, IsActive = true };
        var cedent = new Counterparty { Name = "Cedent", Type = "Cedent", IsActive = true, Status = "Active", CreatedAtUtc = DateTime.UtcNow };
        var ami = new Counterparty { Name = "AMI", Type = "Reinsurer", IsActive = true, Status = "Active", AddressLine = "AMI street", City = "Tunis", Country = "Tunisia", CreatedAtUtc = DateTime.UtcNow };
        var star = new Counterparty { Name = "STAR Assurances", Type = "Reinsurer", IsActive = true, Status = "Active", CreatedAtUtc = DateTime.UtcNow };
        _db.AddRange(user, finance, currency, cedent, ami, star);
        await _db.SaveChangesAsync();
        _db.CounterpartyTypeAssignments.AddRange(new CounterpartyTypeAssignment { CounterpartyId = ami.Id, Type = "Reinsurer" }, new CounterpartyTypeAssignment { CounterpartyId = star.Id, Type = "Reinsurer" });
        var account = new CounterpartyBankAccount { IdCounterparty = cedent.Id, IdCurrency = currency.IdCurrency, BankName = "Receipt bank", IsActive = true, CreatedBy = user.Id, CreatedAt = DateTime.UtcNow };
        var reinsurerAccount = new CounterpartyBankAccount { IdCounterparty = ami.Id, IdCurrency = currency.IdCurrency, BankName = "AMI bank", IsActive = true, IsDefault = true, Iban = "TN5900000000000000000000", SwiftBic = "ABCDEF12", CreatedBy = user.Id, CreatedAt = DateTime.UtcNow };
        var contract = new Contract { Name = "Factory protection", Umr = "UMR-TEST-2026", TypeOfBusiness = "Facultative", Status = "Bound", IdCounterparty = cedent.Id, IdCurrency = currency.IdCurrency, CreatedBy = user.Id, IdUser = user.Id, CreatedAt = DateTime.UtcNow, InceptionDate = new(2026, 1, 1), ExpiryDate = new(2026, 12, 31) };
        _db.AddRange(account, reinsurerAccount, contract);
        await _db.SaveChangesAsync();
        _receipt = new BankReceipt { IdBankAccount = account.IdBankAccount, IdCurrency = currency.IdCurrency, IdCounterparty = cedent.Id, AmountReceived = 1000m, SettlementMethod = "BankTransfer", CollectionType = "Premium", BankReference = "BANK-001", BusinessReference = "ENC-001", CreatedBy = user.Id, CreatedAt = DateTime.UtcNow, DetailsStatus = "Unpaired" };
        _invoice = new Invoice { IdContract = contract.IdContract, IdCurrency = currency.IdCurrency, InvoiceNumber = "INV-001", InvoiceType = "CEDENT_PREMIUM_INVOICE", GrossPremium = 1000m, NetPremium = 1000m, CreatedBy = user.Id, IdUser = user.Id, CreatedAt = DateTime.UtcNow, Status = "Issued", InvoiceDate = new(2026, 10, 8) };
        _panels = [new ContractPanel { IdContract = contract.IdContract, IdCounterparty = ami.Id, Role = "Reinsurer", Status = "Confirmed", NetPremium = 600m, SignedSharePct = 60m, PremiumReference = "AMI-PANEL" },
            new ContractPanel { IdContract = contract.IdContract, IdCounterparty = star.Id, Role = "Reinsurer", Status = "Bound", NetPremium = 400m, SignedSharePct = 40m, PremiumReference = "STAR-PANEL" }];
        _db.AddRange(_receipt, _invoice);
        _db.ContractPanels.AddRange(_panels);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task PartialPairingCreatesNoLines()
    {
        var result = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 500m }] }, default);
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Empty(await _db.RemittanceRefs.ToListAsync());
    }

    [Fact]
    public async Task BackOfficerInvoiceDownloadReturnsFrenchCedentInvoicePdf()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.CedentCommission = 10m;
        _invoice.GrossPremium = 50000m;
        var accountValues = new Dictionary<string, string?>
        {
            ["IrisBankAccounts:Accounts:0:BankName"] = "Test Bank TND",
            ["IrisBankAccounts:Accounts:0:Currency"] = "TND",
            ["IrisBankAccounts:Accounts:0:Iban"] = "TNTESTIBAN0000000000000001",
            ["IrisBankAccounts:Accounts:0:SwiftBic"] = "TESTTN01",
            ["IrisBankAccounts:Accounts:0:IsActive"] = "true",
            ["IrisBankAccounts:Accounts:1:BankName"] = "Test Bank EUR",
            ["IrisBankAccounts:Accounts:1:Currency"] = "EUR",
            ["IrisBankAccounts:Accounts:1:Iban"] = "TNTESTIBAN0000000000000002",
            ["IrisBankAccounts:Accounts:1:SwiftBic"] = "TESTTN02",
            ["IrisBankAccounts:Accounts:1:IsActive"] = "true",
            ["IrisBankAccounts:Accounts:2:BankName"] = "Test Bank USD",
            ["IrisBankAccounts:Accounts:2:Currency"] = "USD",
            ["IrisBankAccounts:Accounts:2:Iban"] = "TNTESTIBAN0000000000000003",
            ["IrisBankAccounts:Accounts:2:SwiftBic"] = "TESTTN03",
            ["IrisBankAccounts:Accounts:2:IsActive"] = "true"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(accountValues).Build();
        var accountsOptions = configuration.GetSection("IrisBankAccounts").Get<IrisBankAccountsOptions>()!;
        Assert.Equal(3, accountsOptions.Accounts.Count);

        var result = await Service(_db, branding: new IrisInvoiceBrandingOptions
        {
            CompanyName = "IRIS Test",
            AddressLine1 = "Test address line 1",
            AddressLine2 = "Test address, Tunis",
            Phone = "Test telephone",
            TaxIdentifier = "Test fiscal id",
            CommercialRegister = "Test registry"
        }, bankAccounts: accountsOptions, configuration: configuration).GetPremiumInvoiceDocumentAsync(_invoice.IdInvoice, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal($"Facture_{_invoice.InvoiceNumber}.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
        var pdf = System.Text.Encoding.Latin1.GetString(result.Value.Content);
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("230 0 0 84 48 731 cm /Logo Do", pdf);
        Assert.Contains("FACTURE", pdf);
        Assert.Contains("Tunis, le", pdf);
        Assert.Contains("IRIS Test", pdf);
        Assert.Contains("Test telephone", pdf);
        Assert.Contains("Test fiscal id", pdf);
        Assert.Contains("Test registry", pdf);
        Assert.Contains("Cedent", pdf);
        Assert.Contains("Test address, Tunis", pdf);
        Assert.Contains("PRIME NETTE", pdf);
        Assert.Contains("Prime TTC", pdf);
        Assert.Contains("NOS COMPTES BANCAIRES", pdf);
        Assert.Contains("Test Bank TND", pdf);
        Assert.Contains("Test Bank EUR", pdf);
        Assert.Contains("Test Bank USD", pdf);
        Assert.Contains("TNTESTIBAN0000000000000001", pdf);
        Assert.Contains("TNTESTIBAN0000000000000002", pdf);
        Assert.Contains("TNTESTIBAN0000000000000003", pdf);
        Assert.Contains("TESTTN01", pdf);
        Assert.Contains("TESTTN02", pdf);
        Assert.Contains("TESTTN03", pdf);
        Assert.DoesNotContain("DÉTAILS DE LA FACTURE", pdf);
        Assert.DoesNotContain("Reste à facturer", pdf);
    }

    [Fact]
    public async Task BackOfficerInvoiceDownloadRequiresAnActiveIrisBankAccount()
    {
        var result = await Service(_db).GetPremiumInvoiceDocumentAsync(_invoice.IdInvoice, default);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("IRIS_BANK_ACCOUNT_NOT_CONFIGURED", result.Error!.Code);
    }

    [Fact]
    public async Task FullPairingSplitsPanelsAndExactRetryCreatesNoDuplicates()
    {
        var request = new InvoiceReceiptAllocationRequestDto { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 1000m }] };
        var first = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, request, default);
        var retry = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, request, default);
        Assert.Equal(201, first.StatusCode);
        Assert.Equal(200, retry.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(first.Value), JsonSerializer.Serialize(retry.Value));
        Assert.Equal(1, await _db.ReceiptAllocations.CountAsync(x => x.IsActive));
        Assert.Equal(1, await _db.InvoicePairingOperations.CountAsync());
        var lines = await _db.RemittanceRefs.OrderBy(x => x.IdPanel).ToListAsync();
        Assert.Equal(new[] { 600m, 400m }, lines.Select(x => x.NetPayableAmount!.Value));
        Assert.All(lines, x => Assert.NotNull(x.IdReceiptAllocation));
        Assert.Equal(1000m, lines.Sum(x => x.NetPayableAmount));
    }

    [Fact]
    public async Task FullPairingGeneratesEarlierPartialAllocationsToo()
    {
        var second = new Invoice { IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = "INV-002", InvoiceType = "CEDENT_PREMIUM_INVOICE", GrossPremium = 600m, NetPremium = 600m, CreatedBy = "back-user", IdUser = "back-user", Status = "Issued" };
        _invoice.GrossPremium = 400m;
        _invoice.NetPremium = 400m;
        _db.Invoices.Add(second);
        await _db.SaveChangesAsync();
        var service = Service(_db);
        Assert.True((await service.AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 400m }] }, default)).Succeeded);
        Assert.Empty(await _db.RemittanceRefs.ToListAsync());
        Assert.True((await service.AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, new() { Allocations = [new() { InvoiceId = second.IdInvoice, AllocatedAmount = 600m }] }, default)).Succeeded);
        Assert.Equal(4, await _db.RemittanceRefs.CountAsync());
        Assert.Equal(1000m, await _db.RemittanceRefs.SumAsync(x => x.NetPayableAmount));
    }

    [Fact]
    public async Task SavedInvoiceDistributionIsUsed()
    {
        _receipt.AmountReceived = 100m;
        foreach (var (panel, ratio) in new[] { (_panels[0], 0.3m), (_panels[1], 0.7m) })
            _db.InvoicePanelAllocations.Add(new() { IdInvoice = _invoice.IdInvoice, IdPanel = panel.IdPanel, IdCounterparty = panel.IdCounterparty, AllocationRatio = ratio, NetAmount = 1000m * ratio });
        await _db.SaveChangesAsync();
        await AddAllocation(100m);
        var result = await Service(_db).GenerateReceiptRemittancesAsync(_receipt.IdBankReceipt, default);
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new[] { 30m, 70m }, await _db.RemittanceRefs.OrderBy(x => x.IdPanel).Select(x => x.NetPayableAmount!.Value).ToArrayAsync());
        Assert.All(await _db.RemittanceRefs.ToListAsync(), x => Assert.NotNull(x.IdInvoiceAllocation));
    }

    [Fact]
    public async Task ConcurrentRetriesProduceOneLinePerOriginPanel()
    {
        await AddAllocation(1000m);
        await using var first = Context();
        await using var second = Context();
        var results = await Task.WhenAll(Service(first).GenerateReceiptRemittancesAsync(_receipt.IdBankReceipt, default),
            Service(second).GenerateReceiptRemittancesAsync(_receipt.IdBankReceipt, default));
        Assert.All(results, r => Assert.True(r.Succeeded, r.Error?.Message));
        Assert.Equal(2, await _db.RemittanceRefs.CountAsync());
    }

    [Fact]
    public async Task UniqueConstraintRejectsDuplicateOriginPanel()
    {
        await Generate();
        var original = await _db.RemittanceRefs.FirstAsync();
        _db.RemittanceRefs.Add(new RemittanceRef { IdReceiptAllocation = original.IdReceiptAllocation, IdPanel = original.IdPanel, IdCounterparty = original.IdCounterparty, IdCurrency = original.IdCurrency, CreatedBy = "back-user", SourceType = "Premium", Status = "Draft" });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingRejectsMixedReinsurersOrCurrencies(bool mixedCurrency)
    {
        if (mixedCurrency) { _panels[1].IdCounterparty = _panels[0].IdCounterparty; await _db.SaveChangesAsync(); }
        await Generate();
        var lines = await _db.RemittanceRefs.OrderBy(x => x.IdPanel).ToListAsync();
        if (mixedCurrency)
        {
            var eur = new Currency { CurrencyCode = "EUR", Name = "Euro", DecimalPlaces = 2, IsActive = true };
            _db.Currencies.Add(eur);
            await _db.SaveChangesAsync();
            lines[1].IdCurrency = eur.IdCurrency;
            await _db.SaveChangesAsync();
        }
        var result = await Service(_db).CreateOutgoingClosingAsync(new() { RemittanceIds = lines.Select(x => x.RemittanceRef1).ToArray() }, default);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains(mixedCurrency ? "same currency" : "same reinsurer", result.Error!.Message);
        Assert.Empty(await _db.OutgoingClosings.ToListAsync());
        Assert.All(lines, x => Assert.Null(x.ClosingIssuedDate));
    }

    [Fact]
    public async Task ClosingPersistsLinesNotifiesFinanceAndRegeneratesSamePdf()
    {
        _panels[1].IdCounterparty = _panels[0].IdCounterparty;
        await _db.SaveChangesAsync();
        await Generate();
        var ids = await _db.RemittanceRefs.Select(x => x.RemittanceRef1).ToArrayAsync();
        var service = Service(_db);
        var printed = await service.CreateOutgoingClosingAsync(new() { RemittanceIds = ids }, default);
        Assert.True(printed.Succeeded, printed.Error?.Message);
        var closing = await _db.OutgoingClosings.Include(x => x.Lines).SingleAsync();
        Assert.Equal(2, closing.Lines.Count);
        Assert.Equal(1000m, closing.TotalAmount);
        Assert.Equal("application/pdf", printed.Value!.ContentType);
        var text = Encoding.Latin1.GetString(printed.Value.Content);
        Assert.StartsWith("%PDF-", text);
        foreach (var expected in new[] { "Factory protection", "UMR-TEST-2026", "INV-001", "ENC-001", "TN5900000000000000000000", "ABCDEF12", "one thousand TND", "Authorized signature" }) Assert.Contains(expected, text);
        var notification = Assert.Single(await _db.Notifications.Where(x => x.RecipientUserId == "finance-user").ToListAsync());
        Assert.Equal("CLOSING_DISBURSEMENT_REQUIRED", notification.NotificationType);
        Assert.Equal("OUTGOING_CLOSING", notification.EntityType);
        Assert.Equal(closing.Id, notification.ClosingId);
        Assert.Equal(closing.Id, notification.EntityId);
        Assert.EndsWith($"?closingId={closing.Id}", notification.TargetRoute);
        Assert.Equal("AwaitingFinanceDisbursement", closing.Status);
        Assert.All(await _db.RemittanceRefs.ToListAsync(), x => { Assert.Equal("AwaitingFinanceDisbursement", x.Status); Assert.NotNull(x.ClosingIssuedDate); });
        var duplicate = await service.CreateOutgoingClosingAsync(new() { RemittanceIds = ids }, default);
        Assert.Equal(400, duplicate.StatusCode);
        var download = await Service(_db, "Finance Officer").GetOutgoingClosingDocumentAsync(closing.Id, default);
        Assert.Equal(printed.Value.Content, download.Value!.Content);
        Assert.Equal(printed.Value.FileName, download.Value.FileName);
    }

    [Fact]
    public async Task ListSupportsHumanSearchFilteringAndPagination()
    {
        await Generate();
        var result = await Service(_db).ListRemittancesAsync(new RemittanceQueryDto { Search = "ENC-001", Reinsurer = "AMI", Currency = "TND", Contract = "Factory", PageSize = 1, SortBy = "netPayableAmount", SortDirection = "desc" }, default);
        Assert.True(result.Succeeded, result.Error?.Message);
        var line = Assert.Single(result.Value!.Items);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal("INV-001", line.InvoiceNumber);
        Assert.Equal("AMI-PANEL", line.PanelName);
        Assert.True(line.ClosingEligible);
        Assert.Equal(600m, line.NetPayableAmount);
    }

    [Fact]
    public async Task InsufficientPayableRollsBackPairingAndRemittances()
    {
        _panels[0].NetPremium = 100m;
        await _db.SaveChangesAsync();
        var result = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 1000m }] }, default);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("OUTGOING_REMITTANCE_INVALID", result.Error!.Code);
        Assert.Empty(await _db.ReceiptAllocations.ToListAsync());
        Assert.Empty(await _db.RemittanceRefs.ToListAsync());
        Assert.Empty(await _db.InvoicePairingOperations.ToListAsync());
        Assert.Equal("Unpaired", (await _db.BankReceipts.AsNoTracking().SingleAsync()).DetailsStatus);
        Assert.Equal("Unsettled", (await _db.Invoices.AsNoTracking().SingleAsync()).RecoveryStatus);
    }

    [Fact]
    public async Task InactiveAllocationsDoNotMakeReceiptFullyPaired()
    {
        await AddAllocation(1000m, false);
        var result = await Service(_db).GenerateReceiptRemittancesAsync(_receipt.IdBankReceipt, default);
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ConcurrentClosingsReserveSelectionOnlyOnce()
    {
        _panels[1].IdCounterparty = _panels[0].IdCounterparty;
        await _db.SaveChangesAsync();
        await Generate();
        var request = new OutgoingClosingRequestDto { RemittanceIds = await _db.RemittanceRefs.Select(x => x.RemittanceRef1).ToArrayAsync() };
        await using var first = Context();
        await using var second = Context();
        var results = await Task.WhenAll(Service(first).CreateOutgoingClosingAsync(request, default),
            Service(second).CreateOutgoingClosingAsync(request, default));
        Assert.Single(results, x => x.Succeeded);
        Assert.Single(results, x => x.StatusCode == 400);
        Assert.Equal(1, await _db.OutgoingClosings.CountAsync());
        Assert.Equal(2, await _db.OutgoingClosingLines.CountAsync());
    }

    [Theory]
    [InlineData("Inactive")]
    [InlineData("Paid")]
    [InlineData("Zero")]
    [InlineData("WrongContract")]
    public async Task ClosingRejectsIneligibleLine(string reason)
    {
        await Generate();
        var line = await _db.RemittanceRefs.FirstAsync();
        if (reason == "Inactive") line.IsActive = false;
        if (reason == "Paid") line.Status = "Closed";
        if (reason == "Zero") line.NetPayableAmount = 0m;
        if (reason == "WrongContract")
        {
            var contract = new Contract { Name = "Other contract", Umr = "OTHER-UMR", Status = "Bound", TypeOfBusiness = "Facultative", IdCounterparty = _receipt.IdCounterparty, IdCurrency = _receipt.IdCurrency, IdUser = "back-user", CreatedBy = "back-user" };
            _db.Contracts.Add(contract);
            await _db.SaveChangesAsync();
            _panels[0].IdContract = contract.IdContract;
        }
        await _db.SaveChangesAsync();
        var result = await Service(_db).CreateOutgoingClosingAsync(new() { RemittanceIds = [line.RemittanceRef1] }, default);
        Assert.Equal(400, result.StatusCode);
        Assert.Empty(await _db.OutgoingClosings.ToListAsync());
    }

    [Fact]
    public async Task FinanceCanListAndDownloadButCannotIssueBackOfficeClosing()
    {
        await Generate();
        var service = Service(_db, "Finance Officer");
        var list = await service.ListRemittancesAsync(new RemittanceQueryDto(), default);
        Assert.True(list.Succeeded);
        Assert.Equal(2, list.Value!.Items.Count());
        var result = await service.CreateOutgoingClosingAsync(new() { RemittanceIds = [list.Value.Items.First().RemittanceId] }, default);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task LongClosingUsesMultiplePagesAndKeepsFinalSignatures()
    {
        await Generate();
        var origin = await _db.ReceiptAllocations.SingleAsync();
        var lines = new List<RemittanceRef>();
        for (var i = 0; i < 12; i++)
        {
            var panel = new ContractPanel { IdContract = _invoice.IdContract, IdCounterparty = _panels[0].IdCounterparty, Role = "Reinsurer", Status = "Confirmed", NetPremium = 10m, PremiumReference = $"EXTRA-PANEL-{i}" };
            var line = new RemittanceRef { IdPanelNavigation = panel, IdReceiptAllocation = origin.IdReceiptAllocation, IdCurrency = _receipt.IdCurrency, IdCounterparty = panel.IdCounterparty, SourceType = "Premium", Status = "Draft", NetPayableAmount = 1m, CreatedBy = "back-user" };
            _db.RemittanceRefs.Add(line);
            lines.Add(line);
        }
        await _db.SaveChangesAsync();
        var result = await Service(_db).CreateOutgoingClosingAsync(new() { RemittanceIds = lines.Select(x => x.RemittanceRef1).ToArray() }, default);
        Assert.True(result.Succeeded, result.Error?.Message);
        var pdf = Encoding.Latin1.GetString(result.Value!.Content);
        Assert.Contains("/Count 2", pdf);
        Assert.Contains("EXTRA-PANEL-11", pdf);
        Assert.Contains("Bank stamp", pdf);
    }

    [Fact]
    public async Task GrossReceiptPairingUsesPersistedNetPanelPayables()
    {
        _receipt.AmountReceived = 50m;
        _invoice.GrossPremium = 50m;
        _invoice.NetPremium = 42.5m;
        foreach (var panel in _panels)
            _db.InvoicePanelAllocations.Add(new() { IdInvoice = _invoice.IdInvoice, IdPanel = panel.IdPanel,
                IdCounterparty = panel.IdCounterparty, AllocationRatio = 0.5m, GrossPremium = 25m, NetAmount = 21.25m });
        await _db.SaveChangesAsync();
        var result = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 50m }] }, default);
        Assert.Equal(201, result.StatusCode);
        var lines = await _db.RemittanceRefs.OrderBy(x => x.IdPanel).ToListAsync();
        Assert.Equal(new[] { 21.25m, 21.25m }, lines.Select(x => x.NetPayableAmount!.Value));
        Assert.Equal(50m, lines.Sum(x => x.PremiumSignedLine));
        Assert.Equal(7.5m, lines.Sum(x => x.Deductions));
        Assert.Equal(50m, await _db.ReceiptAllocations.SumAsync(x => x.AllocatedAmount));
    }

    [Fact]
    public async Task InactiveAllocationsArePreservedAndExcludedFromPairingBalances()
    {
        await AddAllocation(1000m, false);
        var eligible = await Service(_db).GetEligibleInvoicesAsync(_receipt.IdBankReceipt, null, default);
        Assert.Equal(1000m, Assert.Single(eligible.Value!).RemainingAmount);
        var result = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 1000m }] }, default);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal(2, await _db.ReceiptAllocations.CountAsync());
        Assert.Equal(1, await _db.ReceiptAllocations.CountAsync(x => x.IsActive));
        Assert.Equal(1, await _db.ReceiptAllocations.CountAsync(x => !x.IsActive));
    }

    [Fact]
    public async Task PairingReadsCurrentDatabaseAmountsInsteadOfTrackedValues()
    {
        _receipt.AmountReceived = 100m;
        _invoice.GrossPremium = 100m;
        _invoice.NetPremium = 100m;
        await _db.SaveChangesAsync();
        await using (var other = Context())
        {
            await other.BankReceipts.Where(x => x.IdBankReceipt == _receipt.IdBankReceipt).ExecuteUpdateAsync(s => s.SetProperty(x => x.AmountReceived, 1000m));
            await other.Invoices.Where(x => x.IdInvoice == _invoice.IdInvoice).ExecuteUpdateAsync(s => s.SetProperty(x => x.GrossPremium, 1000m).SetProperty(x => x.NetPremium, 1000m));
        }
        var result = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 1000m }] }, default);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal(1000m, await _db.RemittanceRefs.SumAsync(x => x.NetPayableAmount));
    }

    [Fact]
    public async Task AdditionalValidPairingAndOldRetryKeepOneAllocationAndStableResults()
    {
        var firstRequest = new InvoiceReceiptAllocationRequestDto { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 300m }] };
        var first = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, firstRequest, default);
        var second = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 700m }] }, default);
        var oldRetry = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, firstRequest, default);
        Assert.Equal(201, first.StatusCode);
        Assert.Equal(201, second.StatusCode);
        Assert.Equal(200, oldRetry.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(first.Value), JsonSerializer.Serialize(oldRetry.Value));
        Assert.Equal(1, await _db.ReceiptAllocations.CountAsync());
        Assert.Equal(1000m, await _db.ReceiptAllocations.SumAsync(x => x.AllocatedAmount));
        Assert.Equal(2, await _db.InvoicePairingOperations.CountAsync());
        Assert.Equal(2, await _db.RemittanceRefs.CountAsync());
    }

    [Fact]
    public async Task ConcurrentIdenticalPairingCreatesOnlyOneAllocation()
    {
        var request = new InvoiceReceiptAllocationRequestDto { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 1000m }] };
        await using var first = Context();
        await using var second = Context();
        var results = await Task.WhenAll(Service(first).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, request, default),
            Service(second).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, request, default));
        Assert.Equal(new[] { 200, 201 }, results.Select(x => x.StatusCode).OrderBy(x => x));
        Assert.Equal(1, await _db.ReceiptAllocations.CountAsync());
        Assert.Equal(2, await _db.RemittanceRefs.CountAsync());
    }

    [Theory]
    [InlineData("ALLOCATION_EXCEEDS_RECEIPT_REMAINING")]
    [InlineData("ALLOCATION_EXCEEDS_INVOICE_REMAINING")]
    [InlineData("RECEIPT_ALREADY_FULLY_ALLOCATED")]
    [InlineData("INVOICE_ALREADY_SETTLED")]
    [InlineData("CURRENCY_MISMATCH")]
    public async Task GenuineConflictsReturnExactCodeAndLogBalances(string code)
    {
        var requestAmount = 1500m;
        if (code == "ALLOCATION_EXCEEDS_INVOICE_REMAINING") _receipt.AmountReceived = 2000m;
        if (code == "RECEIPT_ALREADY_FULLY_ALLOCATED") { await AddAllocation(1000m); requestAmount = 10m; }
        if (code == "INVOICE_ALREADY_SETTLED") { _invoice.GrossPremium = 0m; requestAmount = 10m; }
        if (code == "CURRENCY_MISMATCH")
        {
            var eur = new Currency { CurrencyCode = "EUR", Name = "Euro", IsActive = true, DecimalPlaces = 2 };
            _db.Currencies.Add(eur);
            await _db.SaveChangesAsync();
            _invoice.IdCurrency = eur.IdCurrency;
            requestAmount = 10m;
        }
        await _db.SaveChangesAsync();
        var logger = new PairingLogger();
        var result = await Service(_db, logger: logger).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = requestAmount }] }, default);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(code, result.Error!.Code);
        var log = Assert.Single(logger.Entries, x => Equals(x.GetValueOrDefault("ConflictCode"), code));
        Assert.Equal(_receipt.IdBankReceipt, log["ReceiptId"]);
        Assert.Equal(_invoice.IdInvoice, log["InvoiceId"]);
        Assert.Equal(requestAmount, log["RequestedAmount"]);
        Assert.NotNull(log["ReceiptRemainingAmount"]);
        Assert.NotNull(log["InvoiceRemainingAmount"]);
        Assert.Empty(await _db.InvoicePairingOperations.ToListAsync());
    }

    [Fact]
    public async Task RemittanceInsertFailureRollsBackAllocationStatusesAndRequestRecord()
    {
        await using var failing = Context(new RemittanceInsertFailure());
        var logger = new PairingLogger();
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => Service(failing, logger: logger).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 1000m }] }, default));
        Assert.NotNull(exception.InnerException);
        Assert.Empty(await _db.ReceiptAllocations.ToListAsync());
        Assert.Empty(await _db.RemittanceRefs.ToListAsync());
        Assert.Empty(await _db.InvoicePairingOperations.ToListAsync());
        Assert.Equal("Unpaired", (await _db.BankReceipts.AsNoTracking().SingleAsync()).DetailsStatus);
        Assert.Equal("Unsettled", (await _db.Invoices.AsNoTracking().SingleAsync()).RecoveryStatus);
        Assert.Contains(logger.Entries, x => Equals(x.GetValueOrDefault("ConflictCode"), "PAIRING_TRANSACTION_FAILED"));
    }

    private sealed class RemittanceInsertFailure : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[REMITTANCE_REF]", StringComparison.Ordinal)
                && (command.CommandText.Contains("INSERT ", StringComparison.Ordinal) || command.CommandText.Contains("MERGE ", StringComparison.Ordinal)))
                throw new InvalidOperationException("Injected remittance persistence failure", new IOException("Injected storage error"));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class PairingLogger : ILogger<FinancialWorkflowService>
    {
        public List<Dictionary<string, object?>> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values) Entries.Add(values.ToDictionary(x => x.Key, x => x.Value));
        }
    }

    [Fact]
    public async Task FinanceClosingContextReturnsEveryPersistedLineAndPdf()
    {
        var closing = await CreateBatchClosing();
        var finance = Service(_db, "Finance Officer");
        var result = await finance.GetClosingDisbursementContextAsync(closing.Id, default);
        Assert.True(result.Succeeded, result.Error?.Message);
        var context = result.Value!;
        Assert.Equal(closing.Id, context.ClosingId);
        Assert.Equal(closing.Reference, context.ClosingReference);
        Assert.Equal(closing.IssuedAt, context.ClosingDate);
        Assert.Equal(1000m, context.TotalClosingAmount);
        Assert.Equal(2, context.Lines.Count);
        Assert.Equal(new[] { 600m, 400m }, context.Lines.Select(x => x.NetPayableAmount!.Value));
        Assert.All(context.Lines, x => { Assert.Equal("Factory protection", x.ContractName); Assert.Equal("UMR-TEST-2026", x.Umr);
            Assert.Equal("INV-001", x.InvoiceNumber); Assert.Equal("ENC-001", x.ReceiptReference); Assert.NotNull(x.PanelName); });
        Assert.Equal("AMI", context.ReinsurerName);
        Assert.Equal("Tunisia", context.ReinsurerCountry);
        Assert.Equal("TND", context.CurrencyCode);
        Assert.Single(context.BankAccounts);
        Assert.NotNull(context.SelectedBankAccount);
        Assert.True(context.CanStartPayment);
        Assert.True(context.CanDownloadPdf);
        var controller = new IRISBackend.Controllers.FinanceController(finance);
        var file = Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(await controller.ClosingPdf(closing.Id, default));
        Assert.Equal(closing.PdfContent, file.FileContents);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal($"IRIS-{closing.Reference}.pdf", file.FileDownloadName);
        foreach (var line in context.Lines)
        {
            var legacy = await finance.GetDisbursementContextAsync(line.RemittanceRef, default);
            Assert.Equal(closing.Id, legacy.Value!.ClosingId);
            Assert.False(legacy.Value.CanStartPayment);
            Assert.False(legacy.Value.CanConfirmTransfer);
            Assert.Equal("CLOSING_BATCH_REQUIRED", (await finance.StartDisbursementAsync(line.RemittanceRef, default)).Error!.Code);
        }
        Assert.All((await finance.ListDisbursementQueueAsync(default)).Value!, x => Assert.Equal(closing.Id, x.ClosingId));
    }

    [Theory]
    [InlineData("AwaitingFinanceDisbursement")]
    [InlineData("PaymentInProgress")]
    [InlineData("Closed")]
    [InlineData("PartiallyDisbursed")]
    public async Task FinanceClosingContextDoesNotHideExistingClosingByStatus(string status)
    {
        var closing = await CreateBatchClosing();
        closing.Status = status;
        await _db.SaveChangesAsync();
        var result = await Service(_db, "Finance Officer").GetClosingDisbursementContextAsync(closing.Id, default);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(status, result.Value!.Status);
        Assert.Equal(2, result.Value.Lines.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99999)]
    public async Task FinanceClosingInvalidIdReturnsClosingNotFound(int id)
    {
        var finance = Service(_db, "Finance Officer");
        var result = await finance.GetClosingDisbursementContextAsync(id, default);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("Closing was not found.", result.Error!.Message);
        Assert.Equal(404, (await finance.GetFinanceClosingPdfAsync(id, default)).StatusCode);
    }

    [Theory]
    [InlineData("Back Officer")]
    [InlineData("Front Officer")]
    public async Task OnlyFinanceCanAccessClosingContextPdfAndPayment(string position)
    {
        var closing = await CreateBatchClosing();
        var service = Service(_db, position);
        Assert.Equal(403, (await service.GetClosingDisbursementContextAsync(closing.Id, default)).StatusCode);
        Assert.Equal(403, (await service.GetFinanceClosingPdfAsync(closing.Id, default)).StatusCode);
        Assert.Equal(403, (await service.StartClosingDisbursementAsync(closing.Id, default)).StatusCode);
        Assert.Equal(403, (await service.ConfirmClosingTransferAsync(closing.Id, default)).StatusCode);
    }

    [Fact]
    public async Task ArchivedClosingIsInaccessibleAndMissingPdfIsNotMissingClosing()
    {
        var closing = await CreateBatchClosing();
        var finance = Service(_db, "Finance Officer");
        closing.PdfContent = [];
        await _db.SaveChangesAsync();
        Assert.False((await finance.GetClosingDisbursementContextAsync(closing.Id, default)).Value!.CanDownloadPdf);
        Assert.Equal(409, (await finance.GetFinanceClosingPdfAsync(closing.Id, default)).StatusCode);
        closing.IsActive = false;
        await _db.SaveChangesAsync();
        Assert.Equal(404, (await finance.GetClosingDisbursementContextAsync(closing.Id, default)).StatusCode);
        Assert.Equal(404, (await finance.GetFinanceClosingPdfAsync(closing.Id, default)).StatusCode);
    }

    [Fact]
    public async Task ClosingPaymentProcessesAllLinesAndRetriesIdempotently()
    {
        var closing = await CreateBatchClosing();
        var finance = Service(_db, "Finance Officer");
        var started = await finance.StartClosingDisbursementAsync(closing.Id, default);
        Assert.True(started.Succeeded, started.Error?.Message);
        Assert.Equal("PaymentInProgress", started.Value!.Status);
        Assert.True(started.Value.CanConfirmTransfer);
        var retry = await finance.StartClosingDisbursementAsync(closing.Id, default);
        Assert.Equal(started.Value.DisbursementId, retry.Value!.DisbursementId);
        Assert.Single(await _db.Disbursements.ToListAsync());
        Assert.Equal(1000m, (await _db.Disbursements.SingleAsync()).TotalAmount);
        Assert.All(await _db.RemittanceRefs.ToListAsync(), x => Assert.Equal(started.Value.DisbursementId, x.IdDisbursement));
        Assert.True((await _db.Notifications.AsNoTracking().SingleAsync()).IsRead);
        var closed = await finance.ConfirmClosingTransferAsync(closing.Id, default);
        Assert.Equal("Closed", closed.Value!.Status);
        Assert.All(await _db.RemittanceRefs.ToListAsync(), x => Assert.Equal("Closed", x.Status));
        Assert.Equal("Closed", (await finance.ConfirmClosingTransferAsync(closing.Id, default)).Value!.Status);
        var document = await finance.GetDisbursementInvoiceAsync(started.Value.DisbursementId!.Value, default);
        Assert.Equal(closing.PdfContent, document.Value!.Content);
        Assert.Equal(closing.Id, (await finance.GetDisbursementContextAsync(started.Value.Lines[0].RemittanceRef, default)).Value!.ClosingId);
    }

    [Fact]
    public async Task BackfillUsesClosingRelationshipAndPreservesUnrelatedNotification()
    {
        var closing = await CreateBatchClosing();
        var remittance = closing.Lines.First().RemittanceId;
        var legacy = new Notification { RecipientUserId = "finance-user", NotificationType = "DISBURSEMENT_REQUIRED", EntityType = "REMITTANCE_REF",
            EntityId = remittance, Title = "Legacy", Message = "Legacy closing", CreatedAt = DateTime.UtcNow, IsRead = true };
        var unrelated = new Notification { RecipientUserId = "finance-user", NotificationType = "DISBURSEMENT_REQUIRED", EntityType = "REMITTANCE_REF",
            EntityId = 99999, Title = "Other", Message = "Missing old line", CreatedAt = DateTime.UtcNow };
        _db.Notifications.AddRange(legacy, unrelated);
        await _db.SaveChangesAsync();
        await _db.Database.ExecuteSqlRawAsync(IRISBackend.Migrations.FinanceClosingBatch.NotificationBackfillSql);
        await _db.Database.ExecuteSqlRawAsync(IRISBackend.Migrations.FinanceClosingBatch.NotificationBackfillSql);
        var repaired = await _db.Notifications.AsNoTracking().SingleAsync(x => x.IdNotification == legacy.IdNotification);
        Assert.Equal(closing.Id, repaired.ClosingId);
        Assert.Equal(closing.Id, repaired.EntityId);
        Assert.Equal("OUTGOING_CLOSING", repaired.EntityType);
        Assert.EndsWith($"?closingId={closing.Id}", repaired.TargetRoute);
        Assert.True(repaired.IsRead);
        Assert.Null((await _db.Notifications.AsNoTracking().SingleAsync(x => x.IdNotification == unrelated.IdNotification)).ClosingId);
        Assert.Contains((await Service(_db, "Finance Officer").GetNotificationsAsync(false, default)).Value!, x => x.ClosingId == closing.Id);
    }

    private async Task<OutgoingClosing> CreateBatchClosing()
    {
        _panels[1].IdCounterparty = _panels[0].IdCounterparty;
        await _db.SaveChangesAsync();
        await Generate();
        var ids = await _db.RemittanceRefs.Select(x => x.RemittanceRef1).ToArrayAsync();
        var result = await Service(_db).CreateOutgoingClosingAsync(new() { RemittanceIds = ids }, default);
        Assert.True(result.Succeeded, result.Error?.Message);
        return await _db.OutgoingClosings.Include(x => x.Lines).SingleAsync();
    }

    private async Task AddAllocation(decimal amount, bool active = true)
    {
        _db.ReceiptAllocations.Add(new ReceiptAllocation { IdBankReceipt = _receipt.IdBankReceipt, IdInvoice = _invoice.IdInvoice, IdCurrency = _receipt.IdCurrency, IdUser = "back-user", TargetType = "INVOICE", AllocatedAmount = amount, IsActive = active });
        await _db.SaveChangesAsync();
    }

    private async Task Generate()
    {
        await AddAllocation(1000m);
        var result = await Service(_db).GenerateReceiptRemittancesAsync(_receipt.IdBankReceipt, default);
        Assert.True(result.Succeeded, result.Error?.Message);
    }

    private IRISBackendContext Context(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<IRISBackendContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={_database};TrustServerCertificate=True;MultipleActiveResultSets=True;");
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }

    private static FinancialWorkflowService Service(
        IRISBackendContext db,
        string position = "Back Officer",
        ILogger<FinancialWorkflowService>? logger = null,
        IrisInvoiceBrandingOptions? branding = null,
        IrisBankAccountsOptions? bankAccounts = null,
        IConfiguration? configuration = null) => new(db,
        new RequestAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new SecurityClaim(ClaimTypes.NameIdentifier, position == "Back Officer" ? "back-user" : "finance-user"), new SecurityClaim("position", position)], "Test")) } },
        logger ?? NullLogger<FinancialWorkflowService>.Instance,
        Options.Create(branding ?? new IrisInvoiceBrandingOptions()),
        null,
        Options.Create(bankAccounts ?? new IrisBankAccountsOptions()),
        configuration);

    private sealed class RequestAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
