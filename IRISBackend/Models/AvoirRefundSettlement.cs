namespace IRISBackend.Models;

public sealed class AvoirRefundSettlement
{
    public int Id { get; set; }
    public int AvoirInvoiceId { get; set; }
    public int BankReceiptId { get; set; }
    public int RemittanceId { get; set; }
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public Invoice AvoirInvoice { get; set; } = null!;
    public BankReceipt BankReceipt { get; set; } = null!;
    public RemittanceRef Remittance { get; set; } = null!;
}

public partial class RemittanceRef
{
    public int? SourceInvoiceId { get; set; }
    public string? BeneficiaryType { get; set; }
    public Invoice? SourceInvoice { get; set; }
    public ICollection<AvoirRefundSettlement> AvoirRefundSettlements { get; set; } = new List<AvoirRefundSettlement>();
}
