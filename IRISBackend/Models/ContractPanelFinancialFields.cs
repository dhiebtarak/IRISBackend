namespace IRISBackend.Models;

// These properties extend the reverse-engineered panel entity without editing
// the generated file. They are mapped in IRISBackendContext.OnModelCreating.
public partial class ContractPanel
{
    public string? LimitOfLiabilityType { get; set; }

    public decimal? LimitOfLiabilityValue { get; set; }

    public decimal? LimitOfLiabilityAmount { get; set; }

    public string? DeductibleType { get; set; }

    public decimal? DeductibleValue { get; set; }

    public decimal? DeductibleAmount { get; set; }

    public decimal? CedentCommissionPct { get; set; }

    public decimal? CedentCommissionAmount { get; set; }

    public decimal? BrokerageIrisAmount { get; set; }
}
