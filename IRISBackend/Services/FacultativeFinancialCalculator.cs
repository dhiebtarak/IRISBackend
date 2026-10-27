namespace IRISBackend.Services;

public sealed record FacultativeFinancialCalculation(
    decimal GrossPremium,
    decimal IrisCommission,
    decimal CedentCommission,
    decimal NetPremium);

public static class FacultativeFinancialCalculator
{
    public static FacultativeFinancialCalculation? Calculate(
        decimal? tsi,
        decimal? premiumRate,
        decimal? irisCommission,
        decimal? cedentCommissionPct,
        int? currencyDecimalPlaces = null)
    {
        if (!tsi.HasValue || !premiumRate.HasValue)
        {
            return null;
        }

        var precision = PanelRemittanceCalculator.Precision(currencyDecimalPlaces ?? 4);
        var grossPremium = CalculateGrossPremium(tsi.Value, premiumRate.Value, precision);
        var irisAmount = RoundMoney(irisCommission ?? 0m, precision);
        var cedentAmount = RoundMoney(grossPremium * (cedentCommissionPct ?? 0m) / 100m, precision);
        var netPremium = RoundMoney(grossPremium - cedentAmount, precision);

        return new FacultativeFinancialCalculation(grossPremium, irisAmount, cedentAmount, netPremium);
    }

    public static decimal CalculateGrossPremium(decimal sumInsured, decimal premiumRatePercentage, int? currencyDecimalPlaces = null)
    {
        var precision = PanelRemittanceCalculator.Precision(currencyDecimalPlaces ?? 4);
        var premiumRateFactor = premiumRatePercentage / 100m;
        return RoundMoney(sumInsured * premiumRateFactor, precision);
    }

    public static decimal RoundMoney(decimal value, int precision) => decimal.Round(value, precision, MidpointRounding.AwayFromZero);

    public static decimal RoundMoney(decimal value) => RoundMoney(value, 4);
}
