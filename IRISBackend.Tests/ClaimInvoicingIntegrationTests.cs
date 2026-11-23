using System.IO.Compression;
using System.Text;
using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IRISBackend.Tests;

public sealed partial class OutgoingRemittanceIntegrationTests
{
    private async Task<ClaimResponseDto> ReadyClaimInvoicing()
    {
        var request = await PrepareClaimRequest(15000m, 2500m);
        request.Deductible = 2500m;
        var created = await Service(_db, "Front Officer").SaveClaimAsync(_invoice.IdContract, null, request, default);
        Assert.True(created.Succeeded, created.Error?.Message + string.Join(";", created.Error?.Errors ?? []));
        return created.Value!;
    }
    private async Task<ClaimInvoicingRequestDto> ClaimDecisions(ClaimResponseDto c)
    {
        var preview = await Service(_db).PreviewClaimInvoicingAsync(c.ClaimId, default);
        Assert.True(preview.Succeeded, preview.Error?.Message);
        return new()
        {
            Label = "  Settlement of storm claim  ",
            Version = preview.Value!.Claim.Version,
            Allocations = preview.Value.Panels.Select(p => new ClaimAllocationDto { PanelId = p.PanelId, Amount = p.SuggestedAmount }).ToList()
        };
    }

    [Fact]
    public async Task ClaimInvoicingCurrencyDeductibleValidationAndNotifications()
    {
        var req = await PrepareClaimRequest();
        var usd = new Currency { CurrencyCode = "USD", Name = "US Dollar", CurrencySymbol = "$", DecimalPlaces = 2, IsActive = true };
        _db.Currencies.Add(usd); await _db.SaveChangesAsync(); req.CurrencyId = usd.IdCurrency;
        var front = Service(_db, "Front Officer"); var back = Service(_db);
        foreach (var deductible in new decimal?[] { null, 0m, 5.1234m })
        {
            req.Deductible = deductible;
            var created = await front.SaveClaimAsync(_invoice.IdContract, null, req, default);
            Assert.True(created.Succeeded, created.Error?.Message); var c = created.Value!;
            Assert.Equal(usd.IdCurrency, c.CurrencyId); Assert.Equal("USD", c.CurrencyCode); Assert.Equal("$", c.CurrencySymbol);
            Assert.Equal(deductible, c.Deductible); Assert.Equal(10000m, c.CollectionAmount);
            Assert.Equal(PremiumDebitNoteFinancials.RoundMoney(10500m - (deductible ?? 0m), 2), c.NetLoss);
            var repeated = await front.SaveClaimAsync(_invoice.IdContract, c.ClaimId, req, default); Assert.True(repeated.Succeeded);
            Assert.Single(await _db.Notifications.Where(n => n.EntityId == c.ClaimId && n.NotificationType == "ClaimReady").ToListAsync());
            var notification = await _db.Notifications.SingleAsync(n => n.EntityId == c.ClaimId && n.NotificationType == "ClaimReady");
            Assert.EndsWith("claims?claimId=" + c.ClaimId, notification.TargetRoute); Assert.Contains("paid and ready for invoicing", notification.Message); Assert.Contains("USD", notification.Message);
            Assert.Equal(403, (await back.SaveClaimAsync(_invoice.IdContract, c.ClaimId, req, default)).StatusCode);
        }
        foreach (var invalid in new[] { -1m, 0.00001m }) { req.Deductible = invalid; Assert.Equal(400, (await front.SaveClaimAsync(_invoice.IdContract, null, req, default)).StatusCode); }
        req.Deductible = null; req.PaidLoss = null; Assert.Equal(400, (await front.SaveClaimAsync(_invoice.IdContract, null, req, default)).StatusCode);
        req.PaidLoss = 10000m; usd.IsActive = false; await _db.SaveChangesAsync(); Assert.Equal(400, (await front.SaveClaimAsync(_invoice.IdContract, null, req, default)).StatusCode);
        req.CurrencyId = -1; Assert.Equal(400, (await front.SaveClaimAsync(_invoice.IdContract, null, req, default)).StatusCode);
        var list = await back.ListClaimsAsync(null, 1, 20, false, default, new() { CurrencyId = usd.IdCurrency, Search = "CL-", Cedent = "Cedent", Contract = "UMR-TEST", InvoicingStatus = "Ready", Sort = "dateOfLoss" });
        Assert.Equal(3, list.Value!.TotalCount);
    }

