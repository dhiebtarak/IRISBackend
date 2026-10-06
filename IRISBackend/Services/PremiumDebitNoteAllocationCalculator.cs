using IRISBackend.DTOs;
using IRISBackend.Models;

namespace IRISBackend.Services;

public sealed record PremiumDebitNoteAllocationCalculation(
    IReadOnlyList<PremiumDebitNoteAllocationDto> Allocations,
    decimal TotalNetAmount);

public static class PremiumDebitNoteAllocationCalculator
{
    public static PremiumDebitNoteAllocationCalculation Calculate(
        Contract contract,
        IReadOnlyList<ContractPanel> panels,
        decimal grossAmount)
    {
        var totalSignedShare = panels.Sum(panel => panel.SignedSharePct ?? 0m);
        if (totalSignedShare <= 0m)
        {
            throw new ArgumentException("At least one panel must have a positive signed share.", nameof(panels));
        }

        var cedentCommissionPct = contract.CedentCommission ?? 0m;
        var allocations = new List<PremiumDebitNoteAllocationDto>(panels.Count);
        var allocatedGross = 0m;
        var allocatedNet = 0m;

        for (var index = 0; index < panels.Count; index++)
        {
            var panel = panels[index];
            var ratio = (panel.SignedSharePct ?? 0m) / totalSignedShare;
            var lineGross = index == panels.Count - 1
                ? grossAmount - allocatedGross
                : decimal.Round(grossAmount * ratio, 4, MidpointRounding.AwayFromZero);
            var brokeragePct = panel.BrokeragePct ?? 0m;
            var cedentCommissionAmount = decimal.Round(
                lineGross * cedentCommissionPct / 100m,
                4,
                MidpointRounding.AwayFromZero);
            var brokerageAmount = decimal.Round(
                lineGross * brokeragePct / 100m,
                4,
                MidpointRounding.AwayFromZero);
            var otherDeductions = panel.GrossPremium > 0m
                ? decimal.Round(
                    (panel.OtherDeductions ?? 0m) * lineGross / panel.GrossPremium.Value,
                    4,
                    MidpointRounding.AwayFromZero)
                : 0m;
            var netAmount = lineGross - cedentCommissionAmount - brokerageAmount - otherDeductions;

            allocations.Add(new PremiumDebitNoteAllocationDto
            {
                PanelId = panel.IdPanel,
                ReinsurerCode = panel.IdCounterpartyNavigation?.LegacyCompanyCode,
                ReinsurerName = panel.IdCounterpartyNavigation?.Name ?? string.Empty,
                SignedSharePct = panel.SignedSharePct,
                AllocationRatio = ratio,
                GrossPremium = lineGross,
                CedentCommissionPct = cedentCommissionPct,
                CedentCommissionAmount = cedentCommissionAmount,
                BrokeragePct = brokeragePct,
                BrokerageAmount = brokerageAmount,
                OtherDeductions = otherDeductions,
                NetAmount = netAmount,
                CurrencyCode = contract.IdCurrencyNavigation?.CurrencyCode ?? string.Empty
            });

            allocatedGross += lineGross;
            allocatedNet += netAmount;
        }

        return new PremiumDebitNoteAllocationCalculation(allocations, allocatedNet);
    }
}
