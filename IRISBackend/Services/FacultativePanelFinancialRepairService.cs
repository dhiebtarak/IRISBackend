using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace IRISBackend.Services;

public sealed class FacultativePanelFinancialRepairService(
    IRISBackendContext db,
    PremiumCalculationService calculator,
    ILogger<FacultativePanelFinancialRepairService> logger)
{
    public async Task<ServiceResult<PremiumCalculationRepairResultDto>> RepairAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        var contracts = await db.Contracts
            .Include(contract => contract.IdCurrencyNavigation)
            .Include(contract => contract.ContractFac)
            .Include(contract => contract.ContractPanels)
            .Include(contract => contract.Invoices)
            .Where(contract => contract.TypeOfBusiness == ContractConstants.FacultativeType)
            .OrderBy(contract => contract.IdContract)
            .ToListAsync(cancellationToken);

        var changes = new List<PanelRepairChange>();
        var contractNetChanges = new List<(int ContractId, decimal? OldNet, decimal NewNet)>();
        var contractGrossChanges = new List<(int ContractId, decimal? OldGross, decimal NewGross)>();
        var historicalInvoiceReconciliations = new List<PremiumRepairInvoiceReconciliationDto>();
        var contractsUpdated = 0;
        var contractsSkipped = 0;
        foreach (var contract in contracts)
        {
            var participating = contract.ContractPanels.ToArray();
            if (contract.ContractFac?.SumInsured is not decimal sumInsured
                || contract.ContractFac.PremiumRate is not decimal premiumRate
                || premiumRate is < 0m or > 100m
                || (contract.CedentCommission ?? 0m) is < 0m or > 100m)
            {
                contractsSkipped++;
                logger.LogWarning(
                    "Skipped Facultative premium repair because required source values are missing or invalid. ContractId={ContractId} SumInsured={SumInsured} PremiumRate={PremiumRate} CedentCommission={CedentCommission}",
                    contract.IdContract, contract.ContractFac?.SumInsured,
                    contract.ContractFac?.PremiumRate, contract.CedentCommission);
                continue;
            }

            var cedentCommission = contract.CedentCommission ?? 0m;
            var precision = contract.IdCurrencyNavigation?.DecimalPlaces;
            var oldGross = contract.GrossPremium;
            var correctedGross = FacultativeFinancialCalculator.CalculateGrossPremium(sumInsured, premiumRate, precision);
            if (oldGross != correctedGross)
            {
                contract.GrossPremium = correctedGross;
                contractGrossChanges.Add((contract.IdContract, oldGross, correctedGross));
            }

            var oldContractNet = contract.NetPremiumCedent;
            var correctedNet = PremiumCalculationService.CalculateContractNetPremium(
                correctedGross, cedentCommission, precision);
            contract.NetPremiumCedent = correctedNet;
            if (oldContractNet != correctedNet)
                contractNetChanges.Add((contract.IdContract, oldContractNet, correctedNet));

            if (oldGross != correctedGross || oldContractNet != correctedNet)
                contractsUpdated++;

            var historicalInvoiced = InvoiceDocumentAccounting.NetInvoiced(contract.Invoices);
            var correctedInvoiceable = contract.SignedSharePctCedent is > 0m and <= 100m
                ? PremiumDebitNoteFinancials.RoundMoney(
                    correctedNet * contract.SignedSharePctCedent.Value / 100m,
                    PanelRemittanceCalculator.Precision(precision))
                : 0m;
            if (historicalInvoiced > correctedInvoiceable)
            {
                historicalInvoiceReconciliations.Add(new PremiumRepairInvoiceReconciliationDto
                {
                    ContractId = contract.IdContract,
                    HistoricalNetInvoicedAmount = historicalInvoiced,
                    CorrectedInvoiceableNetPremium = correctedInvoiceable
                });
            }

            var original = participating.ToDictionary(panel => panel.IdPanel, panel => (
                panel.GrossPremium,
                panel.CedentCommissionPct,
                panel.CedentCommissionAmount,
                panel.BrokerageIrisAmount,
                panel.NetPremium,
                panel.OtherDeductions,
                panel.LimitOfLiabilityAmount,
                panel.DeductibleAmount));

            if (participating.Length > 0)
            {
                var errors = calculator.RecalculatePanels(contract, precision);
                if (errors.Count > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ServiceResult<PremiumCalculationRepairResultDto>.Failure(
                        $"Premium repair stopped at contract {contract.IdContract} because its persisted financial inputs are invalid.",
                        errors,
                        StatusCodes.Status400BadRequest);
                }
            }

            foreach (var panel in participating)
            {
                var old = original[panel.IdPanel];
                if (old.GrossPremium != panel.GrossPremium ||
                    old.CedentCommissionPct != panel.CedentCommissionPct ||
                    old.CedentCommissionAmount != panel.CedentCommissionAmount ||
                    old.BrokerageIrisAmount != panel.BrokerageIrisAmount ||
                    old.NetPremium != panel.NetPremium ||
                    old.OtherDeductions != panel.OtherDeductions ||
                    old.LimitOfLiabilityAmount != panel.LimitOfLiabilityAmount ||
                    old.DeductibleAmount != panel.DeductibleAmount)
                {
                    changes.Add(new PanelRepairChange(contract.IdContract, panel.IdPanel,
                        old.GrossPremium, panel.GrossPremium,
                        old.CedentCommissionPct, panel.CedentCommissionPct,
                        old.CedentCommissionAmount, panel.CedentCommissionAmount,
                        old.BrokerageIrisAmount, panel.BrokerageIrisAmount,
                        old.OtherDeductions, panel.OtherDeductions,
                        old.NetPremium, panel.NetPremium,
                        old.LimitOfLiabilityAmount, panel.LimitOfLiabilityAmount,
                        old.DeductibleAmount, panel.DeductibleAmount));
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var change in changes)
        {
            logger.LogInformation(
                "Repaired Facultative panel financial values. ContractId={ContractId}, PanelId={PanelId}, GrossPremium={OldGross}->{NewGross}, CedentCommissionPct={OldCommissionPct}->{NewCommissionPct}, CedentCommissionAmount={OldCommission}->{NewCommission}, BrokerageAmount={OldBrokerage}->{NewBrokerage}, OtherDeductions={OldDeductions}->{NewDeductions}, NetPremium={OldNet}->{NewNet}, LimitOfLiabilityAmount={OldLimit}->{NewLimit}, DeductibleAmount={OldDeductible}->{NewDeductible}",
                change.ContractId, change.PanelId, change.OldGross, change.NewGross,
                change.OldCommissionPct, change.NewCommissionPct,
                change.OldCommissionAmount, change.NewCommissionAmount,
                change.OldBrokerage, change.NewBrokerage,
                change.OldDeductions, change.NewDeductions,
                change.OldNet, change.NewNet,
                change.OldLimit, change.NewLimit,
                change.OldDeductible, change.NewDeductible);
        }
        foreach (var change in contractNetChanges)
            logger.LogInformation(
                "Repaired Facultative contract net premium. ContractId={ContractId}, NetPremiumCedent={OldNet}->{NewNet}",
                change.ContractId, change.OldNet, change.NewNet);
        foreach (var change in contractGrossChanges)
            logger.LogInformation(
                "Repaired Facultative contract gross premium. ContractId={ContractId}, GrossPremium={OldGross}->{NewGross}",
                change.ContractId, change.OldGross, change.NewGross);
        foreach (var reconciliation in historicalInvoiceReconciliations)
            logger.LogWarning(
                "Historical premium invoices exceed corrected invoiceable net premium and require reconciliation. ContractId={ContractId}, HistoricalNetInvoicedAmount={HistoricalNetInvoicedAmount}, CorrectedInvoiceableNetPremium={CorrectedInvoiceableNetPremium}",
                reconciliation.ContractId, reconciliation.HistoricalNetInvoicedAmount,
                reconciliation.CorrectedInvoiceableNetPremium);
        return ServiceResult<PremiumCalculationRepairResultDto>.Success(new PremiumCalculationRepairResultDto
        {
            ContractsExamined = contracts.Count,
            ContractsUpdated = contractsUpdated,
            ContractsSkipped = contractsSkipped,
            PanelsExamined = contracts.Sum(contract => contract.ContractPanels.Count),
            PanelsUpdated = changes.Count,
            HistoricalInvoiceReconciliations = historicalInvoiceReconciliations
        });
    }

    private sealed record PanelRepairChange(
        int ContractId,
        int PanelId,
        decimal? OldGross,
        decimal? NewGross,
        decimal? OldCommissionPct,
        decimal? NewCommissionPct,
        decimal? OldCommissionAmount,
        decimal? NewCommissionAmount,
        decimal? OldBrokerage,
        decimal? NewBrokerage,
        decimal? OldDeductions,
        decimal? NewDeductions,
        decimal? OldNet,
        decimal? NewNet,
        decimal? OldLimit,
        decimal? NewLimit,
        decimal? OldDeductible,
        decimal? NewDeductible);
}
