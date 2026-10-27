using IRISBackend.DTOs;
using IRISBackend.Models;

namespace IRISBackend.Services;

public sealed record PremiumDebitNoteAllocationCalculation(
    IReadOnlyList<PremiumDebitNoteAllocationDto> Allocations,
    decimal TotalNetAmount,
    decimal InvoiceGrossEquivalent,
    decimal TotalCedentCommissionAmount,
    decimal TotalAllocatedNetPremium,
    decimal TotalNetPayableAmount);

public static class PremiumDebitNoteAllocationCalculator
{
    public static decimal CalculateIrisBrokerage(decimal allocatedGrossPremium, decimal brokerageRate, int? currencyDecimalPlaces)
    {
        var precision = PanelRemittanceCalculator.Precision(currencyDecimalPlaces);
        return PremiumDebitNoteFinancials.RoundMoney(allocatedGrossPremium * brokerageRate / 100m, precision);
    }

    public static decimal CalculateNetPayable(decimal allocatedNetPremium, decimal irisBrokerageAmount, decimal otherDeductions) =>
        allocatedNetPremium - irisBrokerageAmount - otherDeductions;

    public static decimal NetPayableFromPersistedAllocation(InvoicePanelAllocation allocation) =>
        CalculateNetPayable(allocation.NetAmount, allocation.BrokerageAmount, allocation.OtherDeductions);

