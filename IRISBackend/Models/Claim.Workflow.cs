namespace IRISBackend.Models;

public partial class Claim
{
    public virtual Invoice? InvoicingAvoir { get; set; }
    public long InvoicingVersion { get; set; }
    public DateTime? InvoicedAt { get; set; }
    public string? InvoicedBy { get; set; }
    public string? InvoicingSnapshot { get; set; }
    public decimal? OtherFees { get; set; }
    public decimal? SignedShareSnapshot { get; set; }
    public decimal? CollectionAmount { get; set; }
    public string? ProcessingMode { get; set; }
    public string? CollectionReference { get; set; }
    public DateTime? CollectionIssuedAt { get; set; }
    public string? CollectionIssuedBy { get; set; }
    public string? ContractNameSnapshot { get; set; }
    public string? UmrSnapshot { get; set; }
    public string? CurrencyCodeSnapshot { get; set; }
    public byte[]? CollectionPdf { get; set; }
}

public partial class CollectionNote
{
    public string? DocumentReference { get; set; }
    public int? IdPanel { get; set; }
    public int? IdCurrency { get; set; }
    public string? ReinsurerNameSnapshot { get; set; }
}

public partial class Invoice
{
    public int? ClaimRef { get; set; }
    public virtual Claim? SourceClaim { get; set; }
}
public partial class RemittanceRef
{
    public int? IdCollectionNote { get; set; }
}
