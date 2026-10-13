namespace IRISBackend.DTOs;

public sealed class ClosingDisbursementContextDto
{
    public int ClosingId { get; set; }
    public string ClosingReference { get; set; } = string.Empty;
    public DateTime ClosingDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ReinsurerId { get; set; }
    public string ReinsurerName { get; set; } = string.Empty;
    public string? ReinsurerAddress { get; set; }
    public string? ReinsurerCity { get; set; }
    public string? ReinsurerCountry { get; set; }
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal TotalClosingAmount { get; set; }
    public IReadOnlyList<RemittanceResponseDto> Lines { get; set; } = [];
    public IReadOnlyList<BankAccountResponseDto> BankAccounts { get; set; } = [];
    public BankAccountSnapshotDto? SelectedBankAccount { get; set; }
    public bool CanDownloadPdf { get; set; }
    public string? PdfDownloadUrl { get; set; }
    public int? DisbursementId { get; set; }
    public bool CanStartPayment { get; set; }
    public bool CanConfirmTransfer { get; set; }
    public string? ValidationMessage { get; set; }
}
