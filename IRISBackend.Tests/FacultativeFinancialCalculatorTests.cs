using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class FacultativeFinancialCalculatorTests
{
    [Fact]
    public void Calculate_UsesPercentagePremiumRateAndSubtractsCedentCommission()
    {
        var result = FacultativeFinancialCalculator.Calculate(100000m, 2m, 200m, 5m);

        Assert.NotNull(result);
        Assert.Equal(2000m, result!.GrossPremium);
        Assert.Equal(200m, result.IrisCommission);
        Assert.Equal(100m, result.CedentCommission);
        Assert.Equal(1900m, result.NetPremium);
    }

    [Fact]
    public void Calculate_InterpretsPointOneAsPointOnePercent()
    {
        var result = FacultativeFinancialCalculator.Calculate(
            500000m, 0.1m, irisCommission: 0m, cedentCommissionPct: 10m, currencyDecimalPlaces: 3);

        Assert.NotNull(result);
        Assert.Equal(500m, result!.GrossPremium);
        Assert.NotEqual(50000m, result.GrossPremium);
        Assert.Equal(50m, result.CedentCommission);
        Assert.Equal(450m, result.NetPremium);
    }

    [Fact]
    public void Calculate_ReturnsZeroCommissionAmountsWhenBothRatesAreZero()
    {
        var result = FacultativeFinancialCalculator.Calculate(100000m, 2m, 0m, 0m);

        Assert.NotNull(result);
        Assert.Equal(0m, result!.IrisCommission);
        Assert.Equal(0m, result.CedentCommission);
        Assert.Equal(2000m, result.NetPremium);
    }

    [Fact]
    public void Calculate_ReturnsNullWhenTechnicalInputsAreIncomplete()
    {
        Assert.Null(FacultativeFinancialCalculator.Calculate(null, 0.02m, 0m, 0m));
        Assert.Null(FacultativeFinancialCalculator.Calculate(100000m, null, 0m, 0m));
    }
}
