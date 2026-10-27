namespace IRISBackend.Models;

public partial class Invoice
{
    public decimal? GrossPremium { get; set; }
    public int? SequenceNumber { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? ProcessingMode { get; set; }
    public virtual ICollection<InvoicePanelAllocation> PanelAllocations { get; set; } = new List<InvoicePanelAllocation>();
    public virtual ICollection<InvoiceCreditApplication> CreditApplicationsAsTarget { get; set; } = new List<InvoiceCreditApplication>();
    public virtual ICollection<InvoiceCreditApplication> CreditApplicationsAsAvoir { get; set; } = new List<InvoiceCreditApplication>();
}