    [Fact]
    public async Task ClaimInvoicingApprovalIsAtomicIdempotentAndDocumentsRegenerate()
    {
        var c = await ReadyClaimInvoicing(); var req = await ClaimDecisions(c); var back = Service(_db, branding: new IrisInvoiceBrandingOptions { CompanyName = "IRIS Tunisia", AddressLine1 = "Tunis, Tunisia", AddressLine2 = "Development PDF fixture", Phone = "+216 00 000 000", TaxIdentifier = "TEST", CommercialRegister = "TEST", Website = "www.impactrisks.com" }, bankAccounts: TestIrisBankAccounts());
        var preview = await back.PreviewClaimInvoicingAsync(c.ClaimId, default);
        Assert.Equal(15000m, preview.Value!.Claim.NetLoss);
        Assert.Equal("1500.000", preview.Value.AllocationTotalText);
        Assert.Equal(10m, preview.Value.TotalSignedShare);
        Assert.All(preview.Value.Panels, panel => Assert.Equal(750m, panel.SuggestedAmount));
        var result = await back.ApproveClaimInvoicingAsync(c.ClaimId, req, default);
        Assert.True(result.Succeeded, result.Error?.Message); Assert.Equal(3, result.Value!.Documents.Count);
        Assert.All(result.Value.Documents, document => Assert.Equal(result.Value.AvoirNumber, document.Reference));
        var avoir = await _db.Invoices.SingleAsync(i => i.ClaimRef == c.ClaimId);
        Assert.Equal("AVOIR", avoir.InvoiceType); Assert.StartsWith("AV-", avoir.InvoiceNumber); Assert.Equal(c.CurrencyId, avoir.IdCurrency);
        Assert.Equal(1500m, avoir.GrossPremium); Assert.Equal("Settlement of storm claim", avoir.Label);
        Assert.All(await _db.CollectionNotes.ToListAsync(), note => Assert.Equal(750m, note.AmountClaimed));
        Assert.Equal(1500m, (await _db.Claims.FindAsync(c.ClaimId))!.CollectionAmount);
        Assert.Equal(2, await _db.CollectionNotes.CountAsync()); Assert.Equal(1500m, await _db.CollectionNotes.SumAsync(n => n.AmountClaimed));
        Assert.All(await _db.CollectionNotes.ToListAsync(), n => Assert.Equal(n.IdClaim, n.ClaimRef));
        var remits = await _db.RemittanceRefs.ToListAsync(); Assert.Equal(2, remits.Count);
        Assert.All(remits, r => { Assert.Equal("Claim", r.SourceType); Assert.NotNull(r.IdCollectionNote); Assert.Null(r.IdDisbursement); Assert.Equal(c.CurrencyId, r.IdCurrency); });
        Assert.All(remits, remittance => Assert.Equal(avoir.IdInvoice, remittance.SourceInvoiceId));
        var listedRemittances=await back.ListRemittancesAsync(new(){Search=avoir.InvoiceNumber},default);
        Assert.Equal(2,listedRemittances.Value!.Items.Count());
        Assert.All(listedRemittances.Value.Items, row => {
            Assert.Equal(avoir.InvoiceNumber,row.InvoiceAvoirNumber);Assert.Equal(avoir.InvoiceNumber,row.SourceInvoiceNumber);
            Assert.Equal(avoir.Label,row.Label);Assert.Equal("Claim",row.SourceType);
        });
        Assert.True((await back.ApproveClaimInvoicingAsync(c.ClaimId, req, default)).Succeeded);
        req.Label = "Different"; Assert.Equal(409, (await back.ApproveClaimInvoicingAsync(c.ClaimId, req, default)).StatusCode); req.Label = avoir.Label;
        var unconfigured = Service(_db);
        Assert.Equal(409, (await unconfigured.GetClaimInvoicingDocumentAsync(c.ClaimId, "bundle", null, default)).StatusCode);
        Assert.True((await unconfigured.GetClaimInvoicingDocumentAsync(c.ClaimId, "avoir", null, default)).Succeeded);
        var documents = await back.GetClaimInvoicingDocumentAsync(c.ClaimId, "bundle", null, default);
        Assert.True(documents.Succeeded, documents.Error?.Message);
        using var zip = new ZipArchive(new MemoryStream(documents.Value!.Content)); Assert.Equal(3, zip.Entries.Count);
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open(); using var ms = new MemoryStream(); await stream.CopyToAsync(ms);
            var pdf = Encoding.Latin1.GetString(ms.ToArray()); Assert.StartsWith("%PDF-", pdf);
            Assert.Contains(c.ClaimNumber, pdf); Assert.Contains("Source: Claim", pdf);
            if (entry.Name.StartsWith("avoir"))
            {
                foreach(var removed in new[]{"Contract:","UMR:","Cedent / insured:","Date of loss:","Net loss:","Deductible:","Nature of loss:","48 100 499 160 re"})
                    Assert.DoesNotContain(removed,pdf);
                Assert.DoesNotContain("Storm damage",pdf);
            }
            else
            {
                Assert.Contains("Storm damage",pdf);
                Assert.Contains("Reinsured:",pdf); Assert.Contains("Date Of Loss:",pdf);
                Assert.DoesNotContain("Retrocedant:",pdf); Assert.DoesNotContain("Original Insured:",pdf);
                Assert.DoesNotContain("48 100 499 160 re",pdf);
                Assert.Contains("SEVEN HUNDRED AND FIFTY TUNISIAN DINARS",pdf);
                foreach (var account in TestIrisBankAccounts().Accounts) { Assert.Contains(account.Iban,pdf); Assert.Contains(account.SwiftBic,pdf); }
                Assert.Contains("ONE OF OUR BANK ACCOUNTS",pdf);
            }
            Assert.Contains(avoir.InvoiceNumber, pdf); Assert.DoesNotContain("CCN-", pdf);
            Assert.DoesNotContain("NOS COMPTES BANCAIRES", pdf); Assert.DoesNotContain("-10 000", pdf);
            Assert.Contains(entry.Name.StartsWith("avoir") ? "AVOIR" : "Collection Note", pdf);
            var folder = Environment.GetEnvironmentVariable("IRIS_CLAIM_DOCUMENT_PREVIEW");
            if (!string.IsNullOrEmpty(folder)) { Directory.CreateDirectory(folder); await File.WriteAllBytesAsync(Path.Combine(folder, entry.Name), ms.ToArray()); }
        }
        var note = await _db.CollectionNotes.FirstAsync();
        var individual = await back.GetClaimInvoicingDocumentAsync(c.ClaimId, "collection", note.IdCollectionNote, default); Assert.True(individual.Succeeded);
        Assert.Equal(404, (await back.GetClaimInvoicingDocumentAsync(c.ClaimId, "collection", -1, default)).StatusCode);
        var retry = await back.GetClaimInvoicingDocumentAsync(c.ClaimId, "bundle", null, default); Assert.True(retry.Succeeded);
        Assert.Single(await _db.Invoices.Where(i => i.ClaimRef == c.ClaimId).ToListAsync()); Assert.Equal(2, await _db.RemittanceRefs.CountAsync());
        Assert.All(await _db.Notifications.Where(n => n.EntityId == c.ClaimId).ToListAsync(), n => Assert.True(n.IsRead));
        var details = await back.GetClaimAsync(c.ClaimId, default); Assert.False(details.Value!.CanEdit); Assert.False(details.Value.CanPassToInvoicing); Assert.Equal("Invoiced", details.Value.InvoicingStatus);
        var history = await back.ListInvoicesAsync(new() { ContractId = c.ContractId }, default); Assert.Contains(history.Value!.Items, i => i.ClaimId == c.ClaimId && i.ClaimNumber == c.ClaimNumber && i.SourceType == "Claim");
        var contractHistory = await back.ListContractInvoicesAsync(c.ContractId, default);
        Assert.True(contractHistory.Succeeded, contractHistory.Error?.Message);
        var claimHistory = Assert.Single(contractHistory.Value!, i => i.ClaimId == c.ClaimId);
        Assert.Equal(c.ClaimNumber, claimHistory.ClaimNumber);Assert.Equal(0m, claimHistory.TotalAvoirAmount);
        var oldPremium = await _db.Invoices.Where(i => i.ClaimRef == null).ToListAsync();
        Assert.Equal(InvoiceDocumentAccounting.NetInvoiced(oldPremium), InvoiceDocumentAccounting.NetInvoiced(await _db.Invoices.ToListAsync()));
        var edit = await Service(_db, "Front Officer").SaveClaimAsync(c.ContractId, c.ClaimId, new() { }, default); Assert.Equal(409, edit.StatusCode);
    }

    [Fact]
    public async Task ClaimInvoicingRejectsInvalidDecisionsAndStaleVersions()
    {
        var c = await ReadyClaimInvoicing(); var req = await ClaimDecisions(c); var back = Service(_db);
        Assert.Equal(403, (await Service(_db, "Front Officer").ApproveClaimInvoicingAsync(c.ClaimId, req, default)).StatusCode);
        foreach (var label in new[] { " ", new string('x', 256) }) { req.Label = label; Assert.Equal(400, (await back.ApproveClaimInvoicingAsync(c.ClaimId, req, default)).StatusCode); }
        req.Label = "Claim settlement"; req.Version--; Assert.Equal(409, (await back.ApproveClaimInvoicingAsync(c.ClaimId, req, default)).StatusCode); req.Version++;
        var originalAmounts = req.Allocations.Select(a => a.Amount).ToArray();
        foreach (var allocation in req.Allocations) allocation.Amount = 7500m;
        Assert.Equal(400, (await back.ApproveClaimInvoicingAsync(c.ClaimId, req, default)).StatusCode);
        for (var i=0; i<req.Allocations.Count; i++) req.Allocations[i].Amount=originalAmounts[i];
        var amount = req.Allocations[0].Amount;
        foreach (var invalid in new[] { -1m, 0.0001m, amount + 1 }) { req.Allocations[0].Amount = invalid; Assert.Equal(400, (await back.ApproveClaimInvoicingAsync(c.ClaimId, req, default)).StatusCode); }
        req.Allocations[0].Amount = amount; var id = req.Allocations[0].PanelId;
        req.Allocations[0].PanelId = req.Allocations[1].PanelId; Assert.Equal(400, (await back.ApproveClaimInvoicingAsync(c.ClaimId, req, default)).StatusCode);
        req.Allocations[0].PanelId = -1; Assert.Equal(400, (await back.ApproveClaimInvoicingAsync(c.ClaimId, req, default)).StatusCode); req.Allocations[0].PanelId = id;
        _panels[0].Status = "Draft"; await _db.SaveChangesAsync(); Assert.Equal(400, (await back.ApproveClaimInvoicingAsync(c.ClaimId, req, default)).StatusCode);
        Assert.Empty(await _db.Invoices.Where(i => i.ClaimRef != null).ToListAsync()); Assert.Empty(await _db.CollectionNotes.ToListAsync()); Assert.Empty(await _db.RemittanceRefs.ToListAsync());
    }

    [Fact]
    public async Task ClaimInvoicingConcurrentApprovalsAndRollbackAreSafe()
    {
        var c = await ReadyClaimInvoicing(); var req = await ClaimDecisions(c);
        await using (var failing = Context(new RemittanceInsertFailure()))
            await Assert.ThrowsAsync<DbUpdateException>(() => Service(failing).ApproveClaimInvoicingAsync(c.ClaimId, req, default));
        Assert.False(await _db.Invoices.AnyAsync(i => i.ClaimRef == c.ClaimId)); Assert.Empty(await _db.CollectionNotes.ToListAsync());
        Assert.Null((await _db.Claims.AsNoTracking().SingleAsync(x => x.ClaimRef == c.ClaimId)).InvoicedAt);
        Assert.False((await _db.Notifications.SingleAsync(n => n.EntityId == c.ClaimId)).IsRead);
        await using var first = Context(); await using var second = Context();
        var results = await Task.WhenAll(Service(first).ApproveClaimInvoicingAsync(c.ClaimId, req, default), Service(second).ApproveClaimInvoicingAsync(c.ClaimId, req, default));
        Assert.All(results, r => Assert.True(r.Succeeded, r.Error?.Message)); Assert.Equal(results[0].Value!.AvoirId, results[1].Value!.AvoirId);
        Assert.Single(await _db.Invoices.Where(i => i.ClaimRef == c.ClaimId).ToListAsync()); Assert.Equal(2, await _db.CollectionNotes.CountAsync()); Assert.Equal(2, await _db.RemittanceRefs.CountAsync());
    }
    [Fact]
    public async Task ClaimInvoicingBlocksMissingPaidLossAndForeignContractPanels()
    {
        var c = await ReadyClaimInvoicing(); var req = await ClaimDecisions(c); var back = Service(_db);
        var other = new Contract { Name = "Other contract", TypeOfBusiness = "Facultative", Umr = "OTHER", Status = "Bound", IdCurrency = _receipt.IdCurrency, IdCounterparty = _receipt.IdCounterparty, CreatedAt = DateTime.UtcNow, CreatedBy = "back-user", IdUser = "back-user" };
        _db.Contracts.Add(other); await _db.SaveChangesAsync();
        var panel = new ContractPanel { IdContract = other.IdContract, IdCounterparty = _panels[0].IdCounterparty, Role = "Reinsurer", Status = "Confirmed", SignedSharePct = 10m };
        _db.ContractPanels.Add(panel); await _db.SaveChangesAsync(); req.Allocations[0].PanelId = panel.IdPanel;
        Assert.Equal(400, (await back.ApproveClaimInvoicingAsync(c.ClaimId, req, default)).StatusCode);
        var entity = await _db.Claims.FindAsync(c.ClaimId); entity!.PaidLoss = null; await _db.SaveChangesAsync();
        var preview = await back.PreviewClaimInvoicingAsync(c.ClaimId, default); Assert.Equal(400, preview.StatusCode); Assert.Contains("positive paid loss", preview.Error!.Message);
        Assert.False((await back.GetClaimAsync(c.ClaimId, default)).Value!.CanPassToInvoicing);
        Assert.Equal(404, (await back.GetClaimAsync(-1, default)).StatusCode);
    }

    // A persisted pre-deployment collection stays on its original pairing path; it must not
    // produce a new Claim Avoir or be recalculated under the new paid_loss rule.
    private async Task SeedLegacyClaimCollection(int claimId)
    {
        var c = await _db.Claims.FindAsync(claimId); c!.CollectionAmount = 950m; c.CollectionIssuedAt = DateTime.UtcNow;
        c.CollectionIssuedBy = "back-user"; c.CollectionReference = "CCN-" + c.ClaimNumber; c.ProcessingMode = "PAIR_WITH_ENCAISSEMENT";
        foreach (var panel in _panels) _db.CollectionNotes.Add(new CollectionNote
        {
            IdClaim = claimId,
            ClaimRef = claimId,
            IdPanel = panel.IdPanel,
            IdCounterparty = panel.IdCounterparty,
            IdCurrency = c.IdCurrency,
            AmountClaimed = 475m,
            PartPct = 5m,
            ReinsurerNameSnapshot = panel.IdCounterpartyNavigation.Name,
            Status = "Issued",
            EmissionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "back-user"
        });
        await _db.SaveChangesAsync();
    }


    [Fact]
    public async Task ClaimInvoicingSharesTheNormalAvoirSequenceAndResolvesExistingRemittances()
    {
        var claim=await ReadyClaimInvoicing();var back=Service(_db, bankAccounts: TestIrisBankAccounts());var year=DateTime.UtcNow.Year;
        _db.Set<InvoiceNumberSequence>().Add(new(){InvoiceYear=year,DocumentType="AV",LastSequence=13});
        _db.Invoices.Add(new Invoice {IdContract=claim.ContractId,IdCurrency=claim.CurrencyId,InvoiceType="AVOIR",InvoiceNumber=$"AV-{year}-000013",
            GrossPremium=100m,NetPremium=100m,Status="Draft",InvoiceDate=DateOnly.FromDateTime(DateTime.UtcNow),Label="Previous ordinary Avoir",CreatedBy="back-user",IdUser="back-user",CreatedAt=DateTime.UtcNow});
        await _db.SaveChangesAsync();
        var approval=await back.ApproveClaimInvoicingAsync(claim.ClaimId,await ClaimDecisions(claim),default);
        Assert.True(approval.Succeeded,approval.Error?.Message);
        Assert.Equal($"AV-{year}-000014",approval.Value!.AvoirNumber);
        Assert.All(approval.Value.Documents,d => Assert.Equal(approval.Value.AvoirNumber,d.Reference));
        Assert.Equal(14,(await _db.Set<InvoiceNumberSequence>().AsNoTracking().SingleAsync(s => s.DocumentType=="AV")).LastSequence);
        _db.ChangeTracker.Clear();
        var savedClaim=await _db.Claims.SingleAsync(c => c.ClaimRef==claim.ClaimId);
        savedClaim.CollectionReference="CCN-"+claim.ClaimNumber;
        var savedRemittances=await _db.RemittanceRefs.Where(r => r.ClaimRef==claim.ClaimId).ToListAsync();
        foreach(var remittance in savedRemittances) remittance.SourceInvoiceId=null;
        var savedNotes=await _db.CollectionNotes.Where(n => n.IdClaim==claim.ClaimId).ToListAsync();
        foreach(var note in savedNotes) note.DocumentReference="CCN-original-"+note.IdCollectionNote;
        await _db.SaveChangesAsync();_db.ChangeTracker.Clear();
        var listed=await back.ListRemittancesAsync(new(){Search=approval.Value.AvoirNumber},default);
        Assert.True(listed.Succeeded,listed.Error?.Message);Assert.Equal(2,listed.Value!.Items.Count());
        Assert.All(listed.Value.Items,r => {
            Assert.Equal(approval.Value.AvoirNumber,r.InvoiceAvoirNumber);Assert.Equal(approval.Value.AvoirNumber,r.SourceDocumentNumber);
            Assert.Equal(approval.Value.AvoirNumber,r.SourceInvoiceNumber);Assert.Equal(approval.Value.Label,r.Label);
        });
        Assert.All(await _db.RemittanceRefs.Where(r => r.ClaimRef==claim.ClaimId).ToListAsync(),r => Assert.Null(r.SourceInvoiceId));
        Assert.StartsWith("CCN-",(await _db.Claims.AsNoTracking().SingleAsync(c => c.ClaimRef==claim.ClaimId)).CollectionReference);
        Assert.Equal(approval.Value.AvoirNumber,(await back.GetClaimAsync(claim.ClaimId,default)).Value!.CollectionReference);
        var pdf=await back.GetClaimInvoicingDocumentAsync(claim.ClaimId,"collection",savedNotes[0].IdCollectionNote,default);
        Assert.True(pdf.Succeeded);Assert.Contains(approval.Value.AvoirNumber,Encoding.Latin1.GetString(pdf.Value!.Content));
        Assert.DoesNotContain("CCN-",Encoding.Latin1.GetString(pdf.Value.Content));
        _invoice=await _db.Invoices.SingleAsync(i => i.IdInvoice==_invoice.IdInvoice);
        _panels=await _db.ContractPanels.Where(p => p.IdContract==claim.ContractId).OrderBy(p => p.IdPanel).ToArrayAsync();
        await ConfigureDirectAvoirContractAsync();
        var normal=await back.CreatePremiumInvoiceAsync(claim.ContractId,new(){InvoiceType="AVOIR",ProcessingMode="DIRECT_TO_REMITTANCES",GrossAmount=100m,InvoiceDate=DateOnly.FromDateTime(DateTime.UtcNow),IdempotencyKey=Guid.NewGuid().ToString("N")},default);
        Assert.True(normal.Succeeded,normal.Error?.Message);Assert.Equal($"AV-{year}-000015",normal.Value!.InvoiceNumber);
    }
}
