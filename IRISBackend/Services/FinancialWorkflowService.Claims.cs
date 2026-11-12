using System.Data;
using System.Globalization;
using System.Text;
using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    private bool CanManageClaims() => _http.HttpContext?.User is { } user && !user.IsInRole(IdentityRoles.Admin)
        && user.HasClaim(ModuleAccessCatalog.ClaimType, ModuleAccessCatalog.Claims);
    private bool CanReadClaims() => CanManageClaims() || CanBack();
    private IQueryable<Claim> ClaimQuery() => _db.Claims.Include(c => c.IdContractNavigation).ThenInclude(c => c.ContractFac)
        .Include(c => c.IdCurrencyNavigation);
    private async Task ClaimLock(int id, CancellationToken ct) => await _db.Database.ExecuteSqlInterpolatedAsync(
        $"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={"IRIS:Claim:" + id},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51000,'Claim is busy; retry.',1;", ct);

    public async Task<ServiceResult<ClaimContextDto>> GetClaimContextAsync(int contractId, CancellationToken ct)
    {
        if (!CanReadClaims()) return Forbidden<ClaimContextDto>();
        var c = await _db.Contracts.AsNoTracking().Include(x => x.ContractFac).Include(x => x.IdCurrencyNavigation).SingleOrDefaultAsync(x => x.IdContract == contractId, ct);
        if (c is null) return NotFound<ClaimContextDto>("Contract was not found.");
        return ServiceResult<ClaimContextDto>.Success(new()
        {
            ContractId = contractId,
            ContractName = c.Name ?? "",
            Umr = c.Umr ?? "",
            CurrencyId = c.IdCurrency ?? 0,
            CurrencyCode = c.IdCurrencyNavigation?.CurrencyCode ?? "",
            CurrencyDecimalPlaces = PanelRemittanceCalculator.Precision(c.IdCurrencyNavigation?.DecimalPlaces),
            Deductible = c.ContractFac?.Deductible,
            SignedSharePercentage = c.SignedSharePctCedent,
            UnderwritingYear = c.InceptionDate?.Year ?? 0,
            CanCreate = CanManageClaims() && ContractConstants.NormalizeStatus(c.Status) == ContractConstants.BoundStatus
        });
    }

    public async Task<ServiceResult<PagedResponseDto<ClaimResponseDto>>> ListClaimsAsync(int? contractId, int page, int pageSize, bool soaOnly, CancellationToken ct)
    {
        if (!CanReadClaims()) return Forbidden<PagedResponseDto<ClaimResponseDto>>();
        if (page < 1 || pageSize is < 1 or > 100) return Failure<PagedResponseDto<ClaimResponseDto>>("Invalid claim paging.");
        var query = ClaimQuery().AsNoTracking();
        if (contractId.HasValue) query = query.Where(c => c.IdContract == contractId);
        if (!CanManageClaims()) query = query.Where(c => c.Status == "PaidLoss" || c.CollectionIssuedAt != null);
        if (soaOnly) query = query.Where(c => c.CollectionIssuedAt != null && c.CollectionAmount > 0 && (c.Status == "PaidLoss" || c.Status == "Closed"));
        var count = await query.CountAsync(ct);
        var claims = await query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.ClaimRef).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = claims.Select(c => c.ClaimRef).ToArray();
        var paired = (await _db.ReceiptAllocations.Where(a => a.IsActive && a.ClaimRef.HasValue && ids.Contains(a.ClaimRef.Value)).Select(a => a.ClaimRef!.Value).ToListAsync(ct)).ToHashSet();
        return ServiceResult<PagedResponseDto<ClaimResponseDto>>.Success(new()
        { Items = claims.Select(c => ClaimDto(c, paired.Contains(c.ClaimRef))).ToArray(), TotalCount = count, PageNumber = page, PageSize = pageSize, TotalPages = (int)Math.Ceiling(count / (double)pageSize) });
    }

    public async Task<ServiceResult<ClaimResponseDto>> SaveClaimAsync(int contractId, int? claimId, ClaimRequestDto request, CancellationToken ct)
    {
        if (!CanManageClaims()) return Forbidden<ClaimResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<ClaimResponseDto>();
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await ClaimLock(claimId ?? -contractId, ct);
        var contract = await _db.Contracts.Include(c => c.ContractFac).Include(c => c.IdCurrencyNavigation).SingleOrDefaultAsync(c => c.IdContract == contractId, ct);
        if (contract is null) return NotFound<ClaimResponseDto>("Contract was not found.");
        if (ContractConstants.NormalizeStatus(contract.Status) != ContractConstants.BoundStatus) return Failure<ClaimResponseDto>("Claims can only be added or edited on Bound contracts.", status: 409);
        Claim? claim = claimId.HasValue ? await ClaimQuery().SingleOrDefaultAsync(c => c.ClaimRef == claimId && c.IdContract == contractId, ct) : null;
        if (claimId.HasValue && claim is null) return NotFound<ClaimResponseDto>("Claim was not found on this contract.");
        if (claim?.CollectionIssuedAt != null || claim?.Status is "Closed" or "UnderDeductible") return Failure<ClaimResponseDto>("This claim is locked. Issued financial details cannot be edited.", status: 409);
        var errors = new List<string>();
        var status = request.Status;
        if (status is not ("Outstanding" or "PaidLoss" or "Closed" or "UnderDeductible")) errors.Add("Status: Select a supported claim status.");
        if (request.DateOfLoss is null) errors.Add("DateOfLoss: Date of loss is required.");
        if (request.DateOfLoss > DateOnly.FromDateTime(DateTime.UtcNow)) errors.Add("DateOfLoss: Date of loss cannot be in the future.");
        if (request.DateOfLoss < contract.InceptionDate || request.DateOfLoss > contract.ExpiryDate) errors.Add("DateOfLoss: Date of loss must fall within the contract period.");
        if (request.NotificationDate.HasValue && request.NotificationDate < request.DateOfLoss) errors.Add("NotificationDate: Notification cannot precede the loss.");
        if (contract.IdCurrency != request.CurrencyId || contract.IdCurrencyNavigation is null) errors.Add("CurrencyId: Select the contract currency.");
        if ((request.NatureOfLoss?.Length ?? 0) > 10000) errors.Add("NatureOfLoss: Use 10,000 characters or fewer.");
        var precision = PanelRemittanceCalculator.Precision(contract.IdCurrencyNavigation?.DecimalPlaces);
        foreach (var (name, value) in new[] { ("Reserve", request.Reserve), ("PaidLoss", request.PaidLoss), ("ExpertFees", request.ExpertFees), ("OtherFees", request.OtherFees) })
            if (value < 0 || value > 999999999999999m || (value.HasValue && RoundMoney(value.Value, precision) != value.Value)) errors.Add($"{name}: Enter a nonnegative amount using the currency precision.");
        if (contract.InceptionDate is null) errors.Add("UnderwritingYear: The contract inception date is missing.");
        if (status is "PaidLoss" or "UnderDeductible")
        {
            if (string.IsNullOrWhiteSpace(request.NatureOfLoss)) errors.Add("NatureOfLoss: Describe the loss before marking it as paid loss.");
            if (!request.NotificationDate.HasValue) errors.Add("NotificationDate: Notification date is required before processing.");
            if (!request.PaidLoss.HasValue) errors.Add("PaidLoss: Enter the paid loss amount (zero is allowed).");
            if (contract.ContractFac?.Deductible is null or < 0) errors.Add("Deductible: The contract deductible must be specified before processing.");
            if (contract.SignedSharePctCedent is null or <= 0 or > 100) errors.Add("SignedShare: The contract signed share must be valid before processing.");
        }
        if (errors.Count > 0) return Failure<ClaimResponseDto>("Claim validation failed.", errors);
        var gross = RoundMoney((request.PaidLoss ?? 0) + (request.ExpertFees ?? 0) + (request.OtherFees ?? 0), precision);
        if (gross > 999999999999999m) return Failure<ClaimResponseDto>("Claim amount is too large.");
        var deductible = contract.ContractFac?.Deductible;
        if (status is "PaidLoss" or "UnderDeductible")
        {
            if (gross <= deductible) status = "UnderDeductible";
            else if (status == "UnderDeductible") return Failure<ClaimResponseDto>("An above-deductible loss cannot have Under-deductible status.");
        }
        var wasPaid = claim?.Status == "PaidLoss";
        var now = DateTime.UtcNow;
        if (claim is null)
        {
            claim = new Claim { IdContract = contractId, IdContractNavigation = contract, IdCurrencyNavigation = contract.IdCurrencyNavigation!, IdUser = user, CreatedBy = user, CreatedAt = now, ClaimNumber = "PENDING-" + Guid.NewGuid().ToString("N") };
            _db.Claims.Add(claim);
        }
        claim.DateOfLoss = request.DateOfLoss!.Value; claim.NotificationDate = request.NotificationDate; claim.Description = request.NatureOfLoss?.Trim();
        claim.EstimatedLoss = request.Reserve; claim.PaidLoss = request.PaidLoss; claim.ExpertFees = request.ExpertFees; claim.OtherFees = request.OtherFees;
        claim.GrossLossAmount = gross; claim.Deductible = deductible; claim.SignedShareSnapshot = contract.SignedSharePctCedent;
        claim.CollectionAmount = deductible.HasValue && claim.SignedShareSnapshot.HasValue ? RoundMoney(Math.Max(0, gross - deductible.Value) * claim.SignedShareSnapshot.Value / 100m, precision) : null;
        claim.UnderwritingYear = contract.InceptionDate!.Value.Year; claim.IdCurrency = request.CurrencyId; claim.Status = status;
        claim.ClosingDate = status is "Closed" or "UnderDeductible" ? DateOnly.FromDateTime(now) : null;
        claim.UpdatedAt = now; claim.UpdatedBy = user;
        await _db.SaveChangesAsync(ct);
        if (claim.ClaimNumber.StartsWith("PENDING-", StringComparison.Ordinal)) claim.ClaimNumber = $"CL-{claim.UnderwritingYear}-{claim.ClaimRef:000000}";
        if (!wasPaid && status == "PaidLoss")
            await NotifyRoleAsync("Back Officer", "Claim ready for processing", $"Claim {claim.ClaimNumber} is ready for its collection note.", "CLAIM", claim.ClaimRef, ct, "ClaimReady", $"/workspace/reassurance/back-officer/claims?claimId={claim.ClaimRef}");
        await _db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return ServiceResult<ClaimResponseDto>.Success(ClaimDto(claim, false), claimId.HasValue ? 200 : 201);
    }

    public async Task<ServiceResult<ClaimResponseDto>> IssueClaimCollectionAsync(int claimId, ClaimIssueRequestDto request, CancellationToken ct)
    {
        if (!CanBack()) return Forbidden<ClaimResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<ClaimResponseDto>();
        if (request.ProcessingMode is not (InvoiceDocumentAccounting.DirectToRemittances or InvoiceDocumentAccounting.PairWithEncaissement)) return Failure<ClaimResponseDto>("Select a processing mode.");
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); await ClaimLock(claimId, ct);
        var claim = await ClaimQuery().SingleOrDefaultAsync(c => c.ClaimRef == claimId, ct);
        if (claim is null) return NotFound<ClaimResponseDto>("Claim was not found.");
        if (claim.CollectionIssuedAt.HasValue)
        {
            if (claim.ProcessingMode != request.ProcessingMode) return Failure<ClaimResponseDto>("The collection note was already issued with a different processing mode.", status: 409);
            await tx.CommitAsync(ct); return ServiceResult<ClaimResponseDto>.Success(ClaimDto(claim, await ClaimIsPaired(claimId, ct)));
        }
        if (claim.Status != "PaidLoss" || claim.CollectionAmount is null or <= 0) return Failure<ClaimResponseDto>("Only an above-deductible Paid loss claim can be processed.", status: 409);
        if (ContractConstants.NormalizeStatus(claim.IdContractNavigation.Status) != ContractConstants.BoundStatus) return Failure<ClaimResponseDto>("The claim contract must be Bound.", status: 409);
        var panels = await _db.ContractPanels.Include(p => p.IdCounterpartyNavigation).ThenInclude(p => p.TypeAssignments)
            .Where(p => p.IdContract == claim.IdContract && p.Status == ContractConstants.ConfirmedPanelStatus && p.Role != null && p.Role.ToLower() == "reinsurer")
            .OrderBy(p => p.IdPanel).ToListAsync(ct);
        if (panels.Count == 0 || panels.Any(p => p.SignedSharePct is null or <= 0 || !p.IdCounterpartyNavigation.IsActive || !p.IdCounterpartyNavigation.TypeAssignments.Any(t => t.Type == CounterpartyTypes.Reinsurer))
            || panels.Sum(p => p.SignedSharePct ?? 0) != claim.SignedShareSnapshot)
            return Failure<ClaimResponseDto>("Confirmed active reinsurer shares must reconcile to the claim's contract signed share before issuance.", status: 409);
        var precision = PanelRemittanceCalculator.Precision(claim.IdCurrencyNavigation.DecimalPlaces);
        var now = DateTime.UtcNow;
        claim.CollectionReference = $"CCN-{claim.ClaimNumber}"; claim.ProcessingMode = request.ProcessingMode; claim.CollectionIssuedAt = now; claim.CollectionIssuedBy = user;
        claim.ContractNameSnapshot = claim.IdContractNavigation.Name; claim.UmrSnapshot = claim.IdContractNavigation.Umr; claim.CurrencyCodeSnapshot = claim.IdCurrencyNavigation.CurrencyCode;
        var unit = PanelRemittanceCalculator.Unit(precision);
        var totalUnits = claim.CollectionAmount.Value / unit;
        var exactUnits = panels.Select(p => totalUnits * p.SignedSharePct!.Value / claim.SignedShareSnapshot!.Value).ToArray();
        var allocatedUnits = exactUnits.Select(decimal.Floor).ToArray();
        var remainingUnits = (int)(totalUnits - allocatedUnits.Sum());
        foreach (var index in Enumerable.Range(0, panels.Count).OrderByDescending(i => exactUnits[i] - allocatedUnits[i]).ThenBy(i => panels[i].IdPanel).Take(remainingUnits))
            allocatedUnits[index]++;
        var notes = new List<CollectionNote>();
        for (var index = 0; index < panels.Count; index++)
        {
            var panel = panels[index];
            var amount = allocatedUnits[index] * unit;
            notes.Add(new CollectionNote
            {
                IdClaim = claimId,
                ClaimRef = claimId,
                IdPanel = panel.IdPanel,
                IdCounterparty = panel.IdCounterparty,
                IdCurrency = claim.IdCurrency,
                AmountClaimed = amount,
                PartPct = panel.SignedSharePct,
                Status = "Issued",
                EmissionDate = DateOnly.FromDateTime(now),
                CreatedAt = now,
                CreatedBy = user,
                ReinsurerNameSnapshot = panel.IdCounterpartyNavigation.Name,
                Description = claim.CollectionReference
            });
        }
        _db.CollectionNotes.AddRange(notes);
        claim.CollectionPdf = BuildClaimCollectionPdf(claim, notes, precision);
        if (request.ProcessingMode == InvoiceDocumentAccounting.DirectToRemittances) CreateClaimRemittances(claim, notes, null, user);
        await _db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return ServiceResult<ClaimResponseDto>.Success(ClaimDto(claim, false));
    }

    private IQueryable<BankReceipt> ClaimReceiptQuery(Claim claim) => _db.BankReceipts.AsNoTracking().Where(r =>
        r.IdCounterparty == claim.IdContractNavigation.IdCounterparty && r.IdCurrency == claim.IdCurrency
        && !r.ReceiptAllocations.Any(a => a.IsActive && ((a.IdContract.HasValue && a.IdContract != claim.IdContract)
            || (a.ClaimRef.HasValue && a.ClaimRefNavigation.IdContract != claim.IdContract))));

    public async Task<ServiceResult<IReadOnlyList<ClaimReceiptOptionDto>>> GetClaimReceiptsAsync(int claimId, CancellationToken ct)
    {
        if (!CanBack()) return Forbidden<IReadOnlyList<ClaimReceiptOptionDto>>();
        var claim = await ClaimQuery().AsNoTracking().SingleOrDefaultAsync(c => c.ClaimRef == claimId, ct);
        if (claim is null) return NotFound<IReadOnlyList<ClaimReceiptOptionDto>>("Claim was not found.");
        if (claim.ProcessingMode != InvoiceDocumentAccounting.PairWithEncaissement || claim.Status != "PaidLoss") return Failure<IReadOnlyList<ClaimReceiptOptionDto>>("This claim is not awaiting Encaissement pairing.");
        var rows = await ClaimReceiptQuery(claim).OrderByDescending(r => r.OperationDate).ThenByDescending(r => r.IdBankReceipt).Take(100)
            .Select(r => new ClaimReceiptOptionDto { BankReceiptId = r.IdBankReceipt, Reference = r.BusinessReference ?? r.BankReference, OperationDate = r.OperationDate, Amount = r.AmountReceived, CurrencyCode = r.IdCurrencyNavigation.CurrencyCode }).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<ClaimReceiptOptionDto>>.Success(rows);
    }

    public async Task<ServiceResult<ClaimResponseDto>> PairClaimAsync(int claimId, ClaimPairRequestDto request, CancellationToken ct)
    {
        if (!CanBack()) return Forbidden<ClaimResponseDto>();
        var user = CurrentUser(); if (user is null) return Unauthorized<ClaimResponseDto>();
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); await ClaimLock(claimId, ct);
        var claim = await ClaimQuery().SingleOrDefaultAsync(c => c.ClaimRef == claimId, ct);
        if (claim is null) return NotFound<ClaimResponseDto>("Claim was not found.");
        if (claim.Status != "PaidLoss" || claim.CollectionIssuedAt is null || claim.ProcessingMode != InvoiceDocumentAccounting.PairWithEncaissement) return Failure<ClaimResponseDto>("The claim is not awaiting Encaissement pairing.", status: 409);
        var existing = await _db.ReceiptAllocations.SingleOrDefaultAsync(a => a.IsActive && a.ClaimRef == claimId, ct);
        if (existing is not null)
        {
            if (existing.IdBankReceipt != request.BankReceiptId) return Failure<ClaimResponseDto>("This claim was already paired with another Encaissement.", status: 409);
            await tx.CommitAsync(ct); return ServiceResult<ClaimResponseDto>.Success(ClaimDto(claim, true));
        }
        await _db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={"IRIS:ClaimReceipt:" + request.BankReceiptId},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51000,'Receipt is busy; retry.',1;", ct);
        if (!await ClaimReceiptQuery(claim).AnyAsync(r => r.IdBankReceipt == request.BankReceiptId, ct)) return Failure<ClaimResponseDto>("Select an Encaissement for the same cedent, contract and currency.");
        // A claim is a credit, like an Avoir. It offsets premium allocations; it is not new cash received.
        var allocation = new ReceiptAllocation
        {
            IdBankReceipt = request.BankReceiptId,
            ClaimRef = claimId,
            TargetType = "CLAIM",
            AllocatedAmount = -claim.CollectionAmount!.Value,
            IdCurrency = claim.IdCurrency,
            ExchangeRate = 1m,
            IdUser = user,
            PairedAt = DateTime.UtcNow,
            IsActive = true
        };
        _db.ReceiptAllocations.Add(allocation); await _db.SaveChangesAsync(ct);
        var notes = await _db.CollectionNotes.Where(n => n.IdClaim == claimId && n.IdPanel.HasValue).ToListAsync(ct);
        CreateClaimRemittances(claim, notes, allocation.IdReceiptAllocation, user);
        await _db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return ServiceResult<ClaimResponseDto>.Success(ClaimDto(claim, true));
    }

    private void CreateClaimRemittances(Claim claim, IReadOnlyList<CollectionNote> notes, int? allocationId, string user)
    {
        foreach (var note in notes.Where(n => n.AmountClaimed > 0))
            _db.RemittanceRefs.Add(new RemittanceRef
            {
                ClaimRef = claim.ClaimRef,
                IdPanel = note.IdPanel,
                IdCounterparty = note.IdCounterparty,
                IdCurrency = claim.IdCurrency,
                IdReceiptAllocation = allocationId,
                SourceType = "Claim",
                BeneficiaryType = "REINSURER",
                PartPct = note.PartPct,
                PremiumSignedLine = note.AmountClaimed,
                NetPremiumDue = note.AmountClaimed,
                NetPayableAmount = note.AmountClaimed,
                Deductions = 0m,
                Description = claim.CollectionReference,
                Status = "Draft",
                SignatureStatus = "Draft",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = user,
                IsActive = true
            });
    }

    public async Task<ServiceResult<PrintablePaymentDocumentDto>> GetClaimCollectionDocumentAsync(int claimId, CancellationToken ct)
    {
        if (!CanReadClaims()) return Forbidden<PrintablePaymentDocumentDto>();
        var claim = await _db.Claims.AsNoTracking().SingleOrDefaultAsync(c => c.ClaimRef == claimId, ct);
        if (claim is null) return NotFound<PrintablePaymentDocumentDto>("Claim was not found.");
        if (claim.CollectionPdf is null) return Failure<PrintablePaymentDocumentDto>("Issue the collection note before downloading it.", status: 409);
        return ServiceResult<PrintablePaymentDocumentDto>.Success(new() { Content = claim.CollectionPdf, ContentType = "application/pdf", FileName = $"{claim.CollectionReference}.pdf", DocumentType = "CLAIM_COLLECTION_NOTE", DocumentReference = claim.CollectionReference });
    }

    public async Task<ServiceResult<ClaimResponseDto>> CloseClaimAsync(int claimId, CancellationToken ct)
    {
        if (!CanReadClaims()) return Forbidden<ClaimResponseDto>();
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); await ClaimLock(claimId, ct);
        var claim = await ClaimQuery().SingleOrDefaultAsync(c => c.ClaimRef == claimId, ct);
        if (claim is null) return NotFound<ClaimResponseDto>("Claim was not found.");
        if (claim.CollectionIssuedAt.HasValue && (!await _db.RemittanceRefs.AnyAsync(r => r.ClaimRef == claimId && r.IsActive, ct)
            || await _db.RemittanceRefs.AnyAsync(r => r.ClaimRef == claimId && r.IsActive && r.Status != "Closed" && r.Status != "Paid", ct)))
            return Failure<ClaimResponseDto>("Complete pairing and settlement before closing an issued claim.", status: 409);
        if (claim.Status != "UnderDeductible") claim.Status = "Closed";
        claim.ClosingDate ??= DateOnly.FromDateTime(DateTime.UtcNow); claim.UpdatedAt = DateTime.UtcNow; claim.UpdatedBy = CurrentUser();
        await _db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return ServiceResult<ClaimResponseDto>.Success(ClaimDto(claim, await ClaimIsPaired(claimId, ct)));
    }

    private Task<bool> ClaimIsPaired(int claimId, CancellationToken ct) => _db.ReceiptAllocations.AnyAsync(a => a.IsActive && a.ClaimRef == claimId, ct);
    private ClaimResponseDto ClaimDto(Claim c, bool paired) => new()
    {
        ClaimId = c.ClaimRef,
        ClaimNumber = c.ClaimNumber,
        ContractId = c.IdContract,
        ContractName = c.ContractNameSnapshot ?? c.IdContractNavigation.Name ?? "",
        Umr = c.UmrSnapshot ?? c.IdContractNavigation.Umr ?? "",
        DateOfLoss = c.DateOfLoss,
        NotificationDate = c.NotificationDate,
        NatureOfLoss = c.Description,
        UnderwritingYear = c.UnderwritingYear,
        Reserve = c.EstimatedLoss,
        PaidLoss = c.PaidLoss,
        ExpertFees = c.ExpertFees,
        OtherFees = c.OtherFees,
        Amount100Percent = c.GrossLossAmount ?? 0,
        Deductible = c.Deductible,
        SignedSharePercentage = c.SignedShareSnapshot,
        CollectionAmount = c.CollectionAmount,
        CurrencyId = c.IdCurrency,
        CurrencyCode = c.CurrencyCodeSnapshot ?? c.IdCurrencyNavigation.CurrencyCode,
        CurrencyDecimalPlaces = PanelRemittanceCalculator.Precision(c.IdCurrencyNavigation.DecimalPlaces),
        Status = c.Status,
        IsClosed = c.Status is "Closed" or "UnderDeductible",
        IncludeInSoa = c.CollectionIssuedAt.HasValue && c.CollectionAmount > 0 && c.Status is "PaidLoss" or "Closed",
        CanEdit = CanManageClaims() && c.CollectionIssuedAt is null && c.Status is not ("Closed" or "UnderDeductible"),
        CanIssue = CanBack() && c.Status == "PaidLoss" && c.CollectionAmount > 0 && c.CollectionIssuedAt is null,
        CanPair = CanBack() && c.Status == "PaidLoss" && c.CollectionIssuedAt.HasValue && c.ProcessingMode == InvoiceDocumentAccounting.PairWithEncaissement && !paired,
        ProcessingMode = c.ProcessingMode,
        CollectionReference = c.CollectionReference,
        CollectionIssuedAt = c.CollectionIssuedAt,
        IsPaired = paired
    };

    private byte[] BuildClaimCollectionPdf(Claim claim, IReadOnlyList<CollectionNote> notes, int precision)
    {
        var pages = new List<byte[]>();
        var logoBytes = LoadIrisInvoiceLogo(); var logo = logoBytes is null ? null : DecodePngForPdf(logoBytes);
        var groups = notes.Chunk(12).ToArray();
        for (var index = 0; index < groups.Length; index++)
        {
            var drawing = new StringBuilder();
            if (logo is not null) drawing.Append("q 135 0 0 90 48 738 cm /Logo Do Q\n");
            AddText(drawing, _invoiceBranding.CompanyName, 335, 800, 11, bold: true);
            AddText(drawing, claim.CollectionReference!, 335, 782, 9);
            AddText(drawing, $"Issued: {claim.CollectionIssuedAt:yyyy-MM-dd}", 335, 765, 9);
            AddCenteredText(drawing, "CLAIM COLLECTION NOTE", 706, 16, bold: true);
            var fields = new (string, string)[]
            {
                ("Claim / UMR", $"{claim.ClaimNumber} / {claim.UmrSnapshot}"), ("Contract", claim.ContractNameSnapshot ?? ""),
                ("Loss date / UY", $"{claim.DateOfLoss:yyyy-MM-dd} / {claim.UnderwritingYear}"), ("Notification", $"{claim.NotificationDate:yyyy-MM-dd}"),
                ("Reserve (separate)", FormatAmount(claim.EstimatedLoss ?? 0, precision)), ("Paid loss", FormatAmount(claim.PaidLoss ?? 0, precision)),
                ("Expert / other fees", $"{FormatAmount(claim.ExpertFees ?? 0, precision)} / {FormatAmount(claim.OtherFees ?? 0, precision)}"),
                ("100% amount", FormatAmount(claim.GrossLossAmount ?? 0, precision)), ("Contract deductible", FormatAmount(claim.Deductible ?? 0, precision)),
                ("Contract signed share", $"{claim.SignedShareSnapshot:0.########}%"), ("Amount to collect", $"{claim.CurrencyCodeSnapshot} {FormatAmount(claim.CollectionAmount ?? 0, precision)}")
            };
            var y = 667;
            foreach (var (label, value) in fields) { AddText(drawing, label, 50, y, 9, bold: true); AddText(drawing, value.Length > 72 ? value[..69] + "..." : value, 215, y, 9); y -= 22; }
            AddRule(drawing, 48, 403, 499, 1m); AddText(drawing, "REINSURER", 50, 385, 9, bold: true); AddText(drawing, "SHARE", 345, 385, 9, bold: true); AddText(drawing, "COLLECTION", 423, 385, 9, bold: true);
            y = 365;
            foreach (var note in groups[index]) { var name = note.ReinsurerNameSnapshot ?? ""; AddText(drawing, name.Length > 46 ? name[..43] + "..." : name, 50, y, 9); AddText(drawing, $"{note.PartPct:0.########}%", 345, y, 9); AddText(drawing, FormatAmount(note.AmountClaimed, precision), 423, y, 9); y -= 20; }
            AddText(drawing, "Credit applied through the selected claims processing mode.", 50, 88, 8);
            AddText(drawing, $"{claim.CurrencyCodeSnapshot} | Page {index + 1} of {groups.Length}", 50, 65, 8);
            pages.Add(Encoding.Latin1.GetBytes(drawing.ToString()));
        }
        return AssembleClosingAdvicePdf(pages, logo);
    }
}
