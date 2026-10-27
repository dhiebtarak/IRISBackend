using System.Security.Claims;
using System.Text;
using System.Data.Common;
using System.Text.Json;
using IRISBackend.DTOs;
using IRISBackend.Controllers;
using IRISBackend.Models;
using IRISBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
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
        _db.CounterpartyTypeAssignments.AddRange(new CounterpartyTypeAssignment { CounterpartyId = cedent.Id, Type = "Cedent" }, new CounterpartyTypeAssignment { CounterpartyId = ami.Id, Type = "Reinsurer" }, new CounterpartyTypeAssignment { CounterpartyId = star.Id, Type = "Reinsurer" });
        var account = new CounterpartyBankAccount { IdCounterparty = cedent.Id, IdCurrency = currency.IdCurrency, BankName = "Cedent bank", Iban = "TN5900000000000000000000", SwiftBic = "ABCDEF12", IsActive = true, CreatedBy = user.Id, CreatedAt = DateTime.UtcNow };
        var reinsurerAccount = new CounterpartyBankAccount { IdCounterparty = ami.Id, IdCurrency = currency.IdCurrency, BankName = "AMI bank", IsActive = true, IsDefault = true, Iban = "TN5900000000000000000000", SwiftBic = "ABCDEF12", CreatedBy = user.Id, CreatedAt = DateTime.UtcNow };
        var contract = new Contract { Name = "Factory protection", Umr = "UMR-TEST-2026", TypeOfBusiness = "Facultative", Status = "Bound", IdCounterparty = cedent.Id, IdCurrency = currency.IdCurrency, CreatedBy = user.Id, IdUser = user.Id, CreatedAt = DateTime.UtcNow, InceptionDate = new(2026, 1, 1), ExpiryDate = new(2026, 12, 31), SignedSharePctCedent = 100m };
        _db.AddRange(account, reinsurerAccount, contract);
        await _db.SaveChangesAsync();
        _receipt = new BankReceipt { IdBankAccount = account.IdBankAccount, IdCurrency = currency.IdCurrency, IdCounterparty = cedent.Id, AmountReceived = 1000m, SettlementMethod = "BankTransfer", CollectionType = "Premium", BankReference = "BANK-001", BusinessReference = "ENC-001", CreatedBy = user.Id, CreatedAt = DateTime.UtcNow, DetailsStatus = "Unpaired" };
        _invoice = new Invoice { IdContract = contract.IdContract, IdCurrency = currency.IdCurrency, InvoiceNumber = "INV-001", InvoiceType = "CEDENT_PREMIUM_INVOICE", GrossPremium = 1000m, NetPremium = 1000m, CreatedBy = user.Id, IdUser = user.Id, CreatedAt = DateTime.UtcNow, Status = "Issued", InvoiceDate = new(2026, 10, 8) };
        _panels = [new ContractPanel { IdContract = contract.IdContract, IdCounterparty = ami.Id, Role = "Reinsurer", Status = "Confirmed", NetPremium = 600m, SignedSharePct = 60m, PremiumReference = "AMI-PANEL" },
            new ContractPanel { IdContract = contract.IdContract, IdCounterparty = star.Id, Role = "Reinsurer", Status = "Confirmed", NetPremium = 400m, SignedSharePct = 40m, PremiumReference = "STAR-PANEL" }];
        _db.AddRange(_receipt, _invoice);
        _db.ContractPanels.AddRange(_panels);
        await _db.SaveChangesAsync();
        _db.InvoicePanelAllocations.AddRange(
            new InvoicePanelAllocation { IdInvoice = _invoice.IdInvoice, IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty, ReinsurerNameSnapshot = "AMI", SignedSharePctSnapshot = 60m, AllocationRatio = 0.6m, GrossPremium = 600m, NetAmount = 600m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow },
            new InvoicePanelAllocation { IdInvoice = _invoice.IdInvoice, IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty, ReinsurerNameSnapshot = "STAR Assurances", SignedSharePctSnapshot = 40m, AllocationRatio = 0.4m, GrossPremium = 400m, NetAmount = 400m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task PartialPairingImmediatelyCreatesSavedPanelRemittancesAndPartialReceiptStatus()
    {
        var result = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 500m }] }, default);
        Assert.True(result.Succeeded, result.Error?.Message);
        var lines = await _db.RemittanceRefs.OrderBy(x => x.IdPanel).ToListAsync();
        Assert.Equal(new[] { 300m, 200m }, lines.Select(x => x.NetPayableAmount!.Value));
        Assert.Equal(500m, lines.Sum(x => x.NetPayableAmount!.Value));
        Assert.Equal("PARTIALLY_PAIRED", await _db.BankReceipts.AsNoTracking().Where(x => x.IdBankReceipt == _receipt.IdBankReceipt).Select(x => x.DetailsStatus).SingleAsync());
        Assert.Equal(2, result.Value!.CreatedRemittances.Count);
        Assert.Equal(new[] { 300m, 200m }, result.Value.CreatedRemittances.Select(x => x.Amount!.Value));
        var listed = await Service(_db).ListRemittancesAsync(new RemittanceQueryDto(), default);
        Assert.True(listed.Succeeded, listed.Error?.Message);
        Assert.Equal(2, listed.Value!.Items.Count());
    }

    [Fact]
    public async Task InvoicePairingWithoutSavedConfirmedPanelDistributionRollsBack()
    {
        _db.InvoicePanelAllocations.RemoveRange(await _db.InvoicePanelAllocations.Where(x => x.IdInvoice == _invoice.IdInvoice).ToListAsync());
        await _db.SaveChangesAsync();

        var result = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 500m }] }, default);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("no saved allocation for a confirmed reinsurer", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _db.ReceiptAllocations.ToListAsync());
        Assert.Empty(await _db.RemittanceRefs.ToListAsync());
        Assert.Equal("Unpaired", await _db.BankReceipts.AsNoTracking().Select(x => x.DetailsStatus).SingleAsync());
    }

    [Fact]
    public async Task BackOfficerInvoiceDownloadReturnsPersistedDebitNotePdf()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.CedentCommission = 10m;
        _invoice.GrossPremium = 10m;
        _invoice.NetPremium = 10m;
        _invoice.Label = "Test invoice description";
        var allocations = await _db.InvoicePanelAllocations.Where(item => item.IdInvoice == _invoice.IdInvoice).ToListAsync();
        allocations[0].GrossPremium = 6m;
        allocations[1].GrossPremium = 4m;
        var result = await Service(_db, branding: new IrisInvoiceBrandingOptions
        {
            CompanyName = "IRIS Test",
            AddressLine1 = "Test address line 1",
            AddressLine2 = "Test address, Tunis",
            Phone = "Test telephone",
            TaxIdentifier = "Test fiscal id",
            CommercialRegister = "Test registry"
        }, bankAccounts: TestIrisBankAccounts()).GetPremiumInvoiceDocumentAsync(_invoice.IdInvoice, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal($"Facture_{_invoice.InvoiceNumber}.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
        var pdf = System.Text.Encoding.Latin1.GetString(result.Value.Content);
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("q 180 0 0 120 48 720 cm /Logo Do", pdf);
        Assert.DoesNotContain("INVOICE SUMMARY", pdf);
        Assert.Contains("IRIS Test", pdf);
        Assert.Contains("Test telephone", pdf);
        Assert.Contains("Test fiscal id", pdf);
        Assert.Contains("Test registry", pdf);
        Assert.Contains("Cedent", pdf);
        Assert.Contains("Test address, Tunis", pdf);
        Assert.Contains("FACTURE N", pdf);
        Assert.Contains("Objet", pdf);
        Assert.Contains("Test invoice description", pdf);
        Assert.DoesNotContain("UMR UMR-TEST-2026", pdf);
        Assert.Contains("Nature de la garantie", pdf);
        Assert.Contains("P\u00e9riode", pdf);
        Assert.Contains("Prime nette", pdf);
        Assert.Contains("Prime TTC", pdf);
        Assert.Contains("10,00", pdf);
        Assert.DoesNotContain("IRIS Brokerage", pdf);
        Assert.DoesNotContain("1000.00", pdf);
        Assert.DoesNotContain("Invoice ID", pdf);
        Assert.Contains("Le paiement doit", pdf);
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
        Assert.Contains("RIB-TND", pdf);
        Assert.Contains("IBAN / RIB", pdf);
        Assert.Contains("MF: Test fiscal id", pdf);
        Assert.Contains("RC: Test registry", pdf);
        Assert.DoesNotContain("INVOICE SUMMARY", pdf);
        Assert.DoesNotContain("VALUE DUE", pdf);
    }

    [Fact]
    public async Task BackOfficerInvoiceDownloadDoesNotRequireIrisBankAccounts()
    {
        var result = await Service(_db).GetPremiumInvoiceDocumentAsync(_invoice.IdInvoice, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal($"Facture_{_invoice.InvoiceNumber}.pdf", result.Value!.FileName);
        var pdf = Encoding.Latin1.GetString(result.Value.Content);
        Assert.Contains("FACTURE N", pdf);
        Assert.Contains("NOS COMPTES BANCAIRES", pdf);
        Assert.DoesNotContain("INVOICE SUMMARY", pdf);
        Assert.DoesNotContain("Test Bank", pdf);
    }

    [Fact]
    public async Task BackOfficerInvoiceDownloadReturnsStructuredNotFoundForMissingInvoice()
    {
        var result = await Service(_db).GetPremiumInvoiceDocumentAsync(999999, default);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("INVOICE_NOT_FOUND", result.Error!.Code);
    }

    [Fact]
    public async Task ContractFinancialNoteIsGeneratedFromContractWithoutInvoiceSelection()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 500m;
        contract.CedentCommission = 10m;
        contract.NetPremiumCedent = 450m;
        var result = await Service(_db, bankAccounts: TestIrisBankAccounts())
            .GetContractFinancialNoteAsync(contract.IdContract, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal("DEBIT_NOTE", result.Value!.DocumentType);
        Assert.Equal($"DN-{contract.Umr}", result.Value.DocumentReference);
        Assert.Equal($"DebitNote_DN-{contract.Umr}.pdf", result.Value.FileName);
        var pdf = Encoding.Latin1.GetString(result.Value.Content);
        Assert.Contains("Amount Due from Cedent", pdf);
        Assert.Contains("450,00", pdf);
        Assert.Contains("Test Bank TND", pdf);
        Assert.Contains("Test Bank USD", pdf);
        Assert.Contains("Test Bank EUR", pdf);
        Assert.Contains("IRIS Tunisia", pdf);
        Assert.Contains("q 180 0 0 120 48 720 cm /Logo Do Q", pdf);
        Assert.Contains("TNTESTIBAN0000000000000001", pdf);
        Assert.Contains("TNTESTIBAN0000000000000002", pdf);
        Assert.Contains("TNTESTIBAN0000000000000003", pdf);
        Assert.Contains("KINDLY REMIT YOUR PAYMENT TO ONE OF OUR BANK ACCOUNTS", pdf);
        Assert.DoesNotContain("Invoice Business Number", pdf);
    }

    [Fact]
    public async Task FinancialNoteResponseSetsTemplateVersionAndNoCacheHeaders()
    {
        _db.IrisBankAccounts.AddRange(
            new IrisBankAccount { AccountHolder = "IRIS Tunisia", BankName = "Test Bank TND", IdCurrency = _invoice.IdCurrency!.Value, Rib = "RIB-001", Iban = "TNTESTIBAN0000000000000001", BicSwift = "TESTTN01", Domiciliation = "Tunis", DisplayOrder = 1, IsActive = true },
            new IrisBankAccount { AccountHolder = "IRIS Tunisia", BankName = "Test Bank EUR", IdCurrency = _invoice.IdCurrency.Value, Rib = "RIB-002", Iban = "TNTESTIBAN0000000000000002", BicSwift = "TESTTN02", Domiciliation = "Tunis", DisplayOrder = 2, IsActive = true },
            new IrisBankAccount { AccountHolder = "IRIS Tunisia", BankName = "Test Bank USD", IdCurrency = _invoice.IdCurrency.Value, Rib = "RIB-003", Iban = "TNTESTIBAN0000000000000003", BicSwift = "TESTTN03", Domiciliation = "Tunis", DisplayOrder = 3, IsActive = true });
        await _db.SaveChangesAsync();
        var controller = new BackOfficeController(Service(_db, bankAccounts: TestIrisBankAccounts()));
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var response = await controller.PremiumInvoiceDocument(_invoice.IdInvoice, default);

        Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(response);
        Assert.Equal("iris-accounting-v1", httpContext.Response.Headers["X-PDF-Template-Version"]);
        Assert.Equal("no-store, no-cache, must-revalidate", httpContext.Response.Headers.CacheControl);
        Assert.Equal("no-cache", httpContext.Response.Headers.Pragma);
        Assert.Equal("0", httpContext.Response.Headers.Expires);

        httpContext.Response.Headers.Clear();
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 500m;
        contract.CedentCommission = 10m;
        contract.NetPremiumCedent = 450m;
        await _db.SaveChangesAsync();
        var contractRouteResponse = await controller.ContractFinancialNote(_invoice.IdContract, default);
        var contractPdf = Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(contractRouteResponse);
        Assert.Equal("application/pdf", contractPdf.ContentType);
        Assert.Equal("debit-note-v2", httpContext.Response.Headers["X-PDF-Template-Version"]);
        Assert.Equal("DEBIT_NOTE", httpContext.Response.Headers["X-Financial-Note-Type"]);
        Assert.Equal("DN-UMR-TEST-2026", httpContext.Response.Headers["X-Financial-Note-Reference"]);
    }

    [Fact]
    public async Task BackOfficerAvoirPdfUsesPersistedAmountsAndNegativeSignsWithoutBankAccountDependency()
    {
        _invoice.InvoiceType = "AVOIR";
        _invoice.InvoiceNumber = "AV-2026-000001";
        _invoice.GrossPremium = 1000m;
        _invoice.NetPremium = 1000m;
        _invoice.Label = "Test avoir description";
        var result = await Service(_db).GetAvoirCreditNoteDocumentAsync(_invoice.IdInvoice, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal("Avoir_AV-2026-000001.pdf", result.Value!.FileName);
        Assert.Equal("application/pdf", result.Value.ContentType);
        var pdf = System.Text.Encoding.Latin1.GetString(result.Value.Content);
        Assert.Contains("AVOIR N", pdf);
        Assert.Contains("AV-2026-000001", pdf);
        Assert.Contains("Test avoir description", pdf);
        Assert.Contains("Remboursement", pdf);
        Assert.Contains("Total à vous rembourser", pdf);
        Assert.DoesNotContain("Prime nette", pdf);
        Assert.DoesNotContain("Prime TTC", pdf);
        Assert.Contains("1 000,00", pdf);
        Assert.DoesNotContain("-1 000,00", pdf);
        Assert.Contains("481 356 Tm (1 000,00)", pdf);
        Assert.Contains("433 298 Tm (TND     1 000,00)", pdf);
        Assert.Contains("NOS COMPTES BANCAIRES", pdf);
        Assert.DoesNotContain("Test Bank", pdf);
        Assert.DoesNotContain("DEBIT NOTE", pdf);
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
    public async Task OutgoingRemittanceListReturnsLabelsFromLinkedInvoices()
    {
        var invoice8 = new Invoice
        {
            IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency,
            InvoiceNumber = "INV-2026-000008", InvoiceType = "CEDENT_PREMIUM_INVOICE",
            GrossPremium = 100m, NetPremium = 100m, Label = "Cedent premium eight",
            CreatedBy = "back-user", IdUser = "back-user", Status = "Issued", InvoiceDate = new(2026, 10, 8)
        };
        var invoice9 = new Invoice
        {
            IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency,
            InvoiceNumber = "INV-2026-000009", InvoiceType = "CEDENT_PREMIUM_INVOICE",
            GrossPremium = 200m, NetPremium = 200m, Label = "Cedent premium nine",
            CreatedBy = "back-user", IdUser = "back-user", Status = "Issued", InvoiceDate = new(2026, 10, 9)
        };
        _db.Invoices.AddRange(invoice8, invoice9);
        await _db.SaveChangesAsync();

        var allocation8 = new ReceiptAllocation
        {
            IdBankReceipt = _receipt.IdBankReceipt, IdInvoice = invoice8.IdInvoice,
            IdCurrency = _receipt.IdCurrency, IdUser = "back-user", TargetType = "INVOICE",
            AllocatedAmount = 100m, IsActive = true
        };
        var allocation9 = new ReceiptAllocation
        {
            IdBankReceipt = _receipt.IdBankReceipt, IdInvoice = invoice9.IdInvoice,
            IdCurrency = _receipt.IdCurrency, IdUser = "back-user", TargetType = "INVOICE",
            AllocatedAmount = 200m, IsActive = true
        };
        var allocationWithoutLabel = new ReceiptAllocation
        {
            IdBankReceipt = _receipt.IdBankReceipt, IdInvoice = _invoice.IdInvoice,
            IdCurrency = _receipt.IdCurrency, IdUser = "back-user", TargetType = "INVOICE",
            AllocatedAmount = 50m, IsActive = true
        };
        _db.ReceiptAllocations.AddRange(allocation8, allocation9, allocationWithoutLabel);
        await _db.SaveChangesAsync();

        _db.RemittanceRefs.AddRange(
            new RemittanceRef
            {
                IdReceiptAllocation = allocation8.IdReceiptAllocation, IdPanel = _panels[0].IdPanel,
                IdCounterparty = _panels[0].IdCounterparty, IdCurrency = _receipt.IdCurrency,
                SourceType = "Premium", Status = "Draft", SignatureStatus = "Draft",
                NetPayableAmount = 100m, CreatedBy = "back-user", CreatedAt = DateTime.UtcNow, IsActive = true
            },
            new RemittanceRef
            {
                IdReceiptAllocation = allocation9.IdReceiptAllocation, IdPanel = _panels[1].IdPanel,
                IdCounterparty = _panels[1].IdCounterparty, IdCurrency = _receipt.IdCurrency,
                SourceType = "Premium", Status = "Draft", SignatureStatus = "Draft",
                NetPayableAmount = 200m, CreatedBy = "back-user", CreatedAt = DateTime.UtcNow, IsActive = true
            },
            new RemittanceRef
            {
                IdReceiptAllocation = allocationWithoutLabel.IdReceiptAllocation, IdPanel = _panels[0].IdPanel,
                IdCounterparty = _panels[0].IdCounterparty, IdCurrency = _receipt.IdCurrency,
                SourceType = "Premium", Status = "Draft", SignatureStatus = "Draft",
                NetPayableAmount = 50m, CreatedBy = "back-user", CreatedAt = DateTime.UtcNow, IsActive = true
            });
        await _db.SaveChangesAsync();

        var result = await Service(_db).ListRemittancesAsync(new RemittanceQueryDto(), default);

        Assert.True(result.Succeeded, result.Error?.Message);
        var items = result.Value!.Items.Where(item => item.InvoiceNumber is not null)
            .ToDictionary(item => item.InvoiceNumber!, item => item);
        Assert.Equal("Cedent premium eight", items["INV-2026-000008"].InvoiceLabel);
        Assert.Equal("Cedent premium nine", items["INV-2026-000009"].InvoiceLabel);
        Assert.Null(items["INV-001"].InvoiceLabel);
    }

    [Fact]
    public async Task FullPairingGeneratesEarlierPartialAllocationsToo()
    {
        var second = new Invoice { IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = "INV-002", InvoiceType = "CEDENT_PREMIUM_INVOICE", GrossPremium = 600m, NetPremium = 600m, CreatedBy = "back-user", IdUser = "back-user", Status = "Issued" };
        _invoice.GrossPremium = 400m;
        _invoice.NetPremium = 400m;
        _db.Invoices.Add(second);
        await _db.SaveChangesAsync();
        _db.InvoicePanelAllocations.AddRange(
            new InvoicePanelAllocation { IdInvoice = second.IdInvoice, IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty, ReinsurerNameSnapshot = "AMI", SignedSharePctSnapshot = 60m, AllocationRatio = 0.6m, GrossPremium = 360m, NetAmount = 360m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow },
            new InvoicePanelAllocation { IdInvoice = second.IdInvoice, IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty, ReinsurerNameSnapshot = "STAR Assurances", SignedSharePctSnapshot = 40m, AllocationRatio = 0.4m, GrossPremium = 240m, NetAmount = 240m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        var service = Service(_db);
        Assert.True((await service.AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 400m }] }, default)).Succeeded);
        Assert.Equal(2, await _db.RemittanceRefs.CountAsync());
        Assert.True((await service.AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, new() { Allocations = [new() { InvoiceId = second.IdInvoice, AllocatedAmount = 600m }] }, default)).Succeeded);
        Assert.Equal(4, await _db.RemittanceRefs.CountAsync());
        Assert.Equal(1000m, await _db.RemittanceRefs.SumAsync(x => x.NetPayableAmount));
    }

    [Fact]
    public async Task SavedInvoiceDistributionIsUsed()
    {
        _receipt.AmountReceived = 100m;
        _db.InvoicePanelAllocations.RemoveRange(await _db.InvoicePanelAllocations.Where(x => x.IdInvoice == _invoice.IdInvoice).ToListAsync());
        foreach (var (panel, ratio) in new[] { (_panels[0], 0.3m), (_panels[1], 0.7m) })
            _db.InvoicePanelAllocations.Add(new() { IdInvoice = _invoice.IdInvoice, IdPanel = panel.IdPanel, IdCounterparty = panel.IdCounterparty, AllocationRatio = ratio, NetAmount = 1000m * ratio });
        await _db.SaveChangesAsync();
        await AddAllocation(100m);
        var result = await Service(_db).GenerateReceiptRemittancesAsync(_receipt.IdBankReceipt, default);
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new[] { 30m, 70m }, await _db.RemittanceRefs.OrderBy(x => x.IdPanel).Select(x => x.NetPayableAmount!.Value).ToArrayAsync());
        Assert.All(await _db.RemittanceRefs.ToListAsync(), x => Assert.NotNull(x.IdInvoiceAllocation));
        var retry = await Service(_db).GenerateReceiptRemittancesAsync(_receipt.IdBankReceipt, default);
        Assert.True(retry.Succeeded, retry.Error?.Message);
        Assert.Equal(2, await _db.RemittanceRefs.CountAsync());
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
    public async Task PartialEncaissementOriginDoesNotBlockEligibleReinsurerClosing()
    {
        _receipt.AmountReceived = 1500m;
        await _db.SaveChangesAsync();
        await Generate();
        var line = await _db.RemittanceRefs.SingleAsync(x => x.IdPanel == _panels[0].IdPanel);

        var listed = await Service(_db).ListRemittancesAsync(new RemittanceQueryDto(), default);
        Assert.True(listed.Succeeded, listed.Error?.Message);
        Assert.True(Assert.Single(listed.Value!.Items, item => item.RemittanceId == line.RemittanceRef1).ClosingEligible);

        var closing = await Service(_db).CreateOutgoingClosingAsync(new() { RemittanceIds = [line.RemittanceRef1] }, default);
        Assert.True(closing.Succeeded, closing.Error?.Message);
        Assert.Equal(line.NetPayableAmount, (await _db.OutgoingClosings.SingleAsync()).TotalAmount);
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
    public async Task PairingUsesSavedInvoiceAllocationInsteadOfLivePanelPremium()
    {
        _panels[0].NetPremium = 100m;
        await _db.SaveChangesAsync();
        var result = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 1000m }] }, default);
        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new[] { 600m, 400m }, await _db.RemittanceRefs.OrderBy(x => x.IdPanel).Select(x => x.NetPayableAmount!.Value).ToArrayAsync());
        Assert.Equal(1000m, await _db.RemittanceRefs.SumAsync(x => x.NetPayableAmount ?? 0m));
        Assert.Single(await _db.ReceiptAllocations.ToListAsync());
        Assert.Single(await _db.InvoicePairingOperations.ToListAsync());
        Assert.Equal("UNPAIRED", (await _db.BankReceipts.AsNoTracking().SingleAsync()).DetailsStatus);
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
    public async Task RemittanceListExposesPanelBeneficiariesAndFlagsPanelLessLegacyRows()
    {
        await AddAllocation(100m);
        var receiptAllocation = await _db.ReceiptAllocations.SingleAsync();
        var avoir = new Invoice
        {
            IdContract = _invoice.IdContract,
            IdCurrency = _receipt.IdCurrency,
            InvoiceNumber = "AV-2026-000001",
            InvoiceType = "AVOIR",
            InvoiceDate = new(2026, 10, 19),
            GrossPremium = 10m,
            NetPremium = 10m,
            Status = "Issued",
            CreatedBy = "back-user",
            IdUser = "back-user",
            CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoir);
        await _db.SaveChangesAsync();

        var reinsurerRemittance = new RemittanceRef
        {
            IdReceiptAllocation = receiptAllocation.IdReceiptAllocation,
            IdPanel = _panels[0].IdPanel,
            IdCounterparty = _panels[0].IdCounterparty,
            IdCurrency = _receipt.IdCurrency,
            SourceType = "Premium",
            Status = "Draft",
            NetPayableAmount = 6m,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedBy = "back-user",
            Description = "Premium payment"
        };
        var cedentRefund = new RemittanceRef
        {
            IdPanel = null,
            IdCounterparty = _receipt.IdCounterparty,
            IdCurrency = _receipt.IdCurrency,
            SourceType = "AVOIR_REFUND",
            SourceInvoiceId = avoir.IdInvoice,
            BeneficiaryType = "CEDENT",
            Status = "Draft",
            NetPayableAmount = 10m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "back-user",
            Description = "Avoir refund"
        };
        var legacyRemittance = new RemittanceRef
        {
            IdCounterparty = _receipt.IdCounterparty,
            IdCurrency = _receipt.IdCurrency,
            SourceType = "LegacySource",
            Status = "Draft",
            NetPayableAmount = 1m,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            CreatedBy = "back-user",
            Description = "Legacy row without panel or source"
        };
        _db.RemittanceRefs.AddRange(reinsurerRemittance, cedentRefund, legacyRemittance);
        await _db.SaveChangesAsync();

        var logger = new PairingLogger();
        var service = Service(_db, logger: logger);
        var result = await service.ListRemittancesAsync(new RemittanceQueryDto
        {
            PageNumber = 1,
            PageSize = 2,
            SortBy = "createdAt",
            SortDirection = "desc"
        }, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(3, result.Value!.TotalCount);
        Assert.Equal(2, result.Value.Items.Count());

        var reinsurerDto = result.Value.Items.Single(item => item.SourceType == "Premium");
        Assert.Equal("REINSURER", reinsurerDto.BeneficiaryType);
        Assert.Equal(_panels[0].IdCounterparty, reinsurerDto.BeneficiaryId);
        Assert.Equal("AMI", reinsurerDto.BeneficiaryName);
        Assert.Equal(_panels[0].IdPanel, reinsurerDto.PanelId);
        Assert.Equal(_invoice.IdInvoice, reinsurerDto.SourceDocumentId);
        Assert.Equal(_invoice.InvoiceNumber, reinsurerDto.SourceDocumentNumber);
        Assert.Equal(_receipt.IdCounterparty, reinsurerDto.CedentId);
        Assert.Equal("Cedent", reinsurerDto.CedentName);
        Assert.Equal(_panels[0].IdCounterparty, reinsurerDto.ReinsurerId);
        Assert.Equal(6m, reinsurerDto.SignedNetPayableAmount);

        var legacyCedentAggregate = Assert.Single(result.Value.Items, item => item.SourceType == "AVOIR_REFUND");
        Assert.Null(legacyCedentAggregate.PanelId);
        Assert.Null(legacyCedentAggregate.ReinsurerId);
        Assert.Equal("Remittance has no panel and requires data repair.", legacyCedentAggregate.DataIntegrityWarning);

        var secondPage = await service.ListRemittancesAsync(new RemittanceQueryDto
        {
            PageNumber = 2,
            PageSize = 2,
            SortBy = "createdAt",
            SortDirection = "desc"
        }, default);
        Assert.True(secondPage.Succeeded, secondPage.Error?.Message);
        var legacyPanelLess = Assert.Single(secondPage.Value!.Items);
        Assert.Equal("LegacySource", legacyPanelLess.SourceType);
        Assert.Equal("Remittance has no panel and requires data repair.", legacyPanelLess.DataIntegrityWarning);
        Assert.Contains(logger.Entries, entry => Equals(entry.GetValueOrDefault("SourceType"), "LegacySource"));
        var sqlLog = logger.Entries.First(entry => entry.ContainsKey("Sql"));
        var generatedSql = Assert.IsType<string>(sqlLog["Sql"]);
        Assert.Contains("source_invoice_id", generatedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beneficiary_type", generatedSql, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(sqlLog.GetValueOrDefault("TraceId") as string));
    }

    [Fact]
    public async Task RemittanceListValidatesSortingPaginationAndReturnsEmptyItems()
    {
        var service = Service(_db);
        var result = await service.ListRemittancesAsync(new RemittanceQueryDto
        {
            PageNumber = 1,
            PageSize = 20,
            SortBy = "createdAt",
            SortDirection = "desc"
        }, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);

        var invalidSort = await service.ListRemittancesAsync(new RemittanceQueryDto { SortBy = "notAField" }, default);
        Assert.Equal(400, invalidSort.StatusCode);

        var invalidPage = await service.ListRemittancesAsync(new RemittanceQueryDto { PageNumber = 0 }, default);
        Assert.Equal(400, invalidPage.StatusCode);
    }

    [Fact]
    public async Task DirectAvoirPanelRemittanceReturnsSourceNumberAndLabel()
    {
        _invoice.InvoiceType = "AVOIR";
        _invoice.InvoiceNumber = "AV-2026-000011";
        _invoice.Label = "test avoir";
        await _db.SaveChangesAsync();

        var panel = _panels[0];
        _db.RemittanceRefs.Add(new RemittanceRef
        {
            SourceType = "AVOIR",
            SourceInvoiceId = _invoice.IdInvoice,
            IdPanel = panel.IdPanel,
            IdCounterparty = panel.IdCounterparty,
            IdCurrency = _receipt.IdCurrency,
            NetPayableAmount = 2.25m,
            Status = "Draft",
            SignatureStatus = "Draft",
            Description = "Direct Avoir remittance",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "back-user",
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await Service(_db).ListRemittancesAsync(new RemittanceQueryDto(), default);

        Assert.True(result.Succeeded, result.Error?.Message);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("AVOIR", row.SourceType);
        Assert.Equal(_invoice.IdInvoice, row.SourceDocumentId);
        Assert.Equal("AV-2026-000011", row.InvoiceAvoirNumber);
        Assert.Equal("test avoir", row.Label);
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
        _db.InvoicePanelAllocations.RemoveRange(await _db.InvoicePanelAllocations.Where(x => x.IdInvoice == _invoice.IdInvoice).ToListAsync());
        foreach (var panel in _panels)
            _db.InvoicePanelAllocations.Add(new() { IdInvoice = _invoice.IdInvoice, IdPanel = panel.IdPanel,
                IdCounterparty = panel.IdCounterparty, AllocationRatio = 0.5m, GrossPremium = 25m,
                BrokeragePct = 15m, BrokerageAmount = 3.75m, NetAmount = 25m });
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
    public async Task InvoiceNetAmountGeneratesRemittancesFromSavedNetPayables()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 500m;
        contract.CedentCommission = 10m;
        contract.SignedSharePctCedent = 10m;
        _db.ContractFacs.Add(new ContractFac
        {
            IdContract = contract.IdContract,
            SumInsured = 500000m,
            PremiumRate = 0.1m
        });
        foreach (var panel in _panels)
        {
            panel.SignedSharePct = 5m;
            panel.GrossPremium = 25m;
            panel.BrokeragePct = panel.IdCounterparty == _panels[0].IdCounterparty ? 10m : 0m;
            panel.Status = "Confirmed";
        }
        _receipt.AmountReceived = 45m;
        _invoice.Status = "Cancelled";
        await _db.SaveChangesAsync();

        var service = Service(_db);
        var created = await service.CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "INVOICE",
            GrossAmount = 45m,
            InvoiceDate = new(2026, 10, 21)
        }, default);
        Assert.True(created.Succeeded, created.Error?.Message);
        Assert.True(created.Value!.IsReconciled);
        Assert.Equal(0m, created.Value.ReconciliationDifference);
        Assert.Equal(new[] { 25m, 25m }, created.Value!.Allocations.Select(line => line.AllocatedGrossPremium));
        Assert.Equal(new[] { 2.5m, 2.5m }, created.Value.Allocations.Select(line => line.CedentCommissionAmount));
        Assert.Equal(new[] { 22.5m, 22.5m }, created.Value.Allocations.Select(line => line.AllocatedNetPremium));
        Assert.Equal(new[] { 20m, 22.5m }, created.Value.Allocations.Select(line => line.NetPayableAmount));
        Assert.Equal(new[] { 22.5m, 22.5m }, created.Value.Allocations.Select(line => line.NetAmount));

        var paired = await service.AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new InvoiceReceiptAllocationRequestDto
            {
                Allocations = [new InvoiceReceiptAllocationItemDto
                {
                    InvoiceId = created.Value.InvoiceId,
                    AllocatedAmount = 45m
                }]
            }, default);

        Assert.True(paired.Succeeded, paired.Error?.Message);
        var receiptAllocationId = await _db.ReceiptAllocations
            .Where(row => row.IdBankReceipt == _receipt.IdBankReceipt && row.IdInvoice == created.Value.InvoiceId)
            .Select(row => row.IdReceiptAllocation)
            .SingleAsync();
        var remittances = await _db.RemittanceRefs
            .Where(line => line.IdReceiptAllocation == receiptAllocationId)
            .OrderBy(line => line.IdPanel)
            .ToListAsync();
        Assert.Equal(new[] { 20m, 22.5m }, remittances.Select(line => line.NetPayableAmount!.Value));
        Assert.Equal(42.5m, remittances.Sum(line => line.NetPayableAmount!.Value));
        Assert.Equal(45m, remittances.Sum(line => line.PremiumSignedLine!.Value));
        Assert.Equal(2.5m, remittances.Sum(line => line.Deductions!.Value));
        var savedPanelAllocations = await _db.InvoicePanelAllocations
            .Where(line => line.IdInvoice == created.Value.InvoiceId)
            .OrderBy(line => line.IdPanel)
            .ToListAsync();
        Assert.Equal(new[] { 25m, 25m }, savedPanelAllocations.Select(line => line.GrossPremium));
        Assert.Equal(new[] { 22.5m, 22.5m }, savedPanelAllocations.Select(line => line.NetAmount));
    }

    [Fact]
    public async Task InvoicePreviewCreationDetailsHistoryAndRemittancesUseGrossBasedBrokerage()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 55.556m;
        contract.CedentCommission = 10m;
        contract.SignedSharePctCedent = 100m;
        _panels[0].SignedSharePct = 80m;
        _panels[0].GrossPremium = 44.445m;
        _panels[0].BrokeragePct = 10m;
        _panels[0].OtherDeductions = 0.025m;
        _panels[0].Status = "Confirmed";
        _panels[1].SignedSharePct = 20m;
        _panels[1].GrossPremium = 11.111m;
        _panels[1].BrokeragePct = 10m;
        _panels[1].OtherDeductions = 0m;
        _panels[1].Status = "Confirmed";
        _receipt.AmountReceived = 50m;
        _invoice.Status = "Cancelled";
        await _db.SaveChangesAsync();

        var service = Service(_db);
        var request = new PremiumDebitNoteRequestDto
        {
            InvoiceType = "INVOICE",
            GrossAmount = 50m,
            InvoiceDate = new(2026, 10, 23),
            IdempotencyKey = "gross-brokerage-regression"
        };
        var preview = await service.PreviewPremiumInvoiceAsync(contract.IdContract, request, default);
        Assert.True(preview.Succeeded, preview.Error?.Message);
        Assert.Equal(new[] { 44.445m, 11.111m }, preview.Value!.Allocations.Select(line => line.AllocatedGrossPremium));
        Assert.Equal(new[] { 4.445m, 1.111m }, preview.Value.Allocations.Select(line => line.IrisBrokerageAmount));
        Assert.DoesNotContain(preview.Value.Allocations, line => line.IrisBrokerageAmount == 4m || line.IrisBrokerageAmount == 1m);
        Assert.Equal(new[] { 40m, 10m }, preview.Value.Allocations.Select(line => line.AllocatedNetPremium));
        Assert.Equal(new[] { 35.53m, 8.889m }, preview.Value.Allocations.Select(line => line.SignedNetPayableAmount));
        Assert.Equal(50m, preview.Value.Allocations.Sum(line => line.AllocatedNetPremium));

        var created = await service.CreatePremiumInvoiceAsync(contract.IdContract, request, default);
        Assert.True(created.Succeeded, created.Error?.Message);
        Assert.Equal(preview.Value.Allocations.Select(line => (line.AllocatedGrossPremium, line.CedentCommissionAmount,
                line.AllocatedNetPremium, line.IrisBrokerageAmount, line.NetPayableAmount)),
            created.Value!.Allocations.Select(line => (line.AllocatedGrossPremium, line.CedentCommissionAmount,
                line.AllocatedNetPremium, line.IrisBrokerageAmount, line.NetPayableAmount)));

        var persisted = await _db.InvoicePanelAllocations.AsNoTracking()
            .Where(line => line.IdInvoice == created.Value.InvoiceId)
            .OrderBy(line => line.IdPanel).ToListAsync();
        Assert.Equal(new[] { 4.445m, 1.111m }, persisted.Select(line => line.BrokerageAmount));
        Assert.Equal(new[] { 40m, 10m }, persisted.Select(line => line.NetAmount));

        var details = await service.GetInvoiceAsync(created.Value.InvoiceId, default);
        Assert.True(details.Succeeded, details.Error?.Message);
        Assert.Equal(new[] { 4.445m, 1.111m }, details.Value!.Allocations.Select(line => line.IrisBrokerageAmount));
        var history = await service.ListInvoicesAsync(new InvoiceQueryDto { ContractId = contract.IdContract }, default);
        Assert.True(history.Succeeded, history.Error?.Message);
        var historyInvoice = Assert.Single(history.Value!.Items, item => item.InvoiceId == created.Value.InvoiceId);
        Assert.Equal(new[] { 4.445m, 1.111m }, historyInvoice.Allocations.Select(line => line.IrisBrokerageAmount));

        var paired = await service.AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = created.Value.InvoiceId, AllocatedAmount = 50m }] }, default);
        Assert.True(paired.Succeeded, paired.Error?.Message);
        var remittancePayables = await _db.RemittanceRefs.AsNoTracking()
            .Where(row => row.SourceInvoiceId == created.Value.InvoiceId)
            .OrderBy(row => row.IdPanel).Select(row => row.NetPayableAmount!.Value).ToArrayAsync();
        Assert.Equal(new[] { 35.53m, 8.889m }, remittancePayables);
        Assert.Equal(44.419m, remittancePayables.Sum());
    }

    [Fact]
    public async Task DraftBrokerageRepairIsAuditedIdempotentAndLeavesFinalizedInvoiceUnchanged()
    {
        var draft = await _db.Invoices.SingleAsync(row => row.IdInvoice == _invoice.IdInvoice);
        draft.Status = "Draft";
        draft.InvoiceType = "INVOICE";
        _db.InvoicePanelAllocations.RemoveRange(await _db.InvoicePanelAllocations
            .Where(row => row.IdInvoice == draft.IdInvoice).ToListAsync());
        foreach (var panel in _panels)
        {
            _db.InvoicePanelAllocations.Add(new InvoicePanelAllocation
            {
                IdInvoice = draft.IdInvoice,
                IdPanel = panel.IdPanel,
                IdCounterparty = panel.IdCounterparty,
                ReinsurerNameSnapshot = panel.IdCounterparty == _panels[0].IdCounterparty ? "AMI" : "STAR Assurances",
                AllocationRatio = 0.5m,
                GrossPremium = 44.445m,
                CedentCommissionPct = 10m,
                CedentCommissionAmount = 4.445m,
                BrokeragePct = 10m,
                BrokerageAmount = 4m,
                OtherDeductions = 0m,
                NetAmount = 40m,
                CurrencyCodeSnapshot = "TND",
                CreatedAt = DateTime.UtcNow
            });
        }

        var finalized = new Invoice
        {
            IdContract = draft.IdContract,
            IdCurrency = draft.IdCurrency,
            InvoiceNumber = "INV-REPAIR-FINAL",
            InvoiceType = "INVOICE",
            InvoiceDate = new(2026, 10, 22),
            GrossPremium = 44.445m,
            NetPremium = 40m,
            CreatedBy = draft.CreatedBy,
            IdUser = draft.IdUser,
            CreatedAt = DateTime.UtcNow,
            Status = "Closed",
            Label = "Finalized repair exclusion"
        };
        _db.Invoices.Add(finalized);
        await _db.SaveChangesAsync();
        _db.InvoicePanelAllocations.Add(new InvoicePanelAllocation
        {
            IdInvoice = finalized.IdInvoice,
            IdPanel = _panels[0].IdPanel,
            IdCounterparty = _panels[0].IdCounterparty,
            ReinsurerNameSnapshot = "AMI",
            AllocationRatio = 1m,
            GrossPremium = 44.445m,
            CedentCommissionPct = 10m,
            CedentCommissionAmount = 4.445m,
            BrokeragePct = 10m,
            BrokerageAmount = 4m,
            OtherDeductions = 0m,
            NetAmount = 40m,
            CurrencyCodeSnapshot = "TND",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _db.Database.ExecuteSqlRawAsync(IRISBackend.Migrations.CorrectDraftInvoiceBrokerageFromGross.RepairSql);
        await _db.Database.ExecuteSqlRawAsync(IRISBackend.Migrations.CorrectDraftInvoiceBrokerageFromGross.RepairSql);

        var draftAmounts = await _db.InvoicePanelAllocations.AsNoTracking()
            .Where(row => row.IdInvoice == draft.IdInvoice).OrderBy(row => row.IdPanel)
            .Select(row => row.BrokerageAmount).ToArrayAsync();
        Assert.Equal(new[] { 4.445m, 4.445m }, draftAmounts);
        Assert.Equal(2, await _db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM dbo.INVOICE_PANEL_BROKERAGE_RECONCILIATION WHERE id_invoice = {0} AND correction_action = N'CORRECTED_DRAFT'", draft.IdInvoice).SingleAsync());
        Assert.Equal(4m, await _db.InvoicePanelAllocations.AsNoTracking()
            .Where(row => row.IdInvoice == finalized.IdInvoice).Select(row => row.BrokerageAmount).SingleAsync());
        Assert.Equal(1, await _db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM dbo.INVOICE_PANEL_BROKERAGE_RECONCILIATION WHERE id_invoice = {0} AND correction_action = N'RECONCILE_FINALIZED'", finalized.IdInvoice).SingleAsync());
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
        _invoice.GrossPremium = 300m;
        var secondInvoice = new Invoice { IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency,
            InvoiceNumber = "INV-ADDITIONAL", InvoiceType = "CEDENT_PREMIUM_INVOICE", GrossPremium = 700m,
            NetPremium = 700m, CreatedBy = "back-user", IdUser = "back-user", Status = "Issued" };
        _db.Invoices.Add(secondInvoice);
        await _db.SaveChangesAsync();
        _db.InvoicePanelAllocations.AddRange(
            new InvoicePanelAllocation { IdInvoice = secondInvoice.IdInvoice, IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty, ReinsurerNameSnapshot = "AMI", SignedSharePctSnapshot = 60m, AllocationRatio = 0.6m, GrossPremium = 420m, NetAmount = 420m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow },
            new InvoicePanelAllocation { IdInvoice = secondInvoice.IdInvoice, IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty, ReinsurerNameSnapshot = "STAR Assurances", SignedSharePctSnapshot = 40m, AllocationRatio = 0.4m, GrossPremium = 280m, NetAmount = 280m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        var firstRequest = new InvoiceReceiptAllocationRequestDto { Allocations = [new() { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 300m }] };
        var first = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, firstRequest, default);
        var second = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt,
            new() { Allocations = [new() { InvoiceId = secondInvoice.IdInvoice, AllocatedAmount = 700m }] }, default);
        var oldRetry = await Service(_db).AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, firstRequest, default);
        Assert.Equal(201, first.StatusCode);
        Assert.Equal(201, second.StatusCode);
        Assert.Equal(200, oldRetry.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(first.Value), JsonSerializer.Serialize(oldRetry.Value));
        Assert.Equal(2, await _db.ReceiptAllocations.CountAsync());
        Assert.Equal(1000m, await _db.ReceiptAllocations.SumAsync(x => x.AllocatedAmount));
        Assert.Equal(2, await _db.InvoicePairingOperations.CountAsync());
        Assert.Equal(4, await _db.RemittanceRefs.CountAsync());
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
        Assert.Equal("UNPAIRED", (await _db.BankReceipts.AsNoTracking().SingleAsync()).DetailsStatus);
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

    [Fact]
    public async Task MixedPremiumAndDirectAvoirClosingKeepsSignedAmountsAndAppearsOnceInFinanceQueue()
    {
        var avoir = new Invoice
        {
            IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = "AV-TEST-2026-1",
            InvoiceType = "AVOIR", GrossPremium = 0.45m, NetPremium = 0.45m, Status = "Issued",
            InvoiceDate = new(2026, 10, 22), CreatedBy = "back-user", IdUser = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoir);
        await _db.SaveChangesAsync();
        var currencyId = _invoice.IdCurrency!.Value;
        var premium = new RemittanceRef
        {
            SourceType = "PREMIUM", SourceInvoiceId = _invoice.IdInvoice, IdPanel = _panels[0].IdPanel,
            IdCounterparty = _panels[0].IdCounterparty, IdCurrency = currencyId, NetPayableAmount = 200m,
            Status = "Draft", SignatureStatus = "Draft", CreatedBy = "back-user", CreatedAt = DateTime.UtcNow, IsActive = true
        };
        var avoirRemittance = new RemittanceRef
        {
            SourceType = "AVOIR", SourceInvoiceId = avoir.IdInvoice, IdPanel = _panels[0].IdPanel,
            IdCounterparty = _panels[0].IdCounterparty, IdCurrency = currencyId, NetPayableAmount = 0.45m,
            Status = "Draft", SignatureStatus = "Draft", CreatedBy = "back-user", CreatedAt = DateTime.UtcNow, IsActive = true
        };
        _db.RemittanceRefs.AddRange(premium, avoirRemittance);
        await _db.SaveChangesAsync();

        var confirmed = await Service(_db).CreateOutgoingClosingAsync(
            new() { RemittanceIds = [premium.RemittanceRef1, avoirRemittance.RemittanceRef1] }, default);
        Assert.True(confirmed.Succeeded, confirmed.Error?.Message);
        var closing = await _db.OutgoingClosings.Include(x => x.Lines).SingleAsync();
        Assert.Equal(199.55m, closing.TotalAmount);
        Assert.Equal(200m, closing.Lines.Single(x => x.RemittanceId == premium.RemittanceRef1).PayableAmount);
        Assert.Equal(-0.45m, closing.Lines.Single(x => x.RemittanceId == avoirRemittance.RemittanceRef1).PayableAmount);
        Assert.Contains("Source document: AVOIR AV-TEST-2026-1", System.Text.Encoding.Latin1.GetString(closing.PdfContent));
        Assert.Contains("Signed amount: -0.450 TND", System.Text.Encoding.Latin1.GetString(closing.PdfContent));

        var finance = Service(_db, "Finance Officer");
        var context = await finance.GetClosingDisbursementContextAsync(closing.Id, default);
        Assert.True(context.Succeeded, context.Error?.Message);
        Assert.Equal(199.55m, context.Value!.StoredTotal);
        Assert.Equal(199.55m, context.Value.CalculatedTotal);
        Assert.Equal(0m, context.Value.Difference);
        Assert.True(context.Value.IsConsistent);
        var avoirLine = Assert.Single(context.Value.Lines, line => line.SourceType == "AVOIR");
        Assert.Equal(-0.45m, avoirLine.SignedNetPayableAmount);
        Assert.Equal(-0.45m, avoirLine.NetPayableAmount);
        Assert.Equal("AV-TEST-2026-1", avoirLine.SourceDocumentNumber);
        Assert.Null(avoirLine.ReceiptReference);
        Assert.Null(avoirLine.BankReceiptId);

        var queue = await finance.ListDisbursementQueueAsync(default);
        Assert.True(queue.Succeeded, queue.Error?.Message);
        var queueRow = Assert.Single(queue.Value!, row => row.ClosingId == closing.Id);
        Assert.Equal(2, queueRow.LineCount);
        Assert.Equal(200m, queueRow.PremiumTotal);
        Assert.Equal(0.45m, queueRow.AvoirTotal);
        Assert.Equal(199.55m, queueRow.SignedNetPayableAmount);
        var started = await finance.StartClosingDisbursementAsync(closing.Id, default);
        Assert.True(started.Succeeded, started.Error?.Message);
        Assert.Equal(199.55m, (await _db.Disbursements.SingleAsync()).TotalAmount);
    }

    [Fact]
    public async Task OpenClosingRepairIsIdempotentAndDoesNotRewriteFinalizedClosing()
    {
        var open = await CreateMixedSourceClosing();
        var finalized = await CreateMixedSourceClosing("Closed");
        foreach (var closing in new[] { open, finalized })
        {
            var avoirLine = closing.Lines.Single(line => line.Remittance.SourceType == "AVOIR");
            avoirLine.PayableAmount = 0.45m;
            closing.TotalAmount = 200.45m;
        }
        await _db.SaveChangesAsync();

        await _db.Database.ExecuteSqlRawAsync(IRISBackend.Migrations.RepairOpenClosingSignedAmounts.RepairSql);
        await _db.Database.ExecuteSqlRawAsync(IRISBackend.Migrations.RepairOpenClosingSignedAmounts.RepairSql);
        await _db.Entry(open).ReloadAsync();
        await _db.Entry(finalized).ReloadAsync();
        var repaired = await _db.OutgoingClosingLines.AsNoTracking()
            .SingleAsync(line => line.ClosingId == open.Id && line.Remittance.SourceType == "AVOIR");
        var untouched = await _db.OutgoingClosingLines.AsNoTracking()
            .SingleAsync(line => line.ClosingId == finalized.Id && line.Remittance.SourceType == "AVOIR");
        Assert.Equal(-0.45m, repaired.PayableAmount);
        Assert.Equal(199.55m, open.TotalAmount);
        Assert.Equal(0.45m, untouched.PayableAmount);
        Assert.Equal(200.45m, finalized.TotalAmount);
    }

    [Fact]
    public async Task InconsistentSignedClosingIsReportedAndCannotStartPayment()
    {
        var closing = await CreateMixedSourceClosing();
        closing.TotalAmount = 200.45m;
        await _db.SaveChangesAsync();

        var finance = Service(_db, "Finance Officer");
        var context = await finance.GetClosingDisbursementContextAsync(closing.Id, default);
        Assert.True(context.Succeeded, context.Error?.Message);
        Assert.Equal(200.45m, context.Value!.StoredTotal);
        Assert.Equal(199.55m, context.Value.CalculatedTotal);
        Assert.Equal(-0.90m, context.Value.Difference);
        Assert.False(context.Value.IsConsistent);
        Assert.Contains("inconsistent", context.Value.ValidationMessage, StringComparison.OrdinalIgnoreCase);

        var started = await finance.StartClosingDisbursementAsync(closing.Id, default);
        Assert.False(started.Succeeded);
        Assert.Equal(409, started.StatusCode);
        Assert.Equal("CLOSING_NOT_PAYABLE", started.Error!.Code);
        Assert.Empty(await _db.Disbursements.ToListAsync());
    }

    [Fact]
    public async Task ClosingLineRoundingUsesCurrencyPrecisionAndReconcilesTheRemainder()
    {
        var closing = await CreateMixedSourceClosing(premiumAmount: 200.0006m, avoirAmount: 0.4504m);
        var lines = await _db.OutgoingClosingLines.AsNoTracking().Where(line => line.ClosingId == closing.Id)
            .OrderBy(line => line.PayableAmount).ToArrayAsync();
        Assert.Equal(new[] { -0.451m, 200.001m }, lines.Select(line => line.PayableAmount));
        Assert.Equal(199.550m, lines.Sum(line => line.PayableAmount));
        Assert.Equal(199.550m, closing.TotalAmount);
        Assert.Equal(3, PanelRemittanceCalculator.Precision((await _db.Currencies.SingleAsync()).DecimalPlaces));
    }

    private async Task<OutgoingClosing> CreateMixedSourceClosing(string status = "AwaitingFinanceDisbursement",
        decimal premiumAmount = 200m, decimal avoirAmount = 0.45m)
    {
        var avoir = new Invoice
        {
            IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = $"AV-TEST-{Guid.NewGuid():N}",
            InvoiceType = "AVOIR", GrossPremium = 0.45m, NetPremium = 0.45m, Status = "Issued",
            InvoiceDate = new(2026, 10, 22), CreatedBy = "back-user", IdUser = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoir);
        await _db.SaveChangesAsync();
        var currencyId = _invoice.IdCurrency!.Value;
        var premium = new RemittanceRef
        {
            SourceType = "PREMIUM", SourceInvoiceId = _invoice.IdInvoice, IdPanel = _panels[0].IdPanel,
            IdCounterparty = _panels[0].IdCounterparty, IdCurrency = currencyId, NetPayableAmount = premiumAmount,
            Status = "Draft", SignatureStatus = "Draft", CreatedBy = "back-user", CreatedAt = DateTime.UtcNow, IsActive = true
        };
        var avoirRemittance = new RemittanceRef
        {
            SourceType = "AVOIR", SourceInvoiceId = avoir.IdInvoice, IdPanel = _panels[0].IdPanel,
            IdCounterparty = _panels[0].IdCounterparty, IdCurrency = currencyId, NetPayableAmount = avoirAmount,
            Status = "Draft", SignatureStatus = "Draft", CreatedBy = "back-user", CreatedAt = DateTime.UtcNow, IsActive = true
        };
        _db.RemittanceRefs.AddRange(premium, avoirRemittance);
        await _db.SaveChangesAsync();
        var result = await Service(_db).CreateOutgoingClosingAsync(
            new() { RemittanceIds = [premium.RemittanceRef1, avoirRemittance.RemittanceRef1] }, default);
        Assert.True(result.Succeeded, result.Error?.Message);
        var closing = await _db.OutgoingClosings.Include(x => x.Lines).ThenInclude(line => line.Remittance)
            .SingleAsync(row => row.Lines.Any(line => line.RemittanceId == premium.RemittanceRef1));
        closing.Status = status;
        await _db.SaveChangesAsync();
        return closing;
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
        var secondPanelAllocation = await _db.InvoicePanelAllocations
            .SingleAsync(row => row.IdInvoice == _invoice.IdInvoice && row.IdPanel == _panels[1].IdPanel);
        secondPanelAllocation.IdCounterparty = _panels[0].IdCounterparty;
        secondPanelAllocation.ReinsurerNameSnapshot = "AMI";
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

    [Theory]
    [InlineData("INVOICE")]
    [InlineData("INVOICE_FEES")]
    public async Task BackOfficerInvoiceCreationPersistsAndReturnsInvoiceType(string invoiceType)
    {
        var allocation = new ReceiptAllocation
        {
            IdBankReceipt = _receipt.IdBankReceipt,
            IdInvoice = _invoice.IdInvoice,
            IdContract = _invoice.IdContract,
            IdCurrency = _receipt.IdCurrency,
            IdUser = "back-user",
            TargetType = "INVOICE",
            AllocatedAmount = 100m,
            IsActive = true
        };
        _db.ReceiptAllocations.Add(allocation);
        await _db.SaveChangesAsync();

        var created = await Service(_db).CreateInvoiceAsync(new InvoiceRequestDto
        {
            ContractId = _invoice.IdContract,
            ReceiptAllocationId = allocation.IdReceiptAllocation,
            PanelId = _panels[0].IdPanel,
            InvoiceType = invoiceType,
            InvoiceDate = new(2026, 10, 14),
            Amount = 25m,
            Label = "Type test"
        }, default);

        Assert.True(created.Succeeded, created.Error?.Message);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(invoiceType, created.Value!.InvoiceType);
        Assert.Equal(invoiceType, await _db.Invoices
            .Where(invoice => invoice.IdInvoice == created.Value.IdInvoice)
            .Select(invoice => invoice.InvoiceType)
            .SingleAsync());

        var details = await Service(_db).GetInvoiceAsync(created.Value.IdInvoice, default);
        Assert.True(details.Succeeded, details.Error?.Message);
        Assert.Equal(invoiceType, details.Value!.InvoiceType);

        var list = await Service(_db).ListInvoicesAsync(new InvoiceQueryDto { InvoiceType = invoiceType }, default);
        Assert.True(list.Succeeded, list.Error?.Message);
        Assert.Contains(list.Value!.Items, invoice =>
            invoice.IdInvoice == created.Value.IdInvoice && invoice.InvoiceType == invoiceType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("REINSURER_PAYABLE")]
    [InlineData("CEDENT_PREMIUM_INVOICE")]
    public async Task BackOfficerInvoiceCreationRejectsUnsupportedInvoiceType(string invoiceType)
    {
        var result = await Service(_db).CreateInvoiceAsync(new InvoiceRequestDto { InvoiceType = invoiceType }, default);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("invoiceType", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INVOICE_FEES", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("INVOICE")]
    [InlineData("INVOICE_FEES")]
    public async Task ContractPremiumInvoiceCreationPersistsSelectedInvoiceType(string invoiceType)
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 1000m;
        contract.CedentCommission = 0m;
        _invoice.Status = "Cancelled";
        _panels[0].GrossPremium = 600m;
        _panels[1].GrossPremium = 400m;
        _panels[1].Status = "Confirmed";
        await _db.SaveChangesAsync();

        var result = await Service(_db).CreatePremiumInvoiceAsync(_invoice.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = invoiceType,
            GrossAmount = 100m,
            InvoiceDate = new(2026, 10, 14),
            Label = "Selected invoice type",
            IdempotencyKey = $"invoice-type-{invoiceType}"
        }, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(invoiceType, result.Value!.InvoiceType);
        Assert.Equal(invoiceType, await _db.Invoices
            .Where(item => item.IdInvoice == result.Value.InvoiceId)
            .Select(item => item.InvoiceType)
            .SingleAsync());
        Assert.Equal(100m, result.Value.Allocations.Sum(line => line.AllocationAmount));
        Assert.All(result.Value.Allocations, line => Assert.Equal(line.AllocationAmount, line.SignedAllocationAmount));
        var history = await Service(_db).ListContractInvoicesAsync(_invoice.IdContract, default);
        Assert.True(history.Succeeded, history.Error?.Message);
        Assert.Contains(history.Value!, item => item.InvoiceId == result.Value.InvoiceId && item.InvoiceType == invoiceType);
    }

    [Fact]
    public async Task AvoirCreationPersistsPositivePanelAllocationsAndReturnsNegativeSignedAllocationsIdempotently()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 100m;
        contract.CedentCommission = 0m;
        _invoice.InvoiceType = "INVOICE";
        _invoice.GrossPremium = 100m;
        _invoice.NetPremium = 100m;
        _invoice.Status = "Issued";
        _panels[0].Status = "Confirmed";
        _panels[1].Status = "Confirmed";
        await _db.SaveChangesAsync();

        var request = new InvoiceRequestDto
        {
            ContractId = contract.IdContract,
            InvoiceType = "AVOIR",
            ProcessingMode = "PAIR_WITH_ENCAISSEMENT",
            Amount = 10m,
            InvoiceDate = new(2026, 10, 19),
            Label = "Partial reinsurer credit",
            IdempotencyKey = "avoir-allocation-retry"
        };
        var service = Service(_db);
        var created = await service.CreateInvoiceAsync(request, default);
        var retried = await service.CreateInvoiceAsync(request, default);

        Assert.True(created.Succeeded, created.Error?.Message);
        Assert.True(retried.Succeeded, retried.Error?.Message);
        Assert.Equal(created.Value!.InvoiceId, retried.Value!.InvoiceId);
        Assert.Equal(10m, created.Value.Amount);
        Assert.Equal(-10m, created.Value.SignedAmount);
        Assert.Equal(10m, created.Value.Allocations.Sum(line => line.AllocationAmount));
        Assert.Equal(-10m, created.Value.Allocations.Sum(line => line.SignedAllocationAmount));

        var ami = Assert.Single(created.Value.Allocations, line => line.ReinsurerName == "AMI");
        var star = Assert.Single(created.Value.Allocations, line => line.ReinsurerName == "STAR Assurances");
        Assert.Equal(60m, ami.SharePercentage);
        Assert.Equal(6m, ami.AllocationAmount);
        Assert.Equal(-6m, ami.SignedAllocationAmount);
        Assert.Equal(40m, star.SharePercentage);
        Assert.Equal(4m, star.AllocationAmount);
        Assert.Equal(-4m, star.SignedAllocationAmount);
        Assert.All(created.Value.Allocations, line => Assert.Equal("TND", line.Currency));

        var stored = await _db.InvoicePanelAllocations.Where(line => line.IdInvoice == created.Value.InvoiceId).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(10m, stored.Sum(line => line.GrossPremium));
        Assert.All(stored, line => Assert.True(line.GrossPremium >= 0m));

        var details = await service.GetInvoiceAsync(created.Value.InvoiceId, default);
        Assert.True(details.Succeeded, details.Error?.Message);
        Assert.Equal(-10m, details.Value!.Allocations.Sum(line => line.SignedAllocationAmount));
        Assert.Equal(2, details.Value.Allocations.Count);
        var history = await service.ListContractInvoicesAsync(contract.IdContract, default);
        Assert.True(history.Succeeded, history.Error?.Message);
        var historyAvoir = Assert.Single(history.Value!, line => line.InvoiceId == created.Value.InvoiceId);
        Assert.Equal(-10m, historyAvoir.Allocations.Sum(line => line.SignedAllocationAmount));
    }

    [Fact]
    public async Task AvoirAllocationAssignsRoundingRemainderDeterministicallyWithoutNegativeLines()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 100m;
        contract.CedentCommission = 0m;
        contract.SignedSharePctCedent = 3m;
        var panels = new[]
        {
            new ContractPanel { IdPanel = 1, IdCounterparty = 1, SignedSharePct = 1m },
            new ContractPanel { IdPanel = 2, IdCounterparty = 2, SignedSharePct = 1m },
            new ContractPanel { IdPanel = 3, IdCounterparty = 3, SignedSharePct = 1m }
        };

        var first = PremiumDebitNoteAllocationCalculator.Calculate(contract, panels, 0.001m, 3).Allocations;
        var second = PremiumDebitNoteAllocationCalculator.Calculate(contract, panels, 0.001m, 3).Allocations;

        Assert.Equal(new[] { 0m, 0m, 0.001m }, first.Select(line => line.AllocationAmount));
        Assert.Equal(first.Select(line => line.AllocationAmount), second.Select(line => line.AllocationAmount));
        Assert.All(first, line => Assert.True(line.AllocationAmount >= 0m));
        Assert.Equal(0.001m, first.Sum(line => line.AllocationAmount));
    }

    [Fact]
    public async Task AvoirRejectsCreditExceedingOneConfirmedReinsurersRemainingInvoicedAllocation()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 10m;
        contract.CedentCommission = 0m;
        _invoice.InvoiceType = "INVOICE";
        _invoice.GrossPremium = 10m;
        _invoice.NetPremium = 10m;
        _invoice.Status = "Issued";
        _panels[0].Status = "Confirmed";
        _panels[1].Status = "Confirmed";
        _db.InvoicePanelAllocations.RemoveRange(await _db.InvoicePanelAllocations.Where(x => x.IdInvoice == _invoice.IdInvoice).ToListAsync());
        _db.InvoicePanelAllocations.AddRange(
            new InvoicePanelAllocation
            {
                IdInvoice = _invoice.IdInvoice, IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty,
                ReinsurerNameSnapshot = "AMI", SignedSharePctSnapshot = 60m, AllocationRatio = 0.6m,
                GrossPremium = 3m, NetAmount = 3m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow
            },
            new InvoicePanelAllocation
            {
                IdInvoice = _invoice.IdInvoice, IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty,
                ReinsurerNameSnapshot = "STAR Assurances", SignedSharePctSnapshot = 40m, AllocationRatio = 0.4m,
                GrossPremium = 7m, NetAmount = 7m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        var result = await Service(_db).CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "AVOIR", ProcessingMode = "DIRECT_TO_REMITTANCES", GrossAmount = 10m, InvoiceDate = new(2026, 10, 19), IdempotencyKey = "panel-cap-exceeded"
        }, default);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("available invoiced allocation", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(await _db.Invoices.ToListAsync(), invoice => invoice.InvoiceType == "AVOIR");
        Assert.Equal(2, await _db.InvoicePanelAllocations.CountAsync());
    }

    [Fact]
    public async Task AvoirRequiresConfirmedReinsurerPanels()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 100m;
        _invoice.GrossPremium = 100m;
        _invoice.Status = "Issued";
        foreach (var panel in _panels) panel.Status = "Bound";
        await _db.SaveChangesAsync();

        var result = await Service(_db).CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "AVOIR", ProcessingMode = "PAIR_WITH_ENCAISSEMENT", GrossAmount = 10m, InvoiceDate = new(2026, 10, 19)
        }, default);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("confirmed reinsurers", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(await _db.Invoices.ToListAsync(), invoice => invoice.InvoiceType == "AVOIR");
        Assert.Empty(await _db.InvoicePanelAllocations.ToListAsync());
    }

    [Fact]
    public async Task ContractPremiumInvoiceCreationRequiresSupportedInvoiceType()
    {
        var result = await Service(_db).CreatePremiumInvoiceAsync(_invoice.IdContract, new PremiumDebitNoteRequestDto
        {
            GrossAmount = 10m,
            InvoiceDate = new(2026, 10, 14)
        }, default);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("INVOICE, INVOICE_FEES or AVOIR", result.Error!.Message);
    }

    [Fact]
    public async Task BackOfficerInvoiceListHandlesMixedTypesNullOptionalValuesAndPaging()
    {
        _db.Invoices.AddRange(
            new Invoice
            {
                IdContract = _invoice.IdContract,
                InvoiceNumber = "INV-2026-TEST-FEES",
                InvoiceType = "INVOICE_FEES",
                InvoiceDate = new(2026, 10, 14),
                GrossPremium = 25m,
                NetPremium = 25m,
                Status = "Draft",
                Label = "Fees",
                CreatedBy = "back-user",
                IdUser = "back-user",
                CreatedAt = DateTime.UtcNow
            },
            new Invoice
            {
                IdContract = _invoice.IdContract,
                IdCurrency = null,
                IdCounterparty = null,
                IdPanel = null,
                InvoiceNumber = "AV-2026-TEST-AVOIR",
                InvoiceType = "avoir",
                InvoiceDate = new(2026, 10, 15),
                GrossPremium = 10m,
                NetPremium = 10m,
                Status = "Draft",
                Label = null,
                CreatedBy = "back-user",
                IdUser = "back-user",
                CreatedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        var result = await Service(_db).ListInvoicesAsync(new InvoiceQueryDto
        {
            PageNumber = 1,
            PageSize = 20,
            InvoiceType = "  ",
            PaymentStatus = "undefined",
            CurrencyId = "All currencies"
        }, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal(3, result.Value!.TotalCount);
        Assert.Equal(1, result.Value.PageNumber);
        Assert.Equal(20, result.Value.PageSize);
        var avoir = Assert.Single(result.Value.Items, item => item.InvoiceNumber == "AV-2026-TEST-AVOIR");
        Assert.Equal("AVOIR", avoir.InvoiceType);
        Assert.Equal("AV-2026-TEST-AVOIR", avoir.DocumentNumber);
        Assert.Equal(10m, avoir.Amount);
        Assert.Equal(-10m, avoir.SignedAmount);
        Assert.True(avoir.IsAvoir);
        Assert.Equal(string.Empty, avoir.Label);
        Assert.Null(avoir.CurrencyCode);
        Assert.Equal("Factory protection", avoir.ContractName);

        var pageBeyondEnd = await Service(_db).ListInvoicesAsync(new InvoiceQueryDto { PageNumber = 2, PageSize = 20 }, default);
        Assert.True(pageBeyondEnd.Succeeded, pageBeyondEnd.Error?.Message);
        Assert.Empty(pageBeyondEnd.Value!.Items);
        Assert.Equal(3, pageBeyondEnd.Value.TotalCount);

        var normalizedType = await Service(_db).ListInvoicesAsync(new InvoiceQueryDto { InvoiceType = "avoir" }, default);
        Assert.True(normalizedType.Succeeded, normalizedType.Error?.Message);
        Assert.Single(normalizedType.Value!.Items);

        var noMatches = await Service(_db).ListInvoicesAsync(new InvoiceQueryDto { Search = "no matching invoice" }, default);
        Assert.True(noMatches.Succeeded, noMatches.Error?.Message);
        Assert.Equal(StatusCodes.Status200OK, noMatches.StatusCode);
        Assert.Empty(noMatches.Value!.Items);
        Assert.Equal(0, noMatches.Value.TotalCount);

        var invalidPage = await Service(_db).ListInvoicesAsync(new InvoiceQueryDto { PageNumber = 0 }, default);
        Assert.False(invalidPage.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, invalidPage.StatusCode);
    }

    [Fact]
    public async Task FinanceReceiptCanBeCreatedWithoutCollectionType()
    {
        var created = await Service(_db, "Finance Officer").CreateReceiptAsync(new BankReceiptRequestDto
        {
            BankAccountId = _receipt.IdBankAccount,
            ReceiptMonth = "2026-09",
            AmountReceived = 125m,
            SettlementMethod = "BankTransfer",
            BankReference = "BANK-NO-COLLECTION-TYPE",
            SettlementLabel = "Encaissement without collection type",
            BusinessReference = "TEST-ENC-01"
        }, default);

        Assert.True(created.Succeeded, created.Error?.Message);
        Assert.Equal(125m, created.Value!.AmountReceived);
        Assert.DoesNotContain("CollectionType", typeof(BankReceiptResponseDto).GetProperties().Select(property => property.Name));
        var entity = await _db.BankReceipts.SingleAsync(receipt => receipt.IdBankReceipt == created.Value.IdBankReceipt);
        Assert.Null(entity.CollectionType);
    }

    [Fact]
    public async Task ContractInvoiceWorkflowCreatesInvoiceFeesAndAvoirWithIndependentNumbersAndSignedTotals()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 1000m;
        _invoice.Status = "Cancelled";
        await _db.SaveChangesAsync();
        var service = Service(_db);

        var invoice = await service.CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "INVOICE", GrossAmount = 100m, InvoiceDate = new(2026, 10, 16), Label = "Premium"
        }, default);
        var fees = await service.CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "INVOICE_FEES", GrossAmount = 200m, InvoiceDate = new(2026, 10, 16), Label = "Fees"
        }, default);
        var avoir = await service.CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "AVOIR", ProcessingMode = "PAIR_WITH_ENCAISSEMENT", GrossAmount = 50m, InvoiceDate = new(2026, 10, 16), Label = "Premium adjustment"
        }, default);

        Assert.True(invoice.Succeeded, invoice.Error?.Message);
        Assert.True(fees.Succeeded, fees.Error?.Message);
        Assert.True(avoir.Succeeded, avoir.Error?.Message);
        Assert.Equal("INV-2026-000001", invoice.Value!.DocumentNumber);
        Assert.Equal("INV-2026-000002", fees.Value!.DocumentNumber);
        Assert.Equal("AV-2026-000001", avoir.Value!.DocumentNumber);
        Assert.Equal("INVOICE", invoice.Value.InvoiceType);
        Assert.Equal("INVOICE_FEES", fees.Value.InvoiceType);
        Assert.Equal("AVOIR", avoir.Value.InvoiceType);
        Assert.Equal("PAIR_WITH_ENCAISSEMENT", avoir.Value.ProcessingMode);
        Assert.True(avoir.Value.PairingRequired);
        Assert.Empty(avoir.Value.CreatedRemittances);
        Assert.Equal(50m, avoir.Value.Amount);
        Assert.Equal(-50m, avoir.Value.SignedAmount);
        Assert.True(avoir.Value.IsAvoir);
        Assert.Equal(250m, await _db.Invoices.Where(row => row.IdContract == contract.IdContract && row.Status != "Cancelled")
            .SumAsync(row => row.InvoiceType == "AVOIR" ? -(row.GrossPremium ?? 0m) : row.GrossPremium ?? 0m));

        var createdAvoir = await _db.Invoices.SingleAsync(row => row.IdInvoice == avoir.Value.InvoiceId);
        Assert.Equal(50m, createdAvoir.GrossPremium);
        Assert.Equal(50m, createdAvoir.NetPremium);

        var history = await service.ListContractInvoicesAsync(contract.IdContract, default);
        Assert.True(history.Succeeded, history.Error?.Message);
        Assert.Contains(history.Value!, row => row.InvoiceId == avoir.Value.InvoiceId && row.InvoiceType == "AVOIR" && row.SignedAmount == -50m);

        var overviewData = await Service(_db, bankAccounts: TestIrisBankAccounts())
            .GetContractFinancialNoteAsync(contract.IdContract, default);
        Assert.True(overviewData.Succeeded, overviewData.Error?.Message);
        Assert.StartsWith("CreditNote_CN-", overviewData.Value!.FileName);
        Assert.Equal("CREDIT_NOTE", overviewData.Value.DocumentType);
        Assert.Equal("application/pdf", overviewData.Value.ContentType);
        Assert.StartsWith("%PDF-1.4", Encoding.Latin1.GetString(overviewData.Value.Content));
    }

    [Fact]
    public async Task DirectAvoirModeCreatesOneNegativeReinsurerRemittancePerSavedAllocationAndCannotBePaired()
    {
        await ConfigureDirectAvoirContractAsync();
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);

        var request = JsonSerializer.Deserialize<PremiumDebitNoteRequestDto>("""
            {"invoiceType":"AVOIR","amount":100,"invoiceDate":"2026-10-21","label":"Direct panel credit","processingMode":"DIRECT_TO_REMITTANCES","idempotencyKey":"direct-avoir-unique-01"}
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var service = Service(_db);
        var preview = await service.PreviewPremiumInvoiceAsync(contract.IdContract, request, default);
        Assert.True(preview.Succeeded, preview.Error?.Message);
        Assert.Equal("DIRECT_TO_REMITTANCES", preview.Value!.ProcessingMode);
        Assert.Equal(2, preview.Value.Allocations.Count);
        Assert.All(preview.Value.Allocations, row => Assert.True(row.SignedNetPayableAmount < 0m));

        var created = await service.CreatePremiumInvoiceAsync(contract.IdContract, request, default);
        Assert.True(created.Succeeded, created.Error?.Message);
        Assert.Equal("DIRECT_TO_REMITTANCES", created.Value!.ProcessingMode);
        Assert.False(created.Value.PairingRequired);
        Assert.Equal(2, created.Value.CreatedRemittances.Count);
        Assert.All(created.Value.CreatedRemittances, row =>
        {
            Assert.Equal("AVOIR", row.SourceType);
            Assert.Equal("REINSURER", row.BeneficiaryType);
            Assert.True(row.SignedNetPayableAmount < 0m);
            Assert.Null(row.ReceiptAllocationId);
            Assert.Equal("Draft", row.Status);
        });
        Assert.Equal(new[] { -54m, -32m }, created.Value.CreatedRemittances
            .OrderBy(row => row.ReinsurerName).Select(row => row.SignedNetPayableAmount));

        var saved = await _db.RemittanceRefs.Where(row => row.SourceInvoiceId == created.Value.InvoiceId).ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.All(saved, row =>
        {
            Assert.Null(row.IdReceiptAllocation);
            Assert.Equal("AVOIR", row.SourceType);
            Assert.Equal("REINSURER", row.BeneficiaryType);
            Assert.NotNull(row.IdPanel);
            Assert.NotNull(row.IdInvoiceAllocation);
        });

        var eligible = await service.GetEligibleInvoicesAsync(_receipt.IdBankReceipt, null, default);
        Assert.True(eligible.Succeeded, eligible.Error?.Message);
        Assert.DoesNotContain(eligible.Value!, item => item.InvoiceId == created.Value.InvoiceId);
        var pairing = await service.AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, new InvoiceReceiptAllocationRequestDto
        {
            Allocations = [
                new InvoiceReceiptAllocationItemDto { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 10m },
                new InvoiceReceiptAllocationItemDto { InvoiceId = created.Value.InvoiceId, AllocatedAmount = 10m }
            ]
        }, default);
        Assert.False(pairing.Succeeded);
        Assert.Equal(StatusCodes.Status409Conflict, pairing.StatusCode);
        Assert.Equal("AVOIR_ALREADY_PROCESSED_DIRECTLY", pairing.Error!.Code);
        Assert.Empty(await _db.ReceiptAllocations.ToListAsync());

        var retry = await service.CreatePremiumInvoiceAsync(contract.IdContract, request, default);
        Assert.True(retry.Succeeded, retry.Error?.Message);
        Assert.Equal(created.Value.InvoiceId, retry.Value!.InvoiceId);
        Assert.Equal(created.Value.CreatedRemittances.Select(row => row.RemittanceId), retry.Value.CreatedRemittances.Select(row => row.RemittanceId));
        Assert.Equal(2, await _db.RemittanceRefs.CountAsync(row => row.SourceInvoiceId == created.Value.InvoiceId));
    }

    [Fact]
    public async Task DirectAvoirPreviewWithoutValidConfirmedPanelReturnsBusinessError()
    {
        await ConfigureDirectAvoirContractAsync();
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        _panels[0].Status = "Draft";
        _panels[1].Status = "Not Taken Up";
        await _db.SaveChangesAsync();

        var result = await Service(_db).PreviewPremiumInvoiceAsync(_invoice.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "AVOIR",
            ProcessingMode = "DIRECT_TO_REMITTANCES",
            GrossAmount = 1m,
            InvoiceDate = new(2026, 10, 22)
        }, default);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("confirmed reinsurers", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectAvoirPreviewRejectsPanelWhoseCounterpartyIsNotAReinsurer()
    {
        await ConfigureDirectAvoirContractAsync();
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        var assignments = await _db.CounterpartyTypeAssignments
            .Where(item => item.CounterpartyId == _panels[0].IdCounterparty).ToListAsync();
        _db.CounterpartyTypeAssignments.RemoveRange(assignments);
        await _db.SaveChangesAsync();

        var result = await Service(_db).PreviewPremiumInvoiceAsync(_invoice.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "AVOIR",
            ProcessingMode = "DIRECT_TO_REMITTANCES",
            GrossAmount = 1m,
            InvoiceDate = new(2026, 10, 22)
        }, default);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("not configured as a reinsurer", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AvoirCreationRequiresOneOfTheSupportedProcessingModes()
    {
        var result = await Service(_db).CreatePremiumInvoiceAsync(_invoice.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "AVOIR",
            GrossAmount = 10m,
            InvoiceDate = new(2026, 10, 21)
        }, default);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("processingMode", string.Join(" ", result.Error!.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectAvoirRemittanceInsertFailureRollsBackTheAvoirAndPanelAllocations()
    {
        await ConfigureDirectAvoirContractAsync();
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        var invoiceCount = await _db.Invoices.CountAsync();
        var allocationCount = await _db.InvoicePanelAllocations.CountAsync();

        await using var failing = Context(new RemittanceInsertFailure());
        await Assert.ThrowsAsync<DbUpdateException>(() => Service(failing).CreatePremiumInvoiceAsync(contract.IdContract,
            new PremiumDebitNoteRequestDto
            {
                InvoiceType = "AVOIR",
                ProcessingMode = "DIRECT_TO_REMITTANCES",
                GrossAmount = 10m,
                InvoiceDate = new(2026, 10, 21),
                IdempotencyKey = "direct-avoir-rollback-01"
            }, default));

        Assert.Equal(invoiceCount, await _db.Invoices.CountAsync());
        Assert.Equal(allocationCount, await _db.InvoicePanelAllocations.CountAsync());
        Assert.Empty(await _db.RemittanceRefs.ToListAsync());
    }

    [Fact]
    public async Task AvoirPreviewAndCreationUseTheInvoicePanelDistributionAndPersistAuthoritativeRows()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 1000m;
        contract.CedentCommission = 0m;
        _invoice.GrossPremium = 1000m;
        _invoice.Status = "Issued";
        _panels[0].SignedSharePct = 60m;
        _panels[0].GrossPremium = 600m;
        _panels[0].Status = "Confirmed";
        _panels[1].SignedSharePct = 40m;
        _panels[1].GrossPremium = 400m;
        _panels[1].Status = "Confirmed";
        await _db.SaveChangesAsync();

        var service = Service(_db);
        var preview = await service.PreviewPremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "AVOIR", ProcessingMode = "PAIR_WITH_ENCAISSEMENT", GrossAmount = 10m, InvoiceDate = new(2026, 10, 19)
        }, default);

        Assert.True(preview.Succeeded, preview.Error?.Message);
        Assert.Equal(-10m, preview.Value!.SignedAmount);
        Assert.Equal("PAIR_WITH_ENCAISSEMENT", preview.Value.ProcessingMode);
        Assert.Equal(new[] { 6m, 4m }, preview.Value.Allocations.Select(row => row.GrossPremium));
        Assert.Equal(10m, preview.Value.Allocations.Sum(row => row.GrossPremium));
        Assert.Equal(new[] { -6m, -4m }, preview.Value.Allocations.Select(row => row.SignedNetPayableAmount));

        var created = await service.CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "AVOIR",
            ProcessingMode = "PAIR_WITH_ENCAISSEMENT",
            GrossAmount = 10m,
            InvoiceDate = new(2026, 10, 19),
            Allocations = [
                new PremiumDebitNoteAllocationDto { PanelId = _panels[0].IdPanel, GrossPremium = 999m },
                new PremiumDebitNoteAllocationDto { PanelId = _panels[1].IdPanel, GrossPremium = 999m }
            ]
        }, default);

        Assert.True(created.Succeeded, created.Error?.Message);
        Assert.Equal(new[] { 6m, 4m }, created.Value!.Allocations.Select(row => row.GrossPremium));
        Assert.Equal(10m, created.Value.Allocations.Sum(row => row.GrossPremium));
        var savedRows = await _db.InvoicePanelAllocations
            .Where(row => row.IdInvoice == created.Value.InvoiceId)
            .OrderBy(row => row.IdPanel)
            .ToListAsync();
        Assert.Equal(new[] { 6m, 4m }, savedRows.Select(row => row.GrossPremium));
    }

    [Fact]
    public async Task AvoirCannotExceedGrossPremiumLessCedentCommissionAndExistingAvoirs()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 100m;
        contract.CedentCommission = 10m;
        _invoice.InvoiceType = "INVOICE";
        _invoice.GrossPremium = 100m;
        _invoice.NetPremium = 100m;
        _invoice.Status = "Issued";
        await _db.SaveChangesAsync();

        var result = await Service(_db).CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "AVOIR", ProcessingMode = "PAIR_WITH_ENCAISSEMENT", GrossAmount = 90.01m, InvoiceDate = new(2026, 10, 16)
        }, default);

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("available credit balance", result.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(await _db.Invoices.ToListAsync(), row => row.InvoiceType == "AVOIR");
    }

    [Fact]
    public async Task InvoiceContextReturnsCreditBalanceSeparatelyFromRemainingInvoiceAmount()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 45000m;
        contract.CedentCommission = 10m;
        _invoice.InvoiceType = "INVOICE";
        _invoice.GrossPremium = 45000m;
        _invoice.NetPremium = 45000m;
        _invoice.Status = "Issued";
        _invoice.Label = null!;
        await _db.SaveChangesAsync();

        var context = await Service(_db).GetInvoiceContextAsync(contract.IdContract, default);

        Assert.True(context.Succeeded, context.Error?.Message);
        Assert.Equal(0m, context.Value!.RemainingInvoiceAmount);
        Assert.Equal(0m, context.Value.RemainingInvoiceableAmount);
        Assert.Equal(45000m, context.Value.TotalInvoicedAmount);
        Assert.Equal(0m, context.Value.TotalAvoirAmount);
        Assert.Equal(40500m, context.Value.AvailableCreditBalance);
        Assert.True(context.Value.CanCreateAvoir);
        Assert.Null(context.Value.AvoirBlockingReason);
        Assert.Equal("TND", context.Value.Currency);
        Assert.Contains(context.Value.PreviousInvoices, invoice => invoice.Label == string.Empty);
    }

    [Fact]
    public async Task InvoiceContextUsesFullContractGrossWithoutApplyingCedentSignedShare()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 2000m;
        contract.CedentCommission = 10m;
        contract.SignedSharePctCedent = 20m;
        _panels[0].SignedSharePct = 10m;
        _panels[1].SignedSharePct = 10m;
        _db.ContractFacs.Add(new ContractFac
        {
            IdContract = contract.IdContract,
            SumInsured = 500000m,
            PremiumRate = 0.1m
        });
        _invoice.Status = "Cancelled";
        await _db.SaveChangesAsync();

        var context = await Service(_db).GetInvoiceContextAsync(contract.IdContract, default);

        Assert.True(context.Succeeded, context.Error?.Message);
        Assert.Equal(2000m, context.Value!.ContractGrossPremium);
        Assert.Equal(1800m, context.Value.AvailableCreditBalance);
        Assert.Equal(10m, context.Value.CedentCommissionPct);
        Assert.Equal(10m, context.Value.CedentCommissionPercentage);
        Assert.Equal(200m, context.Value.CedentCommissionAmount);
        Assert.Equal(20m, context.Value.ContractSignedSharePercentage);
        Assert.Equal(1800m, context.Value.InvoiceableNetPremium);
        Assert.Equal(2000m, context.Value.InvoiceableGrossPremium);
        Assert.Equal(0m, context.Value.AlreadyInvoicedAmount);
        Assert.Equal(1800m, context.Value.RemainingInvoiceableAmount);
        Assert.Equal(3, context.Value.CurrencyDecimalPlaces);
        Assert.Equal("TND", context.Value.Currency);

        var overLimit = await Service(_db).CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "INVOICE",
            GrossAmount = 1800.001m,
            InvoiceDate = new(2026, 10, 20)
        }, default);
        Assert.False(overLimit.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, overLimit.StatusCode);
        Assert.Contains("remaining invoiceable net premium", string.Join(" ", overLimit.Error!.Errors), StringComparison.OrdinalIgnoreCase);

        var accepted = await Service(_db).CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "INVOICE",
            GrossAmount = 1800m,
            InvoiceDate = new(2026, 10, 20)
        }, default);
        Assert.True(accepted.Succeeded, accepted.Error?.Message);
        Assert.Equal(1800m, accepted.Value!.Amount);
        Assert.Equal(45m, accepted.Value.Allocations.Sum(line => line.AllocationAmount));

        var duplicateOverage = await Service(_db).CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "INVOICE",
            GrossAmount = 0.001m,
            InvoiceDate = new(2026, 10, 20)
        }, default);
        Assert.False(duplicateOverage.Succeeded);
        Assert.Equal(StatusCodes.Status400BadRequest, duplicateOverage.StatusCode);
        Assert.Contains("No invoiceable premium remains", string.Join(" ", duplicateOverage.Error!.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContractInvoiceHistoryUsesSignedShareAndRunningInvoiceBalance()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 450m;
        contract.CedentCommission = 0m;
        contract.SignedSharePctCedent = 10m;
        _invoice.InvoiceType = "INVOICE";
        _invoice.GrossPremium = 10m;
        _invoice.NetPremium = 10m;
        _invoice.Status = "Issued";
        _invoice.InvoiceDate = new(2026, 10, 1);
        _db.Invoices.AddRange(
            new Invoice
            {
                IdContract = contract.IdContract, IdCurrency = _invoice.IdCurrency,
                InvoiceNumber = "INV-RUNNING-002", InvoiceType = "INVOICE",
                InvoiceDate = new(2026, 10, 2), GrossPremium = 10m, NetPremium = 10m,
                Status = "Issued", CreatedAt = DateTime.UtcNow, CreatedBy = "back-user", IdUser = "back-user"
            },
            new Invoice
            {
                IdContract = contract.IdContract, IdCurrency = _invoice.IdCurrency,
                InvoiceNumber = "INV-RUNNING-003", InvoiceType = "INVOICE",
                InvoiceDate = new(2026, 10, 3), GrossPremium = 10m, NetPremium = 10m,
                Status = "Issued", CreatedAt = DateTime.UtcNow, CreatedBy = "back-user", IdUser = "back-user"
            });
        await _db.SaveChangesAsync();

        var service = Service(_db);
        var history = await service.ListContractInvoicesAsync(contract.IdContract, default);
        var context = await service.GetInvoiceContextAsync(contract.IdContract, default);

        Assert.True(history.Succeeded, history.Error?.Message);
        Assert.True(context.Succeeded, context.Error?.Message);
        Assert.Equal(45m, context.Value!.InvoiceableNetPremium);
        Assert.Equal(15m, context.Value.RemainingInvoiceableAmount);
        Assert.Equal(new[] { 35m, 25m, 15m }, history.Value!
            .OrderBy(item => item.InvoiceDate)
            .Select(item => item.RemainingAmount));
        Assert.Equal(new[] { 35m, 25m, 15m }, history.Value
            .OrderBy(item => item.InvoiceDate)
            .Select(item => item.RemainingInvoiceAmount));
    }

    [Fact]
    public async Task FacultativePremiumRepairCorrectsStoredGrossAndNetIdempotentlyWithoutRewritingInvoices()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 50000m;
        contract.CedentCommission = 10m;
        contract.SignedSharePctCedent = 100m;
        contract.NetPremiumCedent = 45000m;
        _db.ContractFacs.Add(new ContractFac
        {
            IdContract = contract.IdContract,
            SumInsured = 500000m,
            PremiumRate = 0.1m
        });
        _panels[0].WrittenSharePct = 60m;
        _panels[1].WrittenSharePct = 40m;
        var originalInvoiceAmount = _invoice.GrossPremium;
        await _db.SaveChangesAsync();

        var repair = new FacultativePanelFinancialRepairService(
            _db, new PremiumCalculationService(), NullLogger<FacultativePanelFinancialRepairService>.Instance);
        var firstRun = await repair.RepairAsync(default);

        Assert.True(firstRun.Succeeded, firstRun.Error?.Message);
        Assert.Equal(1, firstRun.Value!.ContractsUpdated);
        Assert.Equal(500m, contract.GrossPremium);
        Assert.Equal(450m, contract.NetPremiumCedent);
        Assert.Contains(firstRun.Value.HistoricalInvoiceReconciliations,
            item => item.ContractId == contract.IdContract && item.CorrectedInvoiceableNetPremium == 450m);
        Assert.Equal(originalInvoiceAmount, await _db.Invoices
            .Where(item => item.IdInvoice == _invoice.IdInvoice)
            .Select(item => item.GrossPremium)
            .SingleAsync());

        var secondRun = await repair.RepairAsync(default);

        Assert.True(secondRun.Succeeded, secondRun.Error?.Message);
        Assert.Equal(0, secondRun.Value!.ContractsUpdated);
        Assert.Equal(0, secondRun.Value.PanelsUpdated);
    }

    [Fact]
    public async Task InvoiceContextCreditBalanceIncludesFeesSubtractsAvoirsAndExcludesCancelledDocuments()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 45000m;
        contract.CedentCommission = 10m;
        _invoice.InvoiceType = "INVOICE";
        _invoice.GrossPremium = 45000m;
        _invoice.Status = "Issued";
        _db.Invoices.AddRange(
            new Invoice
            {
                IdContract = contract.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = "INV-FEES-1",
                InvoiceType = "INVOICE_FEES", InvoiceDate = new(2026, 10, 10), GrossPremium = 500m,
                NetPremium = 500m, Status = "Issued", Label = null!, CreatedAt = DateTime.UtcNow,
                CreatedBy = "back-user", IdUser = "back-user"
            },
            new Invoice
            {
                IdContract = contract.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = "AV-CREDIT-1",
                InvoiceType = "AVOIR", InvoiceDate = new(2026, 10, 11), GrossPremium = 10000m,
                NetPremium = 10000m, Status = "Draft", CreatedAt = DateTime.UtcNow,
                CreatedBy = "back-user", IdUser = "back-user"
            },
            new Invoice
            {
                IdContract = contract.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = "INV-CANCELLED",
                InvoiceType = "INVOICE", InvoiceDate = new(2026, 10, 12), GrossPremium = 9000m,
                NetPremium = 9000m, Status = "Cancelled", CreatedAt = DateTime.UtcNow,
                CreatedBy = "back-user", IdUser = "back-user"
            },
            new Invoice
            {
                IdContract = contract.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = "INV-ARCHIVED",
                InvoiceType = "INVOICE", InvoiceDate = new(2026, 10, 13), GrossPremium = 7000m,
                NetPremium = 7000m, Status = "Archived", CreatedAt = DateTime.UtcNow,
                CreatedBy = "back-user", IdUser = "back-user"
            });
        await _db.SaveChangesAsync();

        var context = await Service(_db).GetInvoiceContextAsync(contract.IdContract, default);

        Assert.True(context.Succeeded, context.Error?.Message);
        Assert.Equal(45500m, context.Value!.TotalInvoicedAmount);
        Assert.Equal(10000m, context.Value.TotalAvoirAmount);
        Assert.Equal(5000m, context.Value.RemainingInvoiceAmount);
        Assert.Equal(30500m, context.Value.AvailableCreditBalance);
    }

    [Fact]
    public async Task AvoirCreationAcceptsExactAvailableCreditAndReturnsRefreshedTotals()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 45000m;
        contract.CedentCommission = 10m;
        _invoice.InvoiceType = "INVOICE";
        _invoice.GrossPremium = 45000m;
        _invoice.NetPremium = 45000m;
        _invoice.Status = "Issued";
        var panelAllocations = await _db.InvoicePanelAllocations
            .Where(row => row.IdInvoice == _invoice.IdInvoice)
            .OrderBy(row => row.IdPanel)
            .ToListAsync();
        panelAllocations[0].GrossPremium = 27000m;
        panelAllocations[1].GrossPremium = 18000m;
        await _db.SaveChangesAsync();

        var result = await Service(_db).CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "AVOIR", ProcessingMode = "PAIR_WITH_ENCAISSEMENT", GrossAmount = 40500m, InvoiceDate = new(2026, 10, 16), Label = "Full credit"
        }, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(40500m, result.Value!.Amount);
        Assert.Equal(-40500m, result.Value.SignedAmount);
        Assert.Equal(45000m, result.Value.TotalInvoicedAmount);
        Assert.Equal(40500m, result.Value.TotalAvoirAmount);
        Assert.Equal(0m, result.Value.AvailableCreditBalance);
        Assert.Equal(0m, result.Value.RemainingInvoiceAmount);
        Assert.Equal(40500m, (await _db.Invoices.SingleAsync(row => row.IdInvoice == result.Value.InvoiceId)).GrossPremium);
    }

    [Fact]
    public async Task InvoiceContextWithoutActiveInvoicesReturnsZeroCreditAndCancelledAvoirIsIgnored()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 45000m;
        _invoice.Status = "Cancelled";
        _db.Invoices.Add(new Invoice
        {
            IdContract = contract.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = "AV-CANCELLED",
            InvoiceType = "AVOIR", InvoiceDate = new(2026, 10, 10), GrossPremium = 1000m,
            NetPremium = 1000m, Status = "Cancelled", CreatedAt = DateTime.UtcNow,
            CreatedBy = "back-user", IdUser = "back-user"
        });
        await _db.SaveChangesAsync();

        var context = await Service(_db).GetInvoiceContextAsync(contract.IdContract, default);

        Assert.True(context.Succeeded, context.Error?.Message);
        Assert.Equal(0m, context.Value!.TotalInvoicedAmount);
        Assert.Equal(0m, context.Value.TotalAvoirAmount);
        Assert.Equal(45000m, context.Value.AvailableCreditBalance);
        Assert.False(context.Value.CanCreateAvoir);
        Assert.Equal("NO_ACTIVE_INVOICES", context.Value.AvoirBlockingReason);
    }

    [Fact]
    public async Task ContractHistoryNormalizesLegacyTypesAndMatchesContextTotalsWithoutCreatorFiltering()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 150m;
        _invoice.InvoiceType = "CEDENT_PREMIUM_INVOICE";
        _invoice.GrossPremium = 100m;
        _invoice.NetPremium = 90m;
        _invoice.Status = "Issued";
        _invoice.Label = null!;
        _invoice.CreatedBy = "finance-user";
        _invoice.IdUser = "finance-user";
        var fees = new Invoice
        {
            IdContract = contract.IdContract, InvoiceNumber = "INV-FEE-LEGACY", InvoiceType = "INVOICE FEES",
            InvoiceDate = new(2026, 10, 9), GrossPremium = 50m, NetPremium = 50m,
            Status = "Issued", CreatedAt = DateTime.UtcNow, CreatedBy = "finance-user", IdUser = "finance-user"
        };
        var avoir = new Invoice
        {
            IdContract = contract.IdContract, InvoiceNumber = "AV-LEGACY", InvoiceType = "CREDIT_NOTE",
            InvoiceDate = new(2026, 10, 10), GrossPremium = 25m, NetPremium = 25m,
            Status = "Issued", CreatedAt = DateTime.UtcNow, CreatedBy = "finance-user", IdUser = "finance-user"
        };
        var cancelled = new Invoice
        {
            IdContract = contract.IdContract, InvoiceNumber = "INV-CANCELLED-HISTORY", InvoiceType = "INVOICE",
            InvoiceDate = new(2026, 10, 11), GrossPremium = 500m, NetPremium = 500m,
            Status = "Cancelled", CreatedAt = DateTime.UtcNow, CreatedBy = "finance-user", IdUser = "finance-user"
        };
        _db.Invoices.AddRange(fees, avoir, cancelled);
        await _db.SaveChangesAsync();

        var service = Service(_db);
        var history = await service.ListContractInvoicesAsync(contract.IdContract, default);
        var context = await service.GetInvoiceContextAsync(contract.IdContract, default);

        Assert.True(history.Succeeded, history.Error?.Message);
        Assert.True(context.Succeeded, context.Error?.Message);
        Assert.Equal(3, history.Value!.Count);
        var normalizedInvoice = Assert.Single(history.Value, item => item.InvoiceId == _invoice.IdInvoice);
        Assert.Equal("INVOICE", normalizedInvoice.InvoiceType);
        Assert.Equal(100m, normalizedInvoice.Amount);
        Assert.Equal(100m, normalizedInvoice.SignedAmount);
        Assert.Equal(string.Empty, normalizedInvoice.Label);
        Assert.Equal("TND", normalizedInvoice.Currency);
        Assert.Equal("INVOICE_FEES", Assert.Single(history.Value, item => item.InvoiceId == fees.IdInvoice).InvoiceType);
        var normalizedAvoir = Assert.Single(history.Value, item => item.InvoiceId == avoir.IdInvoice);
        Assert.Equal("AVOIR", normalizedAvoir.InvoiceType);
        Assert.Equal(-25m, normalizedAvoir.SignedAmount);
        Assert.True(normalizedAvoir.IsAvoir);
        Assert.DoesNotContain(history.Value, item => item.InvoiceId == cancelled.IdInvoice);

        Assert.Equal(150m, context.Value!.TotalInvoicedAmount);
        Assert.Equal(25m, context.Value.TotalAvoirAmount);
        Assert.Equal(25m, context.Value.RemainingInvoiceAmount);
        Assert.Equal(125m, context.Value.AvailableCreditBalance);
        Assert.Equal(history.Value.Count, context.Value.PreviousInvoices.Count);
        Assert.Contains(context.Value.PreviousInvoices, item => item.InvoiceType == "INVOICE" && item.IdInvoice == _invoice.IdInvoice);
        Assert.Contains(context.Value.PreviousInvoices, item => item.InvoiceType == "AVOIR" && item.SignedAmount == -25m);
    }

    [Fact]
    public async Task ContractInvoiceHistoryReturnsEmptyArrayWhenNoActiveDocumentsExist()
    {
        _invoice.Status = "Archived";
        await _db.SaveChangesAsync();

        var history = await Service(_db).ListContractInvoicesAsync(_invoice.IdContract, default);
        var context = await Service(_db).GetInvoiceContextAsync(_invoice.IdContract, default);

        Assert.True(history.Succeeded, history.Error?.Message);
        Assert.Empty(history.Value!);
        Assert.True(context.Succeeded, context.Error?.Message);
        Assert.Empty(context.Value!.PreviousInvoices);
        Assert.Equal(0m, context.Value.TotalInvoicedAmount);
        Assert.Equal(0m, context.Value.TotalAvoirAmount);
    }

    [Fact]
    public async Task AvoirOffsetsPairingWithoutCreatingPositiveReceiptAllocationAndRetryIsIdempotent()
    {
        _invoice.InvoiceType = "INVOICE";
        _receipt.AmountReceived = 50m;
        _invoice.GrossPremium = 100m;
        _invoice.NetPremium = 100m;
        _invoice.Status = "Issued";
        var avoir = new Invoice
        {
            IdContract = _invoice.IdContract,
            IdCurrency = _invoice.IdCurrency,
            InvoiceNumber = "AV-2026-000099",
            InvoiceType = "AVOIR",
            InvoiceDate = new(2026, 10, 16),
            GrossPremium = 50m,
            NetPremium = 50m,
            Status = "Issued",
            CreatedBy = "back-user",
            IdUser = "back-user",
            CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoir);
        await _db.SaveChangesAsync();
        var request = new InvoiceReceiptAllocationRequestDto
        {
            Allocations =
            [
                new InvoiceReceiptAllocationItemDto { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 100m },
                new InvoiceReceiptAllocationItemDto { InvoiceId = avoir.IdInvoice, AllocatedAmount = 50m }
            ]
        };

        var service = Service(_db);
        var paired = await service.AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, request, default);
        var retry = await service.AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, request, default);

        Assert.True(paired.Succeeded, paired.Error?.Message);
        Assert.Equal(50m, paired.Value!.SignedPairingTotal);
        var cashAllocation = Assert.Single(await _db.ReceiptAllocations.Where(row => row.IdBankReceipt == _receipt.IdBankReceipt).ToListAsync());
        Assert.Equal(_invoice.IdInvoice, cashAllocation.IdInvoice);
        Assert.Equal(50m, cashAllocation.AllocatedAmount);
        var credit = Assert.Single(await _db.InvoiceCreditApplications.Where(row => row.IdBankReceipt == _receipt.IdBankReceipt).ToListAsync());
        Assert.Equal(_invoice.IdInvoice, credit.TargetInvoiceId);
        Assert.Equal(avoir.IdInvoice, credit.AvoirInvoiceId);
        Assert.Equal(50m, credit.Amount);
        Assert.True(retry.Succeeded, retry.Error?.Message);
        Assert.Equal(paired.Value.TotalAllocated, retry.Value!.TotalAllocated);
        Assert.Single(await _db.ReceiptAllocations.Where(row => row.IdBankReceipt == _receipt.IdBankReceipt).ToListAsync());
        Assert.Single(await _db.InvoiceCreditApplications.Where(row => row.IdBankReceipt == _receipt.IdBankReceipt).ToListAsync());

        var invoiceDto = await service.GetInvoiceAsync(_invoice.IdInvoice, default);
        var avoirDto = await service.GetInvoiceAsync(avoir.IdInvoice, default);
        Assert.Equal(100m, invoiceDto.Value!.SettledAmount);
        Assert.Equal(InvoiceRecoveryCalculator.Settled, invoiceDto.Value.PaymentStatus);
        Assert.Equal(50m, avoirDto.Value!.SettledAmount);
        Assert.Equal(InvoiceRecoveryCalculator.Settled, avoirDto.Value.PaymentStatus);

        var refund = await service.RefundAvoirAsync(_receipt.IdBankReceipt, new AvoirRefundRequestDto
        {
            AvoirInvoiceId = avoir.IdInvoice, Amount = 1m, IdempotencyKey = "already-offset-avoir-refund"
        }, default);
        Assert.False(refund.Succeeded);
        Assert.Empty(await _db.AvoirRefundSettlements.ToListAsync());
        Assert.DoesNotContain(await _db.RemittanceRefs.ToListAsync(), item => item.SourceType == "AVOIR_REFUND");
    }

    [Fact]
    public async Task AvoirSettlementCreatesOneNegativeRemittancePerConfirmedReinsurerIdempotently()
    {
        _receipt.AmountReceived = 45m;
        _receipt.DetailsStatus = "Paired";
        _invoice.InvoiceType = "INVOICE";
        _invoice.GrossPremium = 45m;
        _invoice.NetPremium = 45m;
        var avoir = new Invoice
        {
            IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency,
            InvoiceNumber = "AV-2026-000201", InvoiceType = "AVOIR", GrossPremium = 10m,
            NetPremium = 10m, Status = "Issued", InvoiceDate = new(2026, 10, 19),
            CreatedBy = "back-user", IdUser = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoir);
        await _db.SaveChangesAsync();
        _db.InvoicePanelAllocations.AddRange(
            new InvoicePanelAllocation { IdInvoice = avoir.IdInvoice, IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty, ReinsurerNameSnapshot = "AMI", AllocationRatio = 0.5m, GrossPremium = 5m, NetAmount = 5m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow },
            new InvoicePanelAllocation { IdInvoice = avoir.IdInvoice, IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty, ReinsurerNameSnapshot = "STAR Assurances", AllocationRatio = 0.5m, GrossPremium = 5m, NetAmount = 5m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        var originalAllocation = new ReceiptAllocation
        {
            TargetType = "INVOICE", IdBankReceipt = _receipt.IdBankReceipt, IdInvoice = _invoice.IdInvoice,
            IdContract = _invoice.IdContract, IdCurrency = _receipt.IdCurrency, ExchangeRate = 1m,
            AllocatedAmount = 45m, PairedAt = DateTime.UtcNow, IdUser = "back-user", IsActive = true
        };
        _db.ReceiptAllocations.Add(originalAllocation);
        await _db.SaveChangesAsync();

        var back = Service(_db);
        var eligible = await back.GetEligibleAvoirRefundReceiptsAsync(avoir.IdInvoice, default);
        Assert.True(eligible.Succeeded, eligible.Error?.Message);
        var eligibleReceipt = Assert.Single(eligible.Value!);
        Assert.Equal(_receipt.IdBankReceipt, eligibleReceipt.BankReceiptId);
        Assert.Equal(45m, eligibleReceipt.AllocatedAmount);
        Assert.Equal(10m, eligibleReceipt.AvailableAvoirAmount);

        var request = new AvoirRefundRequestDto { AvoirInvoiceId = avoir.IdInvoice, Amount = 10m, IdempotencyKey = "avoir-refund-fully-paired" };
        var refund = await back.RefundAvoirAsync(_receipt.IdBankReceipt, request, default);
        var retry = await back.RefundAvoirAsync(_receipt.IdBankReceipt, request, default);
        Assert.True(refund.Succeeded, refund.Error?.Message);
        Assert.True(retry.Succeeded, retry.Error?.Message);
        Assert.Equal("AVOIR_REINSURER_ALLOCATION", refund.Value!.PairingMode);
        Assert.Equal(10m, refund.Value.AvoirAppliedAmount);
        Assert.Equal(0m, refund.Value.AvoirRemainingAmount);
        Assert.Equal("REINSURER", refund.Value.BeneficiaryType, ignoreCase: true);
        Assert.Equal(_panels[0].IdCounterparty, refund.Value.BeneficiaryId);
        Assert.Equal(refund.Value.RemittanceId, retry.Value!.RemittanceId);
        Assert.Equal(2, refund.Value.CreatedRemittances.Count);
        Assert.Equal(new[] { -5m, -5m }, refund.Value.CreatedRemittances.OrderBy(row => row.PanelId).Select(row => row.SignedNetPayableAmount));
        var remittances = await _db.RemittanceRefs.Where(x => x.SourceInvoiceId == avoir.IdInvoice).OrderBy(x => x.IdPanel).ToListAsync();
        Assert.Equal(2, remittances.Count);
        Assert.All(remittances, remittance =>
        {
            Assert.Equal("AVOIR", remittance.SourceType);
            Assert.Equal("REINSURER", remittance.BeneficiaryType, ignoreCase: true);
            Assert.NotNull(remittance.IdPanel);
            Assert.Equal(_db.ContractPanels.Single(panel => panel.IdPanel == remittance.IdPanel).IdCounterparty, remittance.IdCounterparty);
        });
        Assert.Equal(2, await _db.AvoirRefundSettlements.CountAsync());
        Assert.Equal(45m, await _db.ReceiptAllocations.Where(x => x.IdBankReceipt == _receipt.IdBankReceipt && x.IsActive).SumAsync(x => x.AllocatedAmount ?? 0m));
        Assert.Equal("Paired", _receipt.DetailsStatus);
        Assert.Equal(InvoiceRecoveryCalculator.Settled, await _db.Invoices.AsNoTracking().Where(x => x.IdInvoice == avoir.IdInvoice).Select(x => x.RecoveryStatus).SingleAsync());

        var blocked = await back.ConfirmRemittanceForDisbursementAsync(remittances[0].RemittanceRef1, default);
        Assert.False(blocked.Succeeded);
        Assert.Contains("must be netted", blocked.Error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchConfirmationNetsPremiumAndDirectAvoirForSameReinsurerAndIsIdempotent()
    {
        var account = await _db.CounterpartyBankAccounts.SingleAsync(row => row.IdCounterparty == _panels[0].IdCounterparty);
        account.IsDefault = false;
        var sourceContract = await _db.Contracts.SingleAsync(row => row.IdContract == _invoice.IdContract);
        var otherContract = new Contract
        {
            Name = "Other contract", Umr = "UMR-OTHER-2026", TypeOfBusiness = sourceContract.TypeOfBusiness,
            Status = "Bound", IdCounterparty = sourceContract.IdCounterparty, IdCurrency = sourceContract.IdCurrency,
            CreatedBy = "back-user", IdUser = "back-user", CreatedAt = DateTime.UtcNow,
            InceptionDate = sourceContract.InceptionDate, ExpiryDate = sourceContract.ExpiryDate, SignedSharePctCedent = 100m
        };
        _db.Contracts.Add(otherContract);
        await _db.SaveChangesAsync();
        var otherContractPanel = new ContractPanel
        {
            IdContract = otherContract.IdContract, IdCounterparty = _panels[0].IdCounterparty, Role = "Reinsurer",
            Status = "Confirmed", NetPremium = 0.45m, SignedSharePct = 100m, PremiumReference = "AMI-OTHER-PANEL"
        };
        _db.ContractPanels.Add(otherContractPanel);
        var avoirInvoice = new Invoice
        {
            IdContract = otherContract.IdContract, IdCurrency = _receipt.IdCurrency, InvoiceNumber = "AV-OTHER-2026-001",
            InvoiceType = "AVOIR", GrossPremium = 0.45m, NetPremium = 0.45m, Status = "Issued",
            InvoiceDate = new(2026, 10, 20), CreatedBy = "back-user", IdUser = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoirInvoice);
        await _db.SaveChangesAsync();
        await _db.SaveChangesAsync();

        var premium = new RemittanceRef
        {
            SourceType = "PREMIUM", IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty,
            IdCurrency = _receipt.IdCurrency, NetPayableAmount = 200m, Status = "Draft", SignatureStatus = "Draft",
            CreatedBy = "back-user", CreatedAt = DateTime.UtcNow, IsActive = true
        };
        var avoir = new RemittanceRef
        {
            SourceType = "AVOIR", SourceInvoiceId = avoirInvoice.IdInvoice, IdPanel = otherContractPanel.IdPanel,
            IdCounterparty = _panels[0].IdCounterparty, IdCurrency = _receipt.IdCurrency,
            NetPayableAmount = 0.45m, Status = "Draft", SignatureStatus = "Draft",
            CreatedBy = "back-user", CreatedAt = DateTime.UtcNow, IsActive = true
        };
        _db.RemittanceRefs.AddRange(premium, avoir);
        await _db.SaveChangesAsync();
        var service = Service(_db);
        var request = new RemittanceBatchConfirmationRequestDto { RemittanceIds = [premium.RemittanceRef1, avoir.RemittanceRef1] };

        var result = await service.ConfirmRemittancesForDisbursementAsync(request, default);
        var retry = await service.ConfirmRemittancesForDisbursementAsync(request, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.True(retry.Succeeded, retry.Error?.Message);
        Assert.Equal(result.Value!.BatchId, retry.Value!.BatchId);
        Assert.NotEqual(0, result.Value.BatchId);
        Assert.Equal(_panels[0].IdCounterparty, result.Value.ReinsurerId);
        Assert.Equal("AMI", result.Value.ReinsurerName);
        Assert.Equal("TND", result.Value.CurrencyCode);
        Assert.Equal(200m, result.Value.PremiumTotal);
        Assert.Equal(0.45m, result.Value.AvoirTotal);
        Assert.Equal(199.55m, result.Value.NetPayableAmount);
        Assert.Equal("AwaitingFinanceDisbursement", result.Value.Status);
        Assert.Equal(new[] { $"REM-{premium.RemittanceRef1}", $"REM-{avoir.RemittanceRef1}" }, result.Value.RemittanceReferences);
        Assert.Equal(1, await _db.OutgoingClosings.CountAsync());
        Assert.Equal(2, await _db.OutgoingClosingLines.CountAsync());
        Assert.Equal(new[] { -0.45m, 200m }, await _db.OutgoingClosingLines.OrderBy(line => line.PayableAmount).Select(line => line.PayableAmount).ToArrayAsync());
        Assert.All(await _db.RemittanceRefs.Where(row => row.RemittanceRef1 == premium.RemittanceRef1 || row.RemittanceRef1 == avoir.RemittanceRef1).ToListAsync(),
            row => Assert.Equal("AwaitingFinanceDisbursement", row.Status));
        Assert.Equal(1, await _db.Notifications.CountAsync(row => row.EntityType == "OUTGOING_CLOSING"
            && row.EntityId == result.Value.BatchId && row.NotificationType == "CLOSING_DISBURSEMENT_REQUIRED"));

        var additional = new RemittanceRef
        {
            SourceType = "PREMIUM", IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty,
            IdCurrency = _receipt.IdCurrency, NetPayableAmount = 1m, Status = "Draft", CreatedBy = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.RemittanceRefs.Add(additional);
        await _db.SaveChangesAsync();
        var overlappingBatch = await service.ConfirmRemittancesForDisbursementAsync(
            new() { RemittanceIds = [premium.RemittanceRef1, avoir.RemittanceRef1, additional.RemittanceRef1] }, default);
        Assert.False(overlappingBatch.Succeeded);
        Assert.Equal(409, overlappingBatch.StatusCode);
        Assert.Equal("REMITTANCE_ALREADY_BATCHED", overlappingBatch.Error!.Code);
    }

    [Fact]
    public async Task BatchConfirmationReturnsSpecificConflictsForCurrencyAndBankAccount()
    {
        var eur = new Currency { CurrencyCode = "EUR", Name = "Euro", DecimalPlaces = 2, IsActive = true };
        _db.Currencies.Add(eur);
        await _db.SaveChangesAsync();
        var premium = new RemittanceRef
        {
            SourceType = "PREMIUM", IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty,
            IdCurrency = _receipt.IdCurrency, NetPayableAmount = 10m, Status = "Draft", CreatedBy = "back-user", CreatedAt = DateTime.UtcNow
        };
        var differentCurrency = new RemittanceRef
        {
            SourceType = "AVOIR", IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty,
            IdCurrency = eur.IdCurrency, NetPayableAmount = 1m, Status = "Draft", CreatedBy = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.RemittanceRefs.AddRange(premium, differentCurrency);
        await _db.SaveChangesAsync();

        var mixedCurrency = await Service(_db).ConfirmRemittancesForDisbursementAsync(
            new() { RemittanceIds = [premium.RemittanceRef1, differentCurrency.RemittanceRef1] }, default);
        Assert.False(mixedCurrency.Succeeded);
        Assert.Equal("MIXED_CURRENCIES", mixedCurrency.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(mixedCurrency.Error.TraceId));

        var account = await _db.CounterpartyBankAccounts.SingleAsync(row => row.IdCounterparty == _panels[0].IdCounterparty);
        account.IsActive = false;
        await _db.SaveChangesAsync();
        var missingAccount = await Service(_db).ConfirmRemittancesForDisbursementAsync(
            new() { RemittanceIds = [premium.RemittanceRef1] }, default);
        Assert.False(missingAccount.Succeeded);
        Assert.Equal(409, missingAccount.StatusCode);
        Assert.Equal("ACTIVE_BANK_ACCOUNT_NOT_FOUND", missingAccount.Error!.Code);
        Assert.Contains("active TND bank account", missingAccount.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(missingAccount.Error.TraceId));
        Assert.Empty(await _db.OutgoingClosings.ToListAsync());
    }

    [Fact]
    public async Task BatchConfirmationReturnsSpecificConflictForDifferentReinsurers()
    {
        var first = new RemittanceRef
        {
            SourceType = "PREMIUM", IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty,
            IdCurrency = _receipt.IdCurrency, NetPayableAmount = 10m, Status = "Draft", CreatedBy = "back-user", CreatedAt = DateTime.UtcNow
        };
        var second = new RemittanceRef
        {
            SourceType = "PREMIUM", IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty,
            IdCurrency = _receipt.IdCurrency, NetPayableAmount = 10m, Status = "Draft", CreatedBy = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.RemittanceRefs.AddRange(first, second);
        await _db.SaveChangesAsync();

        var result = await Service(_db).ConfirmRemittancesForDisbursementAsync(
            new() { RemittanceIds = [first.RemittanceRef1, second.RemittanceRef1] }, default);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("MIXED_REINSURERS", result.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.TraceId));
        Assert.Equal(2, result.Error.Errors.Count());
        Assert.Empty(await _db.OutgoingClosings.ToListAsync());
    }

    [Fact]
    public async Task BatchConfirmationAllowsSameCounterpartyAcrossPremiumAndAvoirRefundAndReturnsSignedSettlementTotals()
    {
        var star = await _db.Counterparties.SingleAsync(x => x.Name == "STAR Assurances");
        var contract = await _db.Contracts.SingleAsync(x => x.IdContract == _invoice.IdContract);
        contract.IdCounterparty = star.Id;
        _db.CounterpartyTypeAssignments.Add(new CounterpartyTypeAssignment { CounterpartyId = star.Id, Type = "Cedent" });

        var avoir = new Invoice
        {
            IdContract = contract.IdContract,
            IdCurrency = _receipt.IdCurrency,
            InvoiceNumber = "AV-2026-000901",
            InvoiceType = "AVOIR",
            GrossPremium = 10m,
            NetPremium = 10m,
            Status = "Issued",
            InvoiceDate = new(2026, 10, 20),
            CreatedBy = "back-user",
            IdUser = "back-user",
            CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoir);
        await _db.SaveChangesAsync();

        var premium = new RemittanceRef
        {
            SourceType = "Premium",
            IdPanel = _panels[1].IdPanel,
            IdCounterparty = star.Id,
            IdCurrency = _receipt.IdCurrency,
            NetPremiumDue = 5m,
            NetPayableAmount = 5m,
            Status = "Draft",
            SignatureStatus = "Pending",
            Description = "Premium obligation",
            CreatedBy = "back-user",
            CreatedAt = DateTime.UtcNow
        };
        var refund = new RemittanceRef
        {
            SourceType = "AVOIR_REFUND",
            SourceInvoiceId = avoir.IdInvoice,
            BeneficiaryType = "CEDENT",
            IdCounterparty = star.Id,
            IdCurrency = _receipt.IdCurrency,
            NetPremiumDue = 10m,
            NetPayableAmount = 10m,
            Status = "Draft",
            SignatureStatus = "Pending",
            Description = "Avoir refund",
            CreatedBy = "back-user",
            CreatedAt = DateTime.UtcNow
        };
        _db.RemittanceRefs.AddRange(premium, refund);
        await _db.SaveChangesAsync();
        _db.AvoirRefundSettlements.Add(new AvoirRefundSettlement
        {
            AvoirInvoiceId = avoir.IdInvoice,
            BankReceiptId = _receipt.IdBankReceipt,
            RemittanceId = refund.RemittanceRef1,
            Amount = 10m,
            IdempotencyKey = "batch-refund-avoir",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "back-user",
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var result = await Service(_db).ConfirmRemittancesForDisbursementAsync(new RemittanceBatchConfirmationRequestDto
        {
            RemittanceIds = [refund.RemittanceRef1, premium.RemittanceRef1]
        }, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(5m, result.Value!.SignedPremiumTotal);
        Assert.Equal(-10m, result.Value.SignedAvoirTotal);
        Assert.Equal(-5m, result.Value.NetBalance);
        Assert.Equal("CREDIT", result.Value.SettlementMode);
        Assert.Equal(0m, result.Value.AmountToDisburse);
        Assert.Equal(0m, result.Value.RemainingAvoirCredit);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.All(await _db.RemittanceRefs.Where(x => x.RemittanceRef1 == refund.RemittanceRef1 || x.RemittanceRef1 == premium.RemittanceRef1).ToListAsync(),
            x => Assert.Equal("AwaitingFinanceDisbursement", x.Status));
    }

    [Fact]
    public async Task AvoirRefundTracksPartialUseAndRejectsCurrencyMismatch()
    {
        _receipt.AmountReceived = 45m;
        _invoice.InvoiceType = "INVOICE";
        _invoice.GrossPremium = 45m;
        var avoir = new Invoice
        {
            IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency,
            InvoiceNumber = "AV-2026-000202", InvoiceType = "AVOIR", GrossPremium = 10m,
            NetPremium = 10m, Status = "Issued", CreatedBy = "back-user", IdUser = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoir);
        await _db.SaveChangesAsync();
        _db.InvoicePanelAllocations.AddRange(
            new InvoicePanelAllocation { IdInvoice = avoir.IdInvoice, IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty, ReinsurerNameSnapshot = "AMI", AllocationRatio = 0.5m, GrossPremium = 5m, NetAmount = 5m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow },
            new InvoicePanelAllocation { IdInvoice = avoir.IdInvoice, IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty, ReinsurerNameSnapshot = "STAR Assurances", AllocationRatio = 0.5m, GrossPremium = 5m, NetAmount = 5m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        _db.ReceiptAllocations.Add(new ReceiptAllocation
        {
            TargetType = "INVOICE", IdBankReceipt = _receipt.IdBankReceipt, IdInvoice = _invoice.IdInvoice,
            IdContract = _invoice.IdContract, IdCurrency = _receipt.IdCurrency, ExchangeRate = 1m,
            AllocatedAmount = 45m, PairedAt = DateTime.UtcNow, IdUser = "back-user", IsActive = true
        });
        await _db.SaveChangesAsync();

        var service = Service(_db);
        var first = await service.RefundAvoirAsync(_receipt.IdBankReceipt,
            new AvoirRefundRequestDto { AvoirInvoiceId = avoir.IdInvoice, Amount = 4m, IdempotencyKey = "partial-refund-1" }, default);
        Assert.True(first.Succeeded, first.Error?.Message);
        Assert.Equal(6m, first.Value!.AvoirRemainingAmount);
        Assert.Equal(InvoiceRecoveryCalculator.PartiallySettled, await _db.Invoices.AsNoTracking().Where(x => x.IdInvoice == avoir.IdInvoice).Select(x => x.RecoveryStatus).SingleAsync());

        var otherCurrency = new Currency { CurrencyCode = "EUR", Name = "Euro", DecimalPlaces = 2, IsActive = true };
        _db.Currencies.Add(otherCurrency);
        await _db.SaveChangesAsync();
        avoir.IdCurrency = otherCurrency.IdCurrency;
        await _db.SaveChangesAsync();
        var mismatch = await service.RefundAvoirAsync(_receipt.IdBankReceipt,
            new AvoirRefundRequestDto { AvoirInvoiceId = avoir.IdInvoice, Amount = 1m, IdempotencyKey = "currency-mismatch-refund" }, default);
        Assert.False(mismatch.Succeeded);
        Assert.Contains("currency does not match", mismatch.Error!.Message, StringComparison.OrdinalIgnoreCase);
        avoir.IdCurrency = _receipt.IdCurrency;
        await _db.SaveChangesAsync();

        var differentCedent = new Counterparty { Name = "Different Cedent", Type = "Cedent", IsActive = true, Status = "Active", CreatedAtUtc = DateTime.UtcNow };
        _db.Counterparties.Add(differentCedent);
        await _db.SaveChangesAsync();
        _db.CounterpartyTypeAssignments.Add(new CounterpartyTypeAssignment { CounterpartyId = differentCedent.Id, Type = "Cedent" });
        var differentContract = new Contract
        {
            Name = "Different cedent contract", Umr = "UMR-OTHER-CEDENT", TypeOfBusiness = "Facultative", Status = "Bound",
            IdCounterparty = differentCedent.Id, IdCurrency = _receipt.IdCurrency, CreatedBy = "back-user", IdUser = "back-user",
            CreatedAt = DateTime.UtcNow, InceptionDate = new(2026, 1, 1), ExpiryDate = new(2026, 12, 31)
        };
        _db.Contracts.Add(differentContract);
        await _db.SaveChangesAsync();
        var differentCedentAvoir = new Invoice
        {
            IdContract = differentContract.IdContract, IdCurrency = _receipt.IdCurrency, InvoiceNumber = "AV-OTHER-CEDENT",
            InvoiceType = "AVOIR", GrossPremium = 10m, NetPremium = 10m, Status = "Issued", CreatedBy = "back-user",
            IdUser = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(differentCedentAvoir);
        await _db.SaveChangesAsync();
        var cedentMismatch = await service.RefundAvoirAsync(_receipt.IdBankReceipt,
            new AvoirRefundRequestDto { AvoirInvoiceId = differentCedentAvoir.IdInvoice, Amount = 1m, IdempotencyKey = "cedent-mismatch-refund" }, default);
        Assert.False(cedentMismatch.Succeeded);
        Assert.Contains("cedent or currency does not match", cedentMismatch.Error!.Message, StringComparison.OrdinalIgnoreCase);

        var second = await service.RefundAvoirAsync(_receipt.IdBankReceipt,
            new AvoirRefundRequestDto { AvoirInvoiceId = avoir.IdInvoice, Amount = 7m, IdempotencyKey = "partial-refund-too-large" }, default);
        Assert.False(second.Succeeded);
        Assert.Contains("unsettled balance of 6", second.Error!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await _db.AvoirRefundSettlements.CountAsync());
        Assert.Equal(2, await _db.RemittanceRefs.Where(x => x.SourceType == "AVOIR" && x.SourceInvoiceId == avoir.IdInvoice).CountAsync());
    }

    [Fact]
    public async Task AvoirRefundEligibilityIncludesUnpairedPartiallyPairedAndFullyPairedCedentReceipts()
    {
        _receipt.AmountReceived = 100m;
        _receipt.DetailsStatus = "Unpaired";
        var avoir = new Invoice
        {
            IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency,
            InvoiceNumber = "AV-ELIGIBILITY-ALL", InvoiceType = "AVOIR", GrossPremium = 10m,
            NetPremium = 10m, Status = "Issued", CreatedBy = "back-user", IdUser = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoir);
        var partialReceipt = new BankReceipt
        {
            IdBankAccount = _receipt.IdBankAccount, IdCurrency = _receipt.IdCurrency, IdCounterparty = _receipt.IdCounterparty,
            AmountReceived = 100m, SettlementMethod = "BankTransfer", CollectionType = "Premium", CreatedBy = "back-user",
            CreatedAt = DateTime.UtcNow, DetailsStatus = "PartiallyPaired"
        };
        var pairedReceipt = new BankReceipt
        {
            IdBankAccount = _receipt.IdBankAccount, IdCurrency = _receipt.IdCurrency, IdCounterparty = _receipt.IdCounterparty,
            AmountReceived = 25m, SettlementMethod = "BankTransfer", CollectionType = "Premium", CreatedBy = "back-user",
            CreatedAt = DateTime.UtcNow, DetailsStatus = "Paired"
        };
        _db.BankReceipts.AddRange(partialReceipt, pairedReceipt);
        await _db.SaveChangesAsync();
        _db.ReceiptAllocations.AddRange(
            new ReceiptAllocation { TargetType = "INVOICE", IdBankReceipt = partialReceipt.IdBankReceipt, IdInvoice = _invoice.IdInvoice, IdContract = _invoice.IdContract, IdCurrency = _receipt.IdCurrency, ExchangeRate = 1m, AllocatedAmount = 20m, PairedAt = DateTime.UtcNow, IdUser = "back-user", IsActive = true },
            new ReceiptAllocation { TargetType = "INVOICE", IdBankReceipt = pairedReceipt.IdBankReceipt, IdInvoice = _invoice.IdInvoice, IdContract = _invoice.IdContract, IdCurrency = _receipt.IdCurrency, ExchangeRate = 1m, AllocatedAmount = 25m, PairedAt = DateTime.UtcNow, IdUser = "back-user", IsActive = true });

        var otherCurrency = new Currency { CurrencyCode = "EUR", Name = "Euro", DecimalPlaces = 2, IsActive = true };
        var otherCedent = new Counterparty { Name = "Other cedent", Type = "Cedent", IsActive = true, Status = "Active", CreatedAtUtc = DateTime.UtcNow };
        _db.AddRange(otherCurrency, otherCedent);
        await _db.SaveChangesAsync();
        _db.CounterpartyTypeAssignments.Add(new CounterpartyTypeAssignment { CounterpartyId = otherCedent.Id, Type = "Cedent" });
        _db.BankReceipts.AddRange(
            new BankReceipt { IdBankAccount = _receipt.IdBankAccount, IdCurrency = otherCurrency.IdCurrency, IdCounterparty = _receipt.IdCounterparty, AmountReceived = 40m, SettlementMethod = "BankTransfer", CollectionType = "Premium", CreatedBy = "back-user", CreatedAt = DateTime.UtcNow, DetailsStatus = "Unpaired" },
            new BankReceipt { IdBankAccount = _receipt.IdBankAccount, IdCurrency = _receipt.IdCurrency, IdCounterparty = otherCedent.Id, AmountReceived = 40m, SettlementMethod = "BankTransfer", CollectionType = "Premium", CreatedBy = "back-user", CreatedAt = DateTime.UtcNow, DetailsStatus = "Unpaired" });
        await _db.SaveChangesAsync();

        var result = await Service(_db).GetEligibleAvoirRefundReceiptsAsync(avoir.IdInvoice, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(3, result.Value!.Count);
        Assert.DoesNotContain(result.Value, item => item.ReceiptAmount == 40m);
        Assert.Contains(result.Value, item => item.BankReceiptId == _receipt.IdBankReceipt && item.PairingStatus == "UNPAIRED" && item.RemainingAmount == 100m && item.IsEligible);
        Assert.Contains(result.Value, item => item.BankReceiptId == partialReceipt.IdBankReceipt && item.PairingStatus == "PARTIALLY_PAIRED" && item.AllocatedAmount == 20m && item.RemainingAmount == 80m && item.IsEligible);
        Assert.Contains(result.Value, item => item.BankReceiptId == pairedReceipt.IdBankReceipt && item.PairingStatus == "PAIRED" && item.AllocatedAmount == 25m && item.RemainingAmount == 0m && item.IsEligible);
    }

    [Fact]
    public async Task AvoirRefundCanSettleAgainstUnpairedReceiptWithoutChangingReceiptAccounting()
    {
        _receipt.AmountReceived = 45m;
        _receipt.DetailsStatus = "Unpaired";
        var originalAllocationIds = await _db.ReceiptAllocations.Where(item => item.IdBankReceipt == _receipt.IdBankReceipt).Select(item => item.IdReceiptAllocation).ToListAsync();
        var avoir = new Invoice
        {
            IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency,
            InvoiceNumber = "AV-UNPAIRED-RECEIPT", InvoiceType = "AVOIR", GrossPremium = 10m,
            NetPremium = 10m, Status = "Issued", CreatedBy = "back-user", IdUser = "back-user", CreatedAt = DateTime.UtcNow
        };
        await _db.Invoices.AddAsync(avoir);
        await _db.SaveChangesAsync();
        _db.InvoicePanelAllocations.AddRange(
            new InvoicePanelAllocation { IdInvoice = avoir.IdInvoice, IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty, ReinsurerNameSnapshot = "AMI", AllocationRatio = 0.5m, GrossPremium = 5m, NetAmount = 5m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow },
            new InvoicePanelAllocation { IdInvoice = avoir.IdInvoice, IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty, ReinsurerNameSnapshot = "STAR Assurances", AllocationRatio = 0.5m, GrossPremium = 5m, NetAmount = 5m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var request = new AvoirRefundRequestDto { AvoirInvoiceId = avoir.IdInvoice, Amount = 10m, IdempotencyKey = "avoir-unpaired-receipt" };
        var result = await Service(_db).RefundAvoirAsync(_receipt.IdBankReceipt, request, default);
        var retry = await Service(_db).RefundAvoirAsync(_receipt.IdBankReceipt, request, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.True(retry.Succeeded, retry.Error?.Message);
        Assert.Equal(result.Value!.RemittanceId, retry.Value!.RemittanceId);
        Assert.Equal("UNPAIRED", result.Value.PairingStatus);
        Assert.Equal("Unpaired", await _db.BankReceipts.AsNoTracking().Where(item => item.IdBankReceipt == _receipt.IdBankReceipt).Select(item => item.DetailsStatus).SingleAsync());
        Assert.Equal(45m, await _db.BankReceipts.AsNoTracking().Where(item => item.IdBankReceipt == _receipt.IdBankReceipt).Select(item => item.AmountReceived).SingleAsync());
        Assert.Equal(originalAllocationIds, await _db.ReceiptAllocations.Where(item => item.IdBankReceipt == _receipt.IdBankReceipt).Select(item => item.IdReceiptAllocation).ToListAsync());
        var remittances = await _db.RemittanceRefs.Where(item => item.SourceInvoiceId == avoir.IdInvoice).OrderBy(item => item.IdPanel).ToListAsync();
        Assert.Equal(2, remittances.Count);
        Assert.All(remittances, item => Assert.Equal("AVOIR", item.SourceType));
        Assert.All(remittances, item => Assert.NotEqual(_receipt.IdCounterparty, item.IdCounterparty));
        Assert.Equal(1, await _db.Notifications.CountAsync(item => item.EntityId == result.Value.RemittanceId && item.NotificationType == "AvoirRemittancesRequireReview"));

        var duplicate = await Service(_db).RefundAvoirAsync(_receipt.IdBankReceipt,
            new AvoirRefundRequestDto { AvoirInvoiceId = avoir.IdInvoice, Amount = 1m, IdempotencyKey = "avoir-unpaired-duplicate" }, default);
        Assert.False(duplicate.Succeeded);
        Assert.Equal(2, await _db.AvoirRefundSettlements.Where(item => item.AvoirInvoiceId == avoir.IdInvoice).CountAsync());
    }

    [Fact]
    public async Task MixedPremiumAndAvoirPairingCreatesSeparateSignedPanelRemittances()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 100m;
        contract.CedentCommission = 0m;
        _invoice.InvoiceType = "INVOICE";
        _invoice.GrossPremium = 100m;
        _invoice.NetPremium = 100m;
        _invoice.Status = "Issued";
        _receipt.AmountReceived = 50m;
        _panels[0].Status = "Confirmed";
        _panels[1].Status = "Confirmed";
        _db.InvoicePanelAllocations.RemoveRange(await _db.InvoicePanelAllocations.Where(x => x.IdInvoice == _invoice.IdInvoice).ToListAsync());
        _db.InvoicePanelAllocations.AddRange(
            new InvoicePanelAllocation
            {
                IdInvoice = _invoice.IdInvoice, IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty,
                ReinsurerNameSnapshot = "AMI", SignedSharePctSnapshot = 60m, AllocationRatio = 0.6m,
                GrossPremium = 60m, NetAmount = 60m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow
            },
            new InvoicePanelAllocation
            {
                IdInvoice = _invoice.IdInvoice, IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty,
                ReinsurerNameSnapshot = "STAR Assurances", SignedSharePctSnapshot = 40m, AllocationRatio = 0.4m,
                GrossPremium = 40m, NetAmount = 40m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        var service = Service(_db);
        var avoir = await service.CreatePremiumInvoiceAsync(contract.IdContract, new PremiumDebitNoteRequestDto
        {
            InvoiceType = "AVOIR", ProcessingMode = "PAIR_WITH_ENCAISSEMENT", GrossAmount = 50m, InvoiceDate = new(2026, 10, 19), IdempotencyKey = "remittance-credit"
        }, default);
        Assert.True(avoir.Succeeded, avoir.Error?.Message);
        Assert.Equal(new[] { 30m, 20m }, avoir.Value!.Allocations.OrderBy(line => line.PanelId).Select(line => line.AllocationAmount));
        Assert.Equal(new[] { -30m, -20m }, avoir.Value.Allocations.OrderBy(line => line.PanelId).Select(line => line.SignedAllocationAmount));

        var paired = await service.AllocateReceiptInvoicesAsync(_receipt.IdBankReceipt, new InvoiceReceiptAllocationRequestDto
        {
            Allocations =
            [
                new InvoiceReceiptAllocationItemDto { InvoiceId = _invoice.IdInvoice, AllocatedAmount = 100m },
                new InvoiceReceiptAllocationItemDto { InvoiceId = avoir.Value.InvoiceId, AllocatedAmount = 50m }
            ]
        }, default);

        Assert.True(paired.Succeeded, paired.Error?.Message);
        Assert.Equal(50m, paired.Value!.SignedPairingTotal);
        var receiptAllocation = Assert.Single(await _db.ReceiptAllocations.Where(row => row.IdBankReceipt == _receipt.IdBankReceipt).ToListAsync());
        Assert.Equal(50m, receiptAllocation.AllocatedAmount);
        var remittances = await _db.RemittanceRefs.Where(row => row.IdReceiptAllocation == receiptAllocation.IdReceiptAllocation)
            .OrderBy(row => row.SourceType).ThenBy(row => row.IdPanel).ToListAsync();
        Assert.Equal(4, remittances.Count);
        var premiums = remittances.Where(row => row.SourceType == "Premium").ToList();
        var avoirs = remittances.Where(row => row.SourceType == "AVOIR").ToList();
        Assert.Equal(new[] { 60m, 40m }, premiums.Select(row => row.NetPayableAmount ?? 0m));
        Assert.Equal(new[] { 30m, 20m }, avoirs.Select(row => row.NetPayableAmount ?? 0m));
        Assert.All(remittances, row => Assert.Equal("REINSURER", row.BeneficiaryType));
        Assert.All(remittances, row => Assert.NotNull(row.IdPanel));
        Assert.Equal(50m, premiums.Sum(row => Math.Abs(row.NetPayableAmount ?? 0m))
            - avoirs.Sum(row => Math.Abs(row.NetPayableAmount ?? 0m)));
        var responseLines = paired.Value.CreatedRemittances;
        Assert.Equal(4, responseLines.Count);
        Assert.Equal(new[] { -30m, -20m }, responseLines.Where(row => row.SourceType == "AVOIR")
            .OrderBy(row => row.PanelId).Select(row => row.SignedNetPayableAmount));
        Assert.DoesNotContain(await _db.ReceiptAllocations.ToListAsync(), row => row.IdInvoice == avoir.Value.InvoiceId);
    }

    [Fact]
    public async Task AvoirAggregateRepairSupersedesDraftAndIsIdempotent()
    {
        var avoir = new Invoice
        {
            IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = "AV-REPAIR-1",
            InvoiceType = "AVOIR", InvoiceDate = new(2026, 10, 20), GrossPremium = 10m, NetPremium = 10m,
            Status = "Issued", CreatedBy = "back-user", IdUser = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoir);
        await _db.SaveChangesAsync();
        _db.InvoicePanelAllocations.AddRange(
            new InvoicePanelAllocation { IdInvoice = avoir.IdInvoice, IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty, ReinsurerNameSnapshot = "AMI", AllocationRatio = 0.5m, GrossPremium = 5m, NetAmount = 5m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow },
            new InvoicePanelAllocation { IdInvoice = avoir.IdInvoice, IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty, ReinsurerNameSnapshot = "STAR Assurances", AllocationRatio = 0.5m, GrossPremium = 5m, NetAmount = 5m, CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow });
        var aggregate = new RemittanceRef
        {
            SourceType = "AVOIR_REFUND", SourceInvoiceId = avoir.IdInvoice, BeneficiaryType = "CEDENT",
            IdCounterparty = _receipt.IdCounterparty, IdCurrency = _receipt.IdCurrency, NetPayableAmount = 10m,
            Status = "Draft", SignatureStatus = "Draft", CreatedAt = DateTime.UtcNow, CreatedBy = "back-user", IsActive = true
        };
        var closedAggregate = new RemittanceRef
        {
            SourceType = "AVOIR_REFUND", SourceInvoiceId = avoir.IdInvoice, BeneficiaryType = "CEDENT",
            IdCounterparty = _receipt.IdCounterparty, IdCurrency = _receipt.IdCurrency, NetPayableAmount = 10m,
            Status = "Closed", SignatureStatus = "Closed", CreatedAt = DateTime.UtcNow, CreatedBy = "back-user", IsActive = true
        };
        _db.RemittanceRefs.AddRange(aggregate, closedAggregate);
        await _db.SaveChangesAsync();
        _db.AvoirRefundSettlements.Add(new AvoirRefundSettlement
        {
            AvoirInvoiceId = avoir.IdInvoice, BankReceiptId = _receipt.IdBankReceipt, RemittanceId = aggregate.RemittanceRef1,
            Amount = 10m, IdempotencyKey = "legacy-aggregate-repair", CreatedAt = DateTime.UtcNow, CreatedBy = "back-user", IsActive = true
        });
        await _db.SaveChangesAsync();

        var service = Service(_db);
        var first = await service.RepairDraftAvoirRemittancesAsync(default);
        var retry = await service.RepairDraftAvoirRemittancesAsync(default);

        Assert.True(first.Succeeded, first.Error?.Message);
        Assert.Equal(1, first.Value!.DraftAggregatesSuperseded);
        Assert.Equal(2, first.Value.PanelRemittancesCreated);
        Assert.Contains(aggregate.RemittanceRef1, first.Value.SupersededRemittanceIds);
        Assert.Contains(closedAggregate.RemittanceRef1, first.Value.PaidOrClosedLegacyRemittanceIds);
        Assert.True(retry.Succeeded, retry.Error?.Message);
        Assert.Equal(0, retry.Value!.DraftAggregatesSuperseded);
        Assert.Equal(2, await _db.RemittanceRefs.CountAsync(row => row.SourceInvoiceId == avoir.IdInvoice && row.SourceType == "AVOIR" && row.IdPanel.HasValue));
        Assert.Equal("Cancelled", await _db.RemittanceRefs.Where(row => row.RemittanceRef1 == aggregate.RemittanceRef1).Select(row => row.Status).SingleAsync());
        Assert.Equal("Closed", await _db.RemittanceRefs.Where(row => row.RemittanceRef1 == closedAggregate.RemittanceRef1).Select(row => row.Status).SingleAsync());
        var linkedReplacement = await _db.AvoirRefundSettlements.Select(row => row.RemittanceId).SingleAsync();
        Assert.NotEqual(aggregate.RemittanceRef1, linkedReplacement);
        Assert.True(await _db.RemittanceRefs.AnyAsync(row => row.RemittanceRef1 == linkedReplacement && row.IdPanel.HasValue));
    }

    [Fact]
    public async Task AvoirRemittanceUsesSavedPanelNetPayableAmounts()
    {
        var avoir = new Invoice
        {
            IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = "AV-SAVED-NET-1",
            InvoiceType = "AVOIR", InvoiceDate = new(2026, 10, 21), GrossPremium = 5m, NetPremium = 5m,
            Status = "Issued", CreatedBy = "back-user", IdUser = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoir);
        await _db.SaveChangesAsync();
        _db.InvoicePanelAllocations.AddRange(
            new InvoicePanelAllocation
            {
                IdInvoice = avoir.IdInvoice, IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty,
                ReinsurerNameSnapshot = "AMI", AllocationRatio = 0.5m, GrossPremium = 2.778m,
                CedentCommissionAmount = 0.278m, NetAmount = 2.5m, BrokerageAmount = 0.25m,
                CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow
            },
            new InvoicePanelAllocation
            {
                IdInvoice = avoir.IdInvoice, IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty,
                ReinsurerNameSnapshot = "STAR Assurances", AllocationRatio = 0.5m, GrossPremium = 2.778m,
                CedentCommissionAmount = 0.278m, NetAmount = 2.5m, BrokerageAmount = 0m,
                CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow
            });
        await _db.SaveChangesAsync();

        var result = await Service(_db).RefundAvoirAsync(_receipt.IdBankReceipt,
            new AvoirRefundRequestDto { AvoirInvoiceId = avoir.IdInvoice, Amount = 5m, IdempotencyKey = "avoir-saved-net-payables" }, default);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new[] { -2.25m, -2.5m }, result.Value!.CreatedRemittances.OrderBy(row => row.PanelId)
            .Select(row => row.SignedNetPayableAmount));
        Assert.Equal(4.75m, result.Value.CreatedRemittances.Sum(row => row.SignedNetPayableAmount) * -1m);
        Assert.All(await _db.RemittanceRefs.Where(row => row.SourceInvoiceId == avoir.IdInvoice).ToListAsync(), row =>
        {
            Assert.Equal("AVOIR", row.SourceType);
            Assert.Equal("REINSURER", row.BeneficiaryType);
            Assert.NotNull(row.IdPanel);
        });
    }

    [Fact]
    public async Task PremiumAndAvoirCanNetOnlyForTheSameReinsurer()
    {
        _receipt.AmountReceived = 100m;
        await _db.SaveChangesAsync();
        await AddAllocation(100m);
        var origin = await _db.ReceiptAllocations.SingleAsync();
        var premiumAllocation = await _db.InvoicePanelAllocations.SingleAsync(line => line.IdInvoice == _invoice.IdInvoice && line.IdPanel == _panels[0].IdPanel);
        var avoir = new Invoice
        {
            IdContract = _invoice.IdContract, IdCurrency = _invoice.IdCurrency, InvoiceNumber = "AV-NET-1",
            InvoiceType = "AVOIR", InvoiceDate = new(2026, 10, 21), GrossPremium = 5m, NetPremium = 5m,
            Status = "Issued", CreatedBy = "back-user", IdUser = "back-user", CreatedAt = DateTime.UtcNow
        };
        _db.Invoices.Add(avoir);
        await _db.SaveChangesAsync();
        var avoirAllocation = new InvoicePanelAllocation
        {
            IdInvoice = avoir.IdInvoice, IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty,
            ReinsurerNameSnapshot = "AMI", AllocationRatio = 1m, GrossPremium = 5m, NetAmount = 5m,
            CurrencyCodeSnapshot = "TND", CreatedAt = DateTime.UtcNow
        };
        _db.InvoicePanelAllocations.Add(avoirAllocation);
        await _db.SaveChangesAsync();
        var premium = new RemittanceRef
        {
            IdReceiptAllocation = origin.IdReceiptAllocation, IdInvoiceAllocation = premiumAllocation.IdInvoiceAllocation,
            SourceInvoiceId = _invoice.IdInvoice, SourceType = "Premium", BeneficiaryType = "REINSURER",
            IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty, IdCurrency = _receipt.IdCurrency,
            NetPayableAmount = 5m, Status = "Draft", SignatureStatus = "Draft", CreatedAt = DateTime.UtcNow, CreatedBy = "back-user"
        };
        var credit = new RemittanceRef
        {
            IdReceiptAllocation = origin.IdReceiptAllocation, IdInvoiceAllocation = avoirAllocation.IdInvoiceAllocation,
            SourceInvoiceId = avoir.IdInvoice, SourceType = "AVOIR", BeneficiaryType = "REINSURER",
            IdPanel = _panels[0].IdPanel, IdCounterparty = _panels[0].IdCounterparty, IdCurrency = _receipt.IdCurrency,
            NetPayableAmount = 2.5m, Status = "Draft", SignatureStatus = "Draft", CreatedAt = DateTime.UtcNow, CreatedBy = "back-user"
        };
        var otherReinsurer = new RemittanceRef
        {
            IdReceiptAllocation = origin.IdReceiptAllocation, IdInvoiceAllocation = _db.InvoicePanelAllocations.Single(line => line.IdInvoice == _invoice.IdInvoice && line.IdPanel == _panels[1].IdPanel).IdInvoiceAllocation,
            SourceInvoiceId = _invoice.IdInvoice, SourceType = "Premium", BeneficiaryType = "REINSURER",
            IdPanel = _panels[1].IdPanel, IdCounterparty = _panels[1].IdCounterparty, IdCurrency = _receipt.IdCurrency,
            NetPayableAmount = 5m, Status = "Draft", SignatureStatus = "Draft", CreatedAt = DateTime.UtcNow, CreatedBy = "back-user"
        };
        _db.RemittanceRefs.AddRange(premium, credit, otherReinsurer);
        await _db.SaveChangesAsync();

        var service = Service(_db);
        var mixedBeneficiaries = await service.CreateOutgoingClosingAsync(new() { RemittanceIds = [premium.RemittanceRef1, otherReinsurer.RemittanceRef1] }, default);
        Assert.False(mixedBeneficiaries.Succeeded);
        Assert.Contains("same reinsurer", mixedBeneficiaries.Error!.Message, StringComparison.OrdinalIgnoreCase);

        var closingResult = await service.CreateOutgoingClosingAsync(new() { RemittanceIds = [premium.RemittanceRef1, credit.RemittanceRef1] }, default);
        Assert.True(closingResult.Succeeded, closingResult.Error?.Message);
        var closing = await _db.OutgoingClosings.Include(item => item.Lines).SingleAsync();
        Assert.Equal(2.5m, closing.TotalAmount);
        Assert.Equal(new[] { -2.5m, 5m }, closing.Lines.OrderBy(line => line.Remittance.SourceType).Select(line => line.PayableAmount));
    }

    [Fact]
    public async Task ContractDocumentRemainsDebitNoteUntilActiveAvoirExists()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 1000m;
        contract.CedentCommission = 10m;
        contract.NetPremiumCedent = 900m;
        var service = Service(_db, bankAccounts: TestIrisBankAccounts());
        var debitNote = await service.GetContractFinancialNoteAsync(contract.IdContract, default);
        Assert.True(debitNote.Succeeded, debitNote.Error?.Message);
        Assert.StartsWith("DebitNote_DN-", debitNote.Value!.FileName);
        Assert.Contains("900,00", Encoding.Latin1.GetString(debitNote.Value.Content));

        _db.Invoices.Add(new Invoice
        {
            IdContract = contract.IdContract,
            IdCurrency = contract.IdCurrency,
            InvoiceNumber = "AV-2026-000100",
            InvoiceType = "AVOIR",
            InvoiceDate = new(2026, 10, 16),
            GrossPremium = 10m,
            NetPremium = 10m,
            Status = "Issued",
            CreatedBy = "back-user",
            IdUser = "back-user",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var creditNote = await service.GetContractFinancialNoteAsync(contract.IdContract, default);
        Assert.True(creditNote.Succeeded, creditNote.Error?.Message);
        Assert.StartsWith("CreditNote-AV-2026-000100", creditNote.Value!.FileName);
        Assert.Equal("application/pdf", creditNote.Value.ContentType);
    }

    private IRISBackendContext Context(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<IRISBackendContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={_database};TrustServerCertificate=True;MultipleActiveResultSets=True;");
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options);
    }

    private async Task ConfigureDirectAvoirContractAsync()
    {
        var contract = await _db.Contracts.SingleAsync(item => item.IdContract == _invoice.IdContract);
        contract.GrossPremium = 1000m;
        contract.NetPremiumCedent = 1000m;
        contract.CedentCommission = 0m;
        contract.SignedSharePctCedent = 100m;
        _invoice.Status = "Issued";
        _panels[0].SignedSharePct = 60m;
        _panels[0].GrossPremium = 600m;
        _panels[0].BrokeragePct = 10m;
        _panels[0].Status = "Confirmed";
        _panels[1].SignedSharePct = 40m;
        _panels[1].GrossPremium = 400m;
        _panels[1].BrokeragePct = 20m;
        _panels[1].Status = "Confirmed";
        if (!await _db.ContractFacs.AnyAsync(item => item.IdContract == contract.IdContract))
            _db.ContractFacs.Add(new ContractFac
            {
                IdContract = contract.IdContract,
                SumInsured = 100000m,
                PremiumRate = 1m
            });
        await _db.SaveChangesAsync();
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

    private static IrisBankAccountsOptions TestIrisBankAccounts() => new()
    {
        Accounts =
        [
            new() { AccountHolder = "IRIS Tunisia", BankName = "Test Bank TND", Currency = "TND", Rib = "RIB-TND", Iban = "TNTESTIBAN0000000000000001", SwiftBic = "TESTTN01", Domiciliation = "Tunis", DisplayOrder = 1, IsActive = true },
            new() { AccountHolder = "IRIS Tunisia", BankName = "Test Bank USD", Currency = "USD", Iban = "TNTESTIBAN0000000000000002", SwiftBic = "TESTUS02", Domiciliation = "Tunis", DisplayOrder = 2, IsActive = true },
            new() { AccountHolder = "IRIS Tunisia", BankName = "Test Bank EUR", Currency = "EUR", Iban = "TNTESTIBAN0000000000000003", SwiftBic = "TESTEU03", Domiciliation = "Tunis", DisplayOrder = 3, IsActive = true }
        ]
    };

    private sealed class RequestAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
