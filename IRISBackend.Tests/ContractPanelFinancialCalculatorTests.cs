using IRISBackend.DTOs;
using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class ContractPanelFinancialCalculatorTests
{
    [Fact]
    public void Calculate_UsesAuthoritativePanelFormula()
    {
        var result = ContractPanelFinancialCalculator.Calculate(new ContractPanelRequestDto
        {
            WrittenSharePct = 10m,
            SignedSharePct = 5m,
            Tsi = 100000m,
            PremiumRatePct = 0.05m,
            CedentCommissionPct = 10m,
            BrokerageIrisPct = 5m,
            OtherDeductions = 0m,
            DeductibleType = "Amount",
            DeductibleValue = 0m
        });

        Assert.Equal(25m, result.GrossPremium);
        Assert.Equal(2.5m, result.CedentCommissionAmount);
        Assert.Equal(1.25m, result.BrokerageIrisAmount);
        Assert.Equal(21.25m, result.NetPremium);
    }

    [Fact]
    public void Calculate_UsesPercentageDeductibleAndLimit()
    {
        var result = ContractPanelFinancialCalculator.Calculate(new ContractPanelRequestDto
        {
            WrittenSharePct = 10m,
            SignedSharePct = 10m,
            SumInsured = 100000m,
            PremiumRatePct = 40m,
            LimitOfLiabilityType = "Percentage",
            LimitOfLiabilityValue = 25m,
            DeductibleType = "Percentage",
            DeductibleValue = 10m
        });

        Assert.Equal(25000m, result.LimitOfLiabilityAmount);
        Assert.Equal(4000m, result.DeductibleAmount);
        Assert.Equal(40000m, result.NetPremium);
    }

    [Fact]
    public void Calculate_DoesNotTrustClientCalculatedTotals()
    {
        var result = ContractPanelFinancialCalculator.Calculate(new ContractPanelRequestDto
        {
            WrittenSharePct = 10m,
            SignedSharePct = 10m,
            Tsi = 100000m,
            PremiumRatePct = 40m,
            GrossPremium = 1m,
            NetPremium = 1m,
            BrokerageIrisPct = 10m
        });

        Assert.Equal(40000m, result.GrossPremium);
        Assert.Equal(36000m, result.NetPremium);
    }
}
