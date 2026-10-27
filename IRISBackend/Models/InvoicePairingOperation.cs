namespace IRISBackend.Models;

public sealed class InvoicePairingOperation
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public decimal TotalAllocated { get; set; }
    public decimal SignedPairingTotal { get; set; }
    public decimal RemainingAmount { get; set; }
    public string PairingStatus { get; set; } = string.Empty;
    public ICollection<InvoicePairingOperationLine> Lines { get; set; } = new List<InvoicePairingOperationLine>();
    public ICollection<InvoiceCreditApplication> CreditApplications { get; set; } = new List<InvoiceCreditApplication>();
}

public sealed class InvoicePairingOperationLine
{
    public int Id { get; set; }
    public int OperationId { get; set; }
    public int ReceiptAllocationId { get; set; }
    public decimal RequestedAmount { get; set; }
    public InvoicePairingOperation Operation { get; set; } = null!;
    public ReceiptAllocation Allocation { get; set; } = null!;
}
