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
        decimal? cedentCommissionPct)
    {
        if (!tsi.HasValue || !premiumRate.HasValue)
        {
            return null;
        }

        var grossPremium = RoundMoney(tsi.Value * premiumRate.Value);
        var irisAmount = RoundMoney(irisCommission ?? 0m);
        var cedentAmount = RoundMoney(grossPremium * (cedentCommissionPct ?? 0m) / 100m);
        var netPremium = RoundMoney(grossPremium - irisAmount - cedentAmount);

        return new FacultativeFinancialCalculation(grossPremium, irisAmount, cedentAmount, netPremium);
    }

    public static decimal RoundMoney(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}
