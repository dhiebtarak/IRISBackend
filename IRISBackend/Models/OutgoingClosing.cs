namespace IRISBackend.Models;

public sealed class OutgoingClosing
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int IdCounterparty { get; set; }
    public int IdCurrency { get; set; }
    public DateTime IssuedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string Status { get; set; } = "AwaitingFinanceDisbursement";
    public int? IdDisbursement { get; set; }
    public decimal TotalAmount { get; set; }
    public string TotalInWords { get; set; } = string.Empty;
    public byte[] PdfContent { get; set; } = [];
    public ICollection<OutgoingClosingLine> Lines { get; set; } = new List<OutgoingClosingLine>();
}

public sealed class OutgoingClosingLine
{
    public int Id { get; set; }
    public int ClosingId { get; set; }
    public int RemittanceId { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal PayableAmount { get; set; }
    public OutgoingClosing Closing { get; set; } = null!;
    public RemittanceRef Remittance { get; set; } = null!;
}

public partial class RemittanceRef
{
    public bool IsActive { get; set; } = true;
    public int? IdInvoiceAllocation { get; set; }
    public InvoicePanelAllocation? IdInvoiceAllocationNavigation { get; set; }
    public ICollection<OutgoingClosingLine> ClosingLines { get; set; } = new List<OutgoingClosingLine>();
}

public partial class ReceiptAllocation
{
    public bool IsActive { get; set; } = true;
}
