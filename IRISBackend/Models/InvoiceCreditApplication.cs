namespace IRISBackend.Models;

public class InvoiceCreditApplication
{
    public int IdInvoiceCreditApplication { get; set; }
    public int IdBankReceipt { get; set; }
    public int TargetInvoiceId { get; set; }
    public int AvoirInvoiceId { get; set; }
    public int? OperationId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public virtual BankReceipt IdBankReceiptNavigation { get; set; } = null!;
    public virtual Invoice TargetInvoiceNavigation { get; set; } = null!;
    public virtual Invoice AvoirInvoiceNavigation { get; set; } = null!;
    public virtual InvoicePairingOperation? OperationNavigation { get; set; }
}
