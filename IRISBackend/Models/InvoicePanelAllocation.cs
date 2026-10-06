namespace IRISBackend.Models;

public partial class InvoicePanelAllocation
{
    public int IdInvoiceAllocation { get; set; }
    public int IdInvoice { get; set; }
    public int IdPanel { get; set; }
    public int IdCounterparty { get; set; }
    public string ReinsurerNameSnapshot { get; set; } = string.Empty;
    public decimal? SignedSharePctSnapshot { get; set; }
    public decimal AllocationRatio { get; set; }
    public decimal GrossPremium { get; set; }
    public decimal? CedentCommissionPct { get; set; }
    public decimal CedentCommissionAmount { get; set; }
    public decimal? BrokeragePct { get; set; }
    public decimal BrokerageAmount { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal NetAmount { get; set; }
    public string CurrencyCodeSnapshot { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public virtual Invoice IdInvoiceNavigation { get; set; } = null!;
    public virtual ContractPanel IdPanelNavigation { get; set; } = null!;
    public virtual Counterparty IdCounterpartyNavigation { get; set; } = null!;
}
