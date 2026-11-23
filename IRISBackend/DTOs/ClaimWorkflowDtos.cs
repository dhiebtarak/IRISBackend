namespace IRISBackend.DTOs;

public sealed class ClaimRequestDto
{
    public decimal? Deductible { get; set; }
    public DateOnly? DateOfLoss { get; set; }
    public DateOnly? NotificationDate { get; set; }
    public string? NatureOfLoss { get; set; }
    public int CurrencyId { get; set; }
    public decimal? Reserve { get; set; }
    public decimal? PaidLoss { get; set; }
    public decimal? ExpertFees { get; set; }
    public decimal? OtherFees { get; set; }
    public string Status { get; set; } = "Outstanding";
}

public sealed class ClaimIssueRequestDto
{
    public string ProcessingMode { get; set; } = string.Empty;
}

public sealed class ClaimPairRequestDto
{
    public int BankReceiptId { get; set; }
}

public sealed class ClaimContextDto
{
    public int ContractId { get; set; }
    public string ContractName { get; set; } = string.Empty;
    public string Umr { get; set; } = string.Empty;
    public int UnderwritingYear { get; set; }
    public decimal? Deductible { get; set; }
    public decimal? SignedSharePercentage { get; set; }
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public int CurrencyDecimalPlaces { get; set; }
    public bool CanCreate { get; set; }
}

public sealed class ClaimResponseDto
{
    public long Version { get; set; }
    public string InvoicingStatus { get; set; } = "NotInvoiced";
    public bool CanPassToInvoicing { get; set; }
    public string CedentName { get; set; } = "";
    public int? CedentId { get; set; }
    public string CurrencyName { get; set; } = "";
    public string CurrencySymbol { get; set; } = "";
    public int ClaimId { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public int ContractId { get; set; }
    public string ContractName { get; set; } = string.Empty;
    public string Umr { get; set; } = string.Empty;
    public DateOnly DateOfLoss { get; set; }
    public DateOnly? NotificationDate { get; set; }
    public string? NatureOfLoss { get; set; }
    public int? UnderwritingYear { get; set; }
    public decimal? Reserve { get; set; }
    public decimal? PaidLoss { get; set; }
    public decimal? ExpertFees { get; set; }
    public decimal? OtherFees { get; set; }
    public decimal Amount100Percent { get; set; }
    public decimal NetLoss { get; set; }
    public decimal? Deductible { get; set; }
    public decimal? SignedSharePercentage { get; set; }
    public decimal? CollectionAmount { get; set; }
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public int CurrencyDecimalPlaces { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsClosed { get; set; }
    public bool IncludeInSoa { get; set; }
    public bool CanEdit { get; set; }
    public bool CanIssue { get; set; }
    public bool CanPair { get; set; }
    public string? ProcessingMode { get; set; }
    public string? CollectionReference { get; set; }
    public DateTime? CollectionIssuedAt { get; set; }
    public bool IsPaired { get; set; }
}

public sealed class ClaimReceiptOptionDto
{
    public int BankReceiptId { get; set; }
    public string? Reference { get; set; }
    public DateOnly OperationDate { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

public sealed class ClaimListQueryDto
{
    public string? Search { get; set; }
    public string? Contract { get; set; }
    public string? Cedent { get; set; }
    public string? Status { get; set; }
    public string? InvoicingStatus { get; set; }
    public int? CurrencyId { get; set; }
    public DateOnly? LossFrom { get; set; }
    public DateOnly? LossTo { get; set; }
    public string Sort { get; set; } = "created";
    public bool Descending { get; set; } = true;
}
public sealed class ClaimAllocationDto
{
    public int PanelId { get; set; }
    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public decimal Amount { get; set; }
}
public sealed class ClaimInvoicingRequestDto
{
    public string Label { get; set; } = "";
    public long Version { get; set; }
    public List<ClaimAllocationDto> Allocations { get; set; } = [];
}
public sealed class ClaimInvoicingPanelDto
{
    public int PanelId { get; set; }
    public string ReinsurerName { get; set; } = "";
    public decimal SignedShare { get; set; }
    public decimal SuggestedAmount { get; set; }
    public string AmountText { get; set; } = "";
}
public sealed class ClaimInvoicingPreviewDto
{
    public ClaimResponseDto Claim { get; set; } = new();
    public string PaidLossText { get; set; } = "";
    public string AllocationTotalText { get; set; } = "";
    public decimal TotalSignedShare { get; set; }
    public IReadOnlyList<ClaimInvoicingPanelDto> Panels { get; set; } = [];
}
public sealed class ClaimInvoicingResultDto
{
    public int ClaimId { get; set; }
    public int AvoirId { get; set; }
    public string AvoirNumber { get; set; } = "";
    public string Label { get; set; } = "";
    public string InvoicingStatus { get; set; } = "Invoiced";
    public IReadOnlyList<ClaimDocumentDto> Documents { get; set; } = [];
}
public sealed class ClaimDocumentDto
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Kind { get; set; } = "";
    public decimal Amount { get; set; }
}