    public static PremiumDebitNoteAllocationCalculation Calculate(
        Contract contract,
        IReadOnlyList<ContractPanel> panels,
        decimal invoiceNetAmount,
        int? currencyDecimalPlaces = null)
    {
        var precision = PanelRemittanceCalculator.Precision(currencyDecimalPlaces ?? contract.IdCurrencyNavigation?.DecimalPlaces);
        if (invoiceNetAmount <= 0m ||
            decimal.Round(invoiceNetAmount, precision, MidpointRounding.AwayFromZero) != invoiceNetAmount)
            throw new ArgumentOutOfRangeException(nameof(invoiceNetAmount), "Invoice amount must be positive and match the currency precision.");

        if (!PremiumDebitNoteFinancials.TryCalculateInvoiceablePremium(contract, out var invoiceableValues, out var validationError))
            throw new ArgumentException(validationError ?? "Contract invoiceable premium is invalid.", nameof(contract));

        var totalSignedShare = panels.Sum(panel => panel.SignedSharePct ?? 0m);
        if (panels.Count == 0 || panels.Any(panel => panel.SignedSharePct is null or <= 0m)
            || totalSignedShare <= 0m)
            throw new ArgumentException("Confirmed reinsurer panels must each have a positive signed share.", nameof(panels));
        if (invoiceNetAmount > invoiceableValues!.InvoiceableNetPremium)
            throw new ArgumentOutOfRangeException(nameof(invoiceNetAmount), "Invoice amount exceeds the contract's remaining invoiceable net premium.");

        var invoiceFraction = invoiceNetAmount / invoiceableValues.InvoiceableNetPremium;
        var invoiceGrossEquivalent = PremiumDebitNoteFinancials.RoundMoney(
            invoiceableValues.ContractGrossPremium * invoiceFraction, precision);
        var totalCedentCommission = invoiceGrossEquivalent - invoiceNetAmount;
        if (totalCedentCommission < 0m)
            throw new ArgumentException("Rounded gross equivalent cannot be less than the net invoice amount.", nameof(invoiceNetAmount));

        var allocations = new List<PremiumDebitNoteAllocationDto>(panels.Count);
        var allocatedGross = 0m;
        var allocatedCommission = 0m;
        var allocatedNet = 0m;
        var totalNetPayable = 0m;
        var cedentCommissionPct = invoiceableValues.CedentCommissionPercentage;

        for (var index = 0; index < panels.Count; index++)
        {
            var panel = panels[index];
            var ratio = (panel.SignedSharePct ?? 0m) / totalSignedShare;
            var lineGross = index == panels.Count - 1
                ? invoiceGrossEquivalent - allocatedGross
                : PremiumDebitNoteFinancials.RoundMoney(invoiceGrossEquivalent * ratio, precision);
            var lineCedentCommission = index == panels.Count - 1
                ? totalCedentCommission - allocatedCommission
                : PremiumDebitNoteFinancials.RoundMoney(totalCedentCommission * ratio, precision);
            var lineNet = lineGross - lineCedentCommission;
            var brokeragePct = panel.BrokeragePct ?? 0m;
            var brokerageAmount = CalculateIrisBrokerage(lineGross, brokeragePct, precision);
            var otherDeductions = panel.OtherDeductionsPercentage.HasValue
                ? PremiumDebitNoteFinancials.RoundMoney(lineGross * panel.OtherDeductionsPercentage.Value / 100m, precision)
                : panel.GrossPremium > 0m
                    ? PremiumDebitNoteFinancials.RoundMoney(
                        (panel.OtherDeductions ?? 0m) * lineGross / panel.GrossPremium.Value, precision)
                    : 0m;
            var netPayable = CalculateNetPayable(lineNet, brokerageAmount, otherDeductions);
            if (lineGross < 0m || lineCedentCommission < 0m || lineNet < 0m || netPayable < 0m)
                throw new ArgumentException($"Calculated allocation for panel {panel.IdPanel} cannot be negative.", nameof(panels));

            allocations.Add(new PremiumDebitNoteAllocationDto
            {
                PanelId = panel.IdPanel,
                ReinsurerId = panel.IdCounterparty,
                ReinsurerCode = panel.IdCounterpartyNavigation?.LegacyCompanyCode,
                ReinsurerName = panel.IdCounterpartyNavigation?.Name ?? string.Empty,
                SignedSharePct = panel.SignedSharePct,
                SharePercentage = panel.SignedSharePct ?? 0m,
                SignedSharePercentage = panel.SignedSharePct ?? 0m,
                AllocationRatio = ratio,
                AllocationRatioPercentage = ratio * 100m,
                GrossPremium = lineGross,
                AllocatedGrossPremium = lineGross,
                AllocationAmount = lineNet,
                SignedAllocationAmount = lineNet,
                CedentCommissionPct = cedentCommissionPct,
                CedentCommissionRate = cedentCommissionPct,
                CedentCommissionPercentage = cedentCommissionPct,
                CedentCommissionAmount = lineCedentCommission,
                AllocatedNetPremium = lineNet,
                BrokeragePct = brokeragePct,
                IrisBrokeragePercentage = brokeragePct,
                BrokerageAmount = brokerageAmount,
                IrisBrokerageAmount = brokerageAmount,
                OtherDeductions = otherDeductions,
                OtherDeductionsAmount = otherDeductions,
                OtherDeductionsPercentage = panel.OtherDeductionsPercentage,
                NetAmount = lineNet,
                NetPayableAmount = netPayable,
                SignedNetPayableAmount = netPayable,
                CurrencyCode = contract.IdCurrencyNavigation?.CurrencyCode ?? string.Empty,
                Currency = contract.IdCurrencyNavigation?.CurrencyCode ?? string.Empty
            });

            allocatedGross += lineGross;
            allocatedCommission += lineCedentCommission;
            allocatedNet += lineNet;
            totalNetPayable += netPayable;
        }

        if (allocatedGross != invoiceGrossEquivalent
            || allocatedCommission != totalCedentCommission
            || allocatedNet != invoiceNetAmount)
            throw new InvalidOperationException("Invoice panel allocations failed to reconcile to the invoice totals.");

        return new PremiumDebitNoteAllocationCalculation(
            allocations,
            allocatedNet,
            invoiceGrossEquivalent,
            totalCedentCommission,
            allocatedNet,
            totalNetPayable);
    }
}
