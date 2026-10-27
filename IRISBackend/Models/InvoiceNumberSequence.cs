namespace IRISBackend.Models;

public sealed class InvoiceNumberSequence
{
    public int InvoiceYear { get; set; }
    public string DocumentType { get; set; } = "INV";
    public int LastSequence { get; set; }
}
