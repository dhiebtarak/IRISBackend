namespace IRISBackend.DTOs;

public sealed class PremiumCalculationRepairResultDto
{
    public int ContractsExamined { get; init; }
    public int ContractsUpdated { get; init; }
    public int ContractsSkipped { get; init; }
    public int PanelsExamined { get; init; }
    public int PanelsUpdated { get; init; }
    public IReadOnlyList<PremiumRepairInvoiceReconciliationDto> HistoricalInvoiceReconciliations { get; init; } = [];
}

public sealed class PremiumRepairInvoiceReconciliationDto
{
    public int ContractId { get; init; }
    public decimal HistoricalNetInvoicedAmount { get; init; }
    public decimal CorrectedInvoiceableNetPremium { get; init; }
}
