namespace IRISBackend.DTOs;

public sealed class ClaimRequestDto
{
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
