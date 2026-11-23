using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    private IQueryable<ContractPanel> EligibleClaimPanels(int contractId) => _db.ContractPanels
        .Include(p => p.IdCounterpartyNavigation)
        .Where(p => p.IdContract == contractId && p.Status == ContractConstants.ConfirmedPanelStatus
            && p.Role != null && p.Role.ToLower() == "reinsurer" && p.SignedSharePct > 0 && p.SignedSharePct <= 100
            && p.IdCounterpartyNavigation.IsActive
            && p.IdCounterpartyNavigation.TypeAssignments.Any(t => t.Type == CounterpartyTypes.Reinsurer));

    private static IQueryable<Claim> FilterClaims(IQueryable<Claim> q, ClaimListQueryDto f)
    {
        if (!string.IsNullOrWhiteSpace(f.Search)) { var v = f.Search.Trim(); q = q.Where(c => c.ClaimNumber.Contains(v) || (c.Description != null && c.Description.Contains(v))); }
        if (!string.IsNullOrWhiteSpace(f.Contract)) { var v = f.Contract.Trim(); q = q.Where(c => c.IdContractNavigation.Name.Contains(v) || c.IdContractNavigation.Umr.Contains(v)); }
        if (!string.IsNullOrWhiteSpace(f.Cedent)) { var v = f.Cedent.Trim(); q = q.Where(c => c.IdContractNavigation.IdCounterpartyNavigation.Name.Contains(v)); }
        if (!string.IsNullOrWhiteSpace(f.Status)) q = q.Where(c => c.Status == f.Status);
        if (f.CurrencyId.HasValue) q = q.Where(c => c.IdCurrency == f.CurrencyId);
        if (f.LossFrom.HasValue) q = q.Where(c => c.DateOfLoss >= f.LossFrom);
        if (f.LossTo.HasValue) q = q.Where(c => c.DateOfLoss <= f.LossTo);
        q = f.InvoicingStatus switch
        {
            "Invoiced" => q.Where(c => c.InvoicedAt != null),
            "LegacyIssued" => q.Where(c => c.InvoicedAt == null && c.CollectionIssuedAt != null),
            "Ready" => q.Where(c => c.InvoicedAt == null && c.CollectionIssuedAt == null && c.Status == "PaidLoss" && c.PaidLoss > 0),
            "NotInvoiced" => q.Where(c => c.InvoicedAt == null && c.CollectionIssuedAt == null && (c.Status != "PaidLoss" || c.PaidLoss == null || c.PaidLoss <= 0)),
            _ => q
        };
        return q;
    }
    private static IOrderedQueryable<Claim> OrderClaims(IQueryable<Claim> q, ClaimListQueryDto f) => (f.Sort, f.Descending) switch
    {
        ("claimNumber", false) => q.OrderBy(c => c.ClaimNumber),
        ("claimNumber", true) => q.OrderByDescending(c => c.ClaimNumber),
        ("dateOfLoss", false) => q.OrderBy(c => c.DateOfLoss),
        ("dateOfLoss", true) => q.OrderByDescending(c => c.DateOfLoss),
        ("paidLoss", false) => q.OrderBy(c => c.PaidLoss),
        ("paidLoss", true) => q.OrderByDescending(c => c.PaidLoss),
        ("created", false) => q.OrderBy(c => c.CreatedAt),
        _ => q.OrderByDescending(c => c.CreatedAt)
    };

    public async Task<ServiceResult<ClaimResponseDto>> GetClaimAsync(int claimId, CancellationToken ct)
    {
        if (!CanReadClaims()) return Forbidden<ClaimResponseDto>();
        var claim = await ClaimQuery().AsNoTracking().SingleOrDefaultAsync(c => c.ClaimRef == claimId, ct);
        return claim is null ? NotFound<ClaimResponseDto>("Claim was not found.")
            : ServiceResult<ClaimResponseDto>.Success(ClaimDto(claim, await ClaimIsPaired(claimId, ct)));
    }

    private async Task<string?> ClaimInvoicingError(Claim c, CancellationToken ct)
    {
        if (c.InvoicedAt.HasValue || c.CollectionIssuedAt.HasValue) return "Claim has already been financially processed.";
        if (c.Status != "PaidLoss") return "Only a Paid Claim can pass to invoicing.";
        if (c.PaidLoss is null or <= 0) return "PaidLoss: A positive paid loss / approved settlement amount is required; gross loss is not a substitute.";
        if (c.IdContractNavigation.IdCounterparty is null || c.IdContractNavigation.IdCounterpartyNavigation is null) return "The contract cedent is missing.";
        if (!c.IdCurrencyNavigation.IsActive) return "The claim currency is inactive.";
        var places = PanelRemittanceCalculator.Precision(c.IdCurrencyNavigation.DecimalPlaces);
        if (RoundMoney(c.PaidLoss.Value, places) != c.PaidLoss.Value) return "Paid loss does not match the currency precision.";
        if (ContractConstants.NormalizeStatus(c.IdContractNavigation.Status) != ContractConstants.BoundStatus) return "The claim contract must be Bound.";
        var shares = await EligibleClaimPanels(c.IdContract).Select(p => p.SignedSharePct!.Value).ToListAsync(ct);
        if (shares.Count == 0) return "No eligible confirmed reinsurer panels exist on this contract.";
        if (shares.Sum() > 100m) return "Confirmed reinsurer signed shares cannot exceed 100%.";
        var netLoss = CalculateClaimNetLoss(c.PaidLoss, c.ExpertFees, c.OtherFees, c.Deductible, places);
        if (netLoss <= 0m) return "Net loss must be positive before invoicing.";
        if (RoundMoney(netLoss * shares.Sum() / 100m, places) <= 0m) return "The reinsured net loss is below one currency minor unit.";
        return null;
    }

    // Signed shares are percentages of net loss, not weights normalized to 100%.
    // Round the aggregate covered amount once, then distribute residual minor units
    // by largest remainder in panel-ID order so every document reconciles exactly.
    internal static decimal[] CalculateClaimShareAmounts(decimal netLoss, IReadOnlyList<decimal> shares, int precision)
    {
        if (netLoss <= 0m || shares.Count == 0 || shares.Any(s => s <= 0m || s > 100m) || shares.Sum() > 100m)
            throw new ArgumentException("A positive net loss and confirmed signed shares totalling at most 100% are required.");
        var unit = PanelRemittanceCalculator.Unit(precision);
        var exactUnits = shares.Select(share => netLoss * share / 100m / unit).ToArray();
        var units = exactUnits.Select(decimal.Floor).ToArray();
        var totalUnits = RoundMoney(netLoss * shares.Sum() / 100m, precision) / unit;
        foreach (var index in Enumerable.Range(0, shares.Count).OrderByDescending(i => exactUnits[i] - units[i]).ThenBy(i => i).Take((int)(totalUnits - units.Sum())))
            units[index]++;
        return units.Select(amount => amount * unit).ToArray();
    }

    public async Task<ServiceResult<ClaimInvoicingPreviewDto>> PreviewClaimInvoicingAsync(int claimId, CancellationToken ct)
    {
        if (!CanBack()) return Forbidden<ClaimInvoicingPreviewDto>();
        var c = await ClaimQuery().AsNoTracking().SingleOrDefaultAsync(c => c.ClaimRef == claimId, ct);
        if (c is null) return NotFound<ClaimInvoicingPreviewDto>("Claim was not found.");
        var error = await ClaimInvoicingError(c, ct);
        if (error != null) return Failure<ClaimInvoicingPreviewDto>(error, status: c.InvoicedAt.HasValue || c.CollectionIssuedAt.HasValue ? 409 : 400);
        var panels = await EligibleClaimPanels(c.IdContract).OrderBy(p => p.IdPanel).ToListAsync(ct);
        var precision = PanelRemittanceCalculator.Precision(c.IdCurrencyNavigation.DecimalPlaces);
        var amounts = CalculateClaimShareAmounts(CalculateClaimNetLoss(c.PaidLoss, c.ExpertFees, c.OtherFees, c.Deductible, precision),
            panels.Select(p => p.SignedSharePct!.Value).ToArray(), precision);
        return ServiceResult<ClaimInvoicingPreviewDto>.Success(new()
        {
            Claim = ClaimDto(c, false),
            AllocationTotalText = amounts.Sum().ToString("F" + precision, CultureInfo.InvariantCulture),
            TotalSignedShare = panels.Sum(p => p.SignedSharePct!.Value),
            PaidLossText = c.PaidLoss!.Value.ToString("F" + PanelRemittanceCalculator.Precision(c.IdCurrencyNavigation.DecimalPlaces), CultureInfo.InvariantCulture),
            Panels = panels.Select((p, i) => new ClaimInvoicingPanelDto
            { PanelId = p.IdPanel, ReinsurerName = p.IdCounterpartyNavigation.Name, SignedShare = p.SignedSharePct!.Value, SuggestedAmount = amounts[i], AmountText = amounts[i].ToString("F" + PanelRemittanceCalculator.Precision(c.IdCurrencyNavigation.DecimalPlaces), CultureInfo.InvariantCulture) }).ToArray()
        });
    }

    public async Task<ServiceResult<ClaimInvoicingResultDto>> ApproveClaimInvoicingAsync(int claimId, ClaimInvoicingRequestDto request, CancellationToken ct)
    {
        if (!CanBack()) return Forbidden<ClaimInvoicingResultDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<ClaimInvoicingResultDto>();
        var label = request.Label?.Trim();
        if (string.IsNullOrWhiteSpace(label) || label.Length > 255) return Failure<ClaimInvoicingResultDto>("Label: Enter an Avoir label using 1 to 255 characters.");
        var decisions = request.Allocations ?? [];
        if (decisions.Count == 0 || decisions.Select(a => a.PanelId).Distinct().Count() != decisions.Count)
            return Failure<ClaimInvoicingResultDto>("Allocations: Provide each eligible panel exactly once.");
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await ClaimLock(claimId, ct);
            var c = await ClaimQuery().SingleOrDefaultAsync(c => c.ClaimRef == claimId, ct);
            if (c is null) return NotFound<ClaimInvoicingResultDto>("Claim was not found.");
            if (c.InvoicedAt.HasValue)
            {
                var oldInvoice = await _db.Invoices.SingleAsync(i => i.ClaimRef == claimId, ct);
                var oldNotes = await _db.CollectionNotes.Where(n => n.IdClaim == claimId).ToListAsync(ct);
                if (oldInvoice.Label != label || oldNotes.Count != decisions.Count
                    || oldNotes.Any(n => !decisions.Any(a => a.PanelId == n.IdPanel && a.Amount == n.AmountClaimed)))
                    return Failure<ClaimInvoicingResultDto>("Claim was already invoiced with different decisions.", status: 409);
                await tx.CommitAsync(ct);
                return ServiceResult<ClaimInvoicingResultDto>.Success(InvoicingResult(c, oldInvoice, oldNotes));
            }
            if (c.InvoicingVersion != request.Version) return Failure<ClaimInvoicingResultDto>("The claim changed. Refresh its overview before approving.", status: 409);
            var error = await ClaimInvoicingError(c, ct);
            if (error != null) return Failure<ClaimInvoicingResultDto>(error, status: c.CollectionIssuedAt.HasValue ? 409 : 400);
            var panels = await EligibleClaimPanels(c.IdContract).OrderBy(p => p.IdPanel).ToListAsync(ct);
            if (panels.Count != decisions.Count || decisions.Any(a => !panels.Any(p => p.IdPanel == a.PanelId)))
                return Failure<ClaimInvoicingResultDto>("Allocations: Only all eligible confirmed panels on this claim contract are accepted.");
            var precision = PanelRemittanceCalculator.Precision(c.IdCurrencyNavigation.DecimalPlaces);
            if (decisions.Any(a => a.Amount < 0 || a.Amount > 999999999999999m || RoundMoney(a.Amount, precision) != a.Amount))
                return Failure<ClaimInvoicingResultDto>("Allocations: Use nonnegative amounts matching the claim currency precision.");
            var coveredAmounts = CalculateClaimShareAmounts(CalculateClaimNetLoss(c.PaidLoss, c.ExpertFees, c.OtherFees, c.Deductible, precision),
                panels.Select(p => p.SignedSharePct!.Value).ToArray(), precision);
            var coveredTotal = coveredAmounts.Sum();
            if (decisions.Sum(a => a.Amount) != coveredTotal) return Failure<ClaimInvoicingResultDto>("Allocations: Total must equal net loss multiplied by the eligible reinsurers' signed shares.");
            var now = DateTime.UtcNow; var date = DateOnly.FromDateTime(now);
            var sequence = await AllocateInvoiceNumberSequenceAsync(date.Year, "AV", ct);
            if (sequence > 999999) return Failure<ClaimInvoicingResultDto>("Avoir numbering sequence is exhausted.", status: 409);
            var invoice = new Invoice
            {
                IdContract = c.IdContract,
                ClaimRef = claimId,
                IdCurrency = c.IdCurrency,
                IdCounterparty = c.IdContractNavigation.IdCounterparty,
                ProcessingMode = InvoiceDocumentAccounting.DirectToRemittances,
                InvoiceNumber = CedentInvoiceNumber.FormatAvoir(date.Year, sequence),
                InvoiceType = InvoiceTypeAvoir,
                InvoiceDate = date,
                EmissionDate = date,
                SequenceNumber = sequence,
                Label = label,
                GrossPremium = coveredTotal,
                NetPremium = coveredTotal,
                Status = "Draft",
                RecoveryStatus = InvoiceRecoveryCalculator.Unsettled,
                CreatedAt = now,
                CreatedBy = user,
                IdUser = user
            };
            _db.Invoices.Add(invoice);
            var notes = panels.Select(p => new CollectionNote
            {
                IdClaim = claimId,
                ClaimRef = claimId,
                IdPanel = p.IdPanel,
                IdCurrency = c.IdCurrency,
                IdCounterparty = p.IdCounterparty,
                AmountClaimed = decisions.Single(a => a.PanelId == p.IdPanel).Amount,
                PartPct = p.SignedSharePct,
                // The business document number is the linked Avoir number. Panel/claim
                // keys already distinguish notes; do not allocate a second claim sequence.
                DocumentReference = null,
                ReinsurerNameSnapshot = p.IdCounterpartyNavigation.Name,
                Description = label,
                Status = "Issued",
                EmissionDate = date,
                CreatedAt = now,
                CreatedBy = user
            }).ToList();
            _db.CollectionNotes.AddRange(notes);
            c.InvoicedAt = now; c.InvoicedBy = user; c.InvoicingVersion++; c.CollectionIssuedAt = now; c.CollectionIssuedBy = user;
            c.CollectionReference = invoice.InvoiceNumber; c.ProcessingMode = InvoiceDocumentAccounting.DirectToRemittances;
            c.CollectionAmount = coveredTotal; c.ContractNameSnapshot = c.IdContractNavigation.Name; c.UmrSnapshot = c.IdContractNavigation.Umr;
            c.CurrencyCodeSnapshot = c.IdCurrencyNavigation.CurrencyCode;
            c.InvoicingSnapshot = JsonSerializer.Serialize(ClaimDocumentSnapshot.Create(c, panels, precision) with { MainCover = await ClaimMainCover(c.IdContract, ct) });
            c.UpdatedAt = now; c.UpdatedBy = user;
            await _db.SaveChangesAsync(ct); // Note IDs are required for direct, unique remittance links.
            CreateClaimRemittances(c, notes, null, user, invoice);
            var notifications = await _db.Notifications.Where(n => n.EntityType == "CLAIM" && n.EntityId == claimId && n.NotificationType == "ClaimReady" && !n.IsRead).ToListAsync(ct);
            foreach (var n in notifications) { n.IsRead = true; n.ReadAt = now; }
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            // PDF work intentionally happens only on download, outside the financial transaction.
            return ServiceResult<ClaimInvoicingResultDto>.Success(InvoicingResult(c, invoice, notes), 201);
        }
        catch (DbUpdateConcurrencyException) { return Failure<ClaimInvoicingResultDto>("Claim changed during approval. Refresh and retry.", status: 409); }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
        { return Failure<ClaimInvoicingResultDto>("Claim approval conflicted with another request. Refresh to retrieve the existing documents.", status: 409); }
        catch (SqlException e) when (e.Number is 1205 or 51000)
        { return Failure<ClaimInvoicingResultDto>("Claim is being processed. Refresh and retry.", status: 409); }
    }

    private static ClaimInvoicingResultDto InvoicingResult(Claim c, Invoice i, IReadOnlyList<CollectionNote> notes) => new()
    {
        ClaimId = c.ClaimRef,
        AvoirId = i.IdInvoice,
        AvoirNumber = i.InvoiceNumber,
        Label = i.Label,
        Documents = new[] { new ClaimDocumentDto { Id = i.IdInvoice, Reference = i.InvoiceNumber, Kind = "avoir", Amount = i.GrossPremium ?? 0, Recipient = "Cedent" } }
            .Concat(notes.OrderBy(n => n.IdPanel).Select(n => new ClaimDocumentDto { Id = n.IdCollectionNote, Reference = i.InvoiceNumber, Kind = "collection", Amount = n.AmountClaimed, Recipient = n.ReinsurerNameSnapshot ?? "" })).ToArray()
    };
    public async Task<ServiceResult<ClaimInvoicingResultDto>> GetClaimInvoicingAsync(int claimId, CancellationToken ct)
    {
        if (!CanReadClaims()) return Forbidden<ClaimInvoicingResultDto>();
        var c = await _db.Claims.AsNoTracking().SingleOrDefaultAsync(c => c.ClaimRef == claimId, ct);
        if (c is null) return NotFound<ClaimInvoicingResultDto>("Claim was not found.");
        var invoice = await _db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.ClaimRef == claimId, ct);
        if (invoice is null) return Failure<ClaimInvoicingResultDto>("The claim has not completed invoicing.", status: 409);
        var notes = await _db.CollectionNotes.AsNoTracking().Where(n => n.IdClaim == claimId).ToListAsync(ct);
        return ServiceResult<ClaimInvoicingResultDto>.Success(InvoicingResult(c, invoice, notes));
    }

    private sealed record ClaimRecipient(int PanelId, string Name, string? Address, string? City, string? Country);
    private sealed record ClaimDocumentSnapshot(string ContractName, string Umr, string Cedent, string? Address, string? City, string? Country,
        DateOnly? Inception, DateOnly? Expiry, string ClaimNumber, DateOnly LossDate, string? Description, decimal GrossLoss, decimal? Deductible,
        string Currency, int Precision, IReadOnlyList<ClaimRecipient> Recipients)
    {
        public string? MainCover { get; init; }
        public static ClaimDocumentSnapshot Create(Claim c, IReadOnlyList<ContractPanel> panels, int precision)
        {
            var cedent = c.IdContractNavigation.IdCounterpartyNavigation;
            return new(c.IdContractNavigation.Name ?? "", c.IdContractNavigation.Umr ?? "", cedent.Name, cedent.AddressLine, cedent.City, cedent.Country,
                c.IdContractNavigation.InceptionDate, c.IdContractNavigation.ExpiryDate, c.ClaimNumber, c.DateOfLoss, c.Description, c.GrossLossAmount ?? 0, c.Deductible,
                c.IdCurrencyNavigation.CurrencyCode, precision, panels.Select(p => new ClaimRecipient(p.IdPanel, p.IdCounterpartyNavigation.Name, p.IdCounterpartyNavigation.AddressLine, p.IdCounterpartyNavigation.City, p.IdCounterpartyNavigation.Country)).ToArray());
        }
    }

    private Task<string?> ClaimMainCover(int contractId, CancellationToken ct) => _db.ContractCoverageSelections.AsNoTracking()
        .Where(s => s.IdContract == contractId && (s.IdCoverageItemNavigation.CoverageLevel == "MAIN" || s.IdCoverageItemNavigation.CoverageLevel == "MAIN_COVER"))
        .OrderByDescending(s => s.IsPrimary).ThenBy(s => s.IdCoverageItem)
        .Select(s => s.IdCoverageItemNavigation.CoverageName).FirstOrDefaultAsync(ct);

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetClaimInvoicingDocumentAsync(int claimId, string kind, int? noteId, CancellationToken ct)
    {
        if (!CanReadClaims()) return Forbidden<PrintablePaymentDocumentDto>();
        var claim = await _db.Claims.AsNoTracking().SingleOrDefaultAsync(c => c.ClaimRef == claimId, ct);
        if (claim is null) return NotFound<PrintablePaymentDocumentDto>("Claim was not found.");
        var invoice = await _db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.ClaimRef == claimId, ct);
        if (invoice is null || claim.InvoicingSnapshot is null) return Failure<PrintablePaymentDocumentDto>("Claim invoicing has not completed.", status: 409);
        var snapshot = JsonSerializer.Deserialize<ClaimDocumentSnapshot>(claim.InvoicingSnapshot)!;
        var notes = await _db.CollectionNotes.AsNoTracking().Where(n => n.IdClaim == claimId).OrderBy(n => n.IdPanel).ToListAsync(ct);
        var mainCover = snapshot.MainCover ?? await ClaimMainCover(claim.IdContract, ct);
        var accounts = _irisBankAccounts.Accounts.Where(a => a.IsActive).OrderBy(a => a.DisplayOrder ?? int.MaxValue)
            .Select(a => new IrisInvoiceBankAccount(a.AccountHolder, a.BankName, a.Currency, a.Rib, a.Iban, a.SwiftBic, a.Domiciliation)).ToArray();
        if ((kind == "bundle" || kind == "collection") && _irisBankAccounts.GetFinancialNoteConfigurationErrors().Count > 0)
            return Failure<PrintablePaymentDocumentDto>("The three IRIS collection bank accounts must be configured before downloading collection notes.",
                status: 409, code: "IRIS_BANK_ACCOUNT_NOT_CONFIGURED");
        byte[] Render(CollectionNote? note)
        {
            var recipient = note is null ? null : snapshot.Recipients.Single(r => r.PanelId == note.IdPanel);
            var model = new IrisInvoiceAvoirPdfModel(IrisAccountingDocumentType.Avoir,
                invoice.InvoiceNumber, note?.EmissionDate ?? invoice.InvoiceDate,
                recipient?.Name ?? snapshot.Cedent, recipient is null ? snapshot.Address : recipient.Address, recipient is null ? snapshot.City : recipient.City, recipient is null ? snapshot.Country : recipient.Country,
                null, snapshot.ContractName, snapshot.Umr, invoice.Label, note is null ? "Claim" : mainCover, snapshot.Inception, snapshot.Expiry,
                note?.AmountClaimed ?? invoice.GrossPremium!.Value, snapshot.Currency, snapshot.Precision, _invoiceBranding, LoadIrisInvoiceLogo(), note is null ? [] : accounts)
            { ClaimDetails = new ClaimAccountingDetails(snapshot.ClaimNumber, snapshot.LossDate, snapshot.Description, snapshot.GrossLoss, snapshot.Deductible, snapshot.Cedent, note?.PartPct, note != null) };
            return BuildIrisInvoiceAvoirPdf(model);
        }
        if (kind == "avoir") return ServiceResult<PrintablePaymentDocumentDto>.Success(new() { Content = Render(null), ContentType = "application/pdf", FileName = $"avoir-{invoice.InvoiceNumber}.pdf" });
        if (kind == "collection")
        {
            var note = notes.SingleOrDefault(n => n.IdCollectionNote == noteId);
            if (note is null) return NotFound<PrintablePaymentDocumentDto>("Collection note was not found on this claim.");
            return ServiceResult<PrintablePaymentDocumentDto>.Success(new() { Content = Render(note), ContentType = "application/pdf", FileName = $"collection-note-{invoice.InvoiceNumber}-panel-{note.IdPanel}.pdf" });
        }
        if (kind != "bundle") return Failure<PrintablePaymentDocumentDto>("Select bundle, avoir or collection.");
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Add(string name, byte[] bytes) { using var entry = zip.CreateEntry(name).Open(); entry.Write(bytes); }
            Add($"avoir-{invoice.InvoiceNumber}.pdf", Render(null));
            foreach (var note in notes) Add($"collection-note-{invoice.InvoiceNumber}-panel-{note.IdPanel}.pdf", Render(note));
        }
        return ServiceResult<PrintablePaymentDocumentDto>.Success(new() { Content = stream.ToArray(), ContentType = "application/zip", FileName = $"claim-{claim.ClaimNumber}-documents.zip" });
    }
}
