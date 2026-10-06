namespace IRISBackend.Models;

public partial class Invoice
{
    public decimal? GrossPremium { get; set; }
    public int? SequenceNumber { get; set; }
    public string? IdempotencyKey { get; set; }
    public virtual ICollection<InvoicePanelAllocation> PanelAllocations { get; set; } = new List<InvoicePanelAllocation>();
}
