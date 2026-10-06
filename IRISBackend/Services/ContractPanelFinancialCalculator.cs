using IRISBackend.DTOs;
using IRISBackend.Models;

namespace IRISBackend.Services;

public sealed record ContractPanelFinancialCalculation(
    decimal? SumInsured,
    decimal? PremiumRatePct,
    string? LimitOfLiabilityType,
    decimal? LimitOfLiabilityValue,
    decimal? LimitOfLiabilityAmount,
    string? DeductibleType,
    decimal? DeductibleValue,
    decimal? DeductibleAmount,
    decimal? CedentCommissionPct,
    decimal? CedentCommissionAmount,
    decimal? BrokerageIrisPct,
    decimal? BrokerageIrisAmount,
    decimal OtherDeductions,
    decimal? GrossPremium,
    decimal? NetPremium);

public static class ContractPanelFinancialCalculator
{
    public const string AmountType = "Amount";
    public const string PercentageType = "Percentage";

    public static ContractPanelFinancialCalculation Calculate(ContractPanelRequestDto request)
    {
        var sumInsured = request.SumInsured ?? request.Tsi;
        var brokeragePct = request.BrokerageIrisPct ?? request.BrokeragePct;
        var premiumRatePct = request.PremiumRatePct;
        var participationRatio = request.WrittenSharePct is > 0m && request.SignedSharePct is >= 0m
            ? request.SignedSharePct.Value / request.WrittenSharePct.Value
            : (decimal?)null;
        decimal? contractGrossPremium = sumInsured.HasValue && premiumRatePct.HasValue
            ? RoundMoney(sumInsured.Value * premiumRatePct.Value / 100m)
            : null;
        decimal? grossPremium = contractGrossPremium.HasValue && participationRatio.HasValue
            ? RoundMoney(contractGrossPremium.Value * participationRatio.Value)
            : null;

        var limitType = NormalizeType(request.LimitOfLiabilityType, request.LimitOfLiabilityValue.HasValue);
        var limitValue = request.LimitOfLiabilityValue;
        decimal? limitAmount = null;
        if (limitValue.HasValue && limitType is not null)
        {
            limitAmount = limitType.Equals(PercentageType, StringComparison.OrdinalIgnoreCase)
                ? sumInsured.HasValue ? RoundMoney(sumInsured.Value * limitValue.Value / 100m) : null
                : RoundMoney(limitValue.Value);
        }

        var deductibleType = NormalizeType(
            request.DeductibleType,
            request.DeductibleValue.HasValue || request.Deductible.HasValue);
        var deductibleValue = request.DeductibleValue ??
            (request.DeductibleType is null ? request.Deductible : null);
        decimal? deductibleAmount = null;
        if (deductibleValue.HasValue && deductibleType is not null)
        {
            deductibleAmount = deductibleType.Equals(PercentageType, StringComparison.OrdinalIgnoreCase)
                ? grossPremium.HasValue ? RoundMoney(grossPremium.Value * deductibleValue.Value / 100m) : null
                : RoundMoney(deductibleValue.Value);
        }

        var otherDeductions = request.OtherDeductions ?? 0m;
        var cedentCommissionPct = request.CedentCommissionPct ?? 0m;
        decimal? cedentCommissionAmount = grossPremium.HasValue
            ? RoundMoney(grossPremium.Value * cedentCommissionPct / 100m)
            : (decimal?)null;
        decimal? brokerageIrisAmount = grossPremium.HasValue
            ? RoundMoney(grossPremium.Value * (brokeragePct ?? 0m) / 100m)
            : (decimal?)null;
        decimal? netPremium = grossPremium.HasValue
            ? RoundMoney(grossPremium.Value - (cedentCommissionAmount ?? 0m) -
                         (brokerageIrisAmount ?? 0m) - otherDeductions)
            : (decimal?)null;

        return new(
            sumInsured,
            premiumRatePct,
            limitType,
            limitValue,
            limitAmount,
            deductibleType,
            deductibleValue,
            deductibleAmount,
            cedentCommissionPct,
            cedentCommissionAmount,
            brokeragePct,
            brokerageIrisAmount,
            RoundMoney(otherDeductions),
            grossPremium,
            netPremium);
    }

    public static void Apply(ContractPanel panel, ContractPanelFinancialCalculation calculation)
    {
        panel.Tsi = calculation.SumInsured;
        panel.PremiumRatePct = calculation.PremiumRatePct;
        panel.LimitOfLiabilityType = calculation.LimitOfLiabilityType;
        panel.LimitOfLiabilityValue = calculation.LimitOfLiabilityValue;
        panel.LimitOfLiabilityAmount = calculation.LimitOfLiabilityAmount;
        panel.DeductibleType = calculation.DeductibleType;
        panel.DeductibleValue = calculation.DeductibleValue;
        panel.DeductibleAmount = calculation.DeductibleAmount;
        panel.Deductible = calculation.DeductibleAmount;
        panel.CedentCommissionPct = calculation.CedentCommissionPct;
        panel.CedentCommissionAmount = calculation.CedentCommissionAmount;
        panel.BrokeragePct = calculation.BrokerageIrisPct;
        panel.BrokerageIrisAmount = calculation.BrokerageIrisAmount;
        panel.OtherDeductions = calculation.OtherDeductions;
        panel.GrossPremium = calculation.GrossPremium;
        panel.NetPremium = calculation.NetPremium;
    }

    public static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string? NormalizeType(string? value, bool hasValue)
    {
        if (!hasValue && string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (string.Equals(value?.Trim(), PercentageType, StringComparison.OrdinalIgnoreCase))
        {
            return PercentageType;
        }

        return AmountType;
    }
}
