using IRISBackend.Models;
using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class PremiumDebitNoteFinancialsTests
{
    [Fact]
    public void ResolveContractGrossPremium_UsesPersistedGrossPremiumAsSourceOfTruth()
    {
        var contract = new Contract
        {
            GrossPremium = 5000m,
            ContractFac = new ContractFac { SumInsured = 100000m, PremiumRate = 0.05m }
        };

        Assert.Equal(5000m, PremiumDebitNoteFinancials.ResolveContractGrossPremium(contract));
    }

    [Fact]
    public void ResolveContractGrossPremium_UsesPercentageFallbackWhenPersistedValueIsNull()
    {
        var contract = new Contract
        {
            GrossPremium = null,
            ContractFac = new ContractFac { SumInsured = 100000m, PremiumRate = 0.05m }
        };

        Assert.Equal(50m, PremiumDebitNoteFinancials.ResolveContractGrossPremium(contract));
    }

    [Fact]
    public void ResolveContractGrossPremium_UsesPercentUnitsForPointOnePercent()
    {
        var contract = new Contract
        {
            ContractFac = new ContractFac { SumInsured = 500000m, PremiumRate = 0.1m },
            IdCurrencyNavigation = new Currency { DecimalPlaces = 3 }
        };

        var grossPremium = PremiumDebitNoteFinancials.ResolveContractGrossPremium(contract);

        Assert.Equal(500m, grossPremium);
        Assert.NotEqual(50000m, grossPremium);
    }

    [Fact]
    public void InvoiceablePremiumUsesFullContractGrossAndIgnoresCedentSignedShare()
    {
        var contract = new Contract
        {
            GrossPremium = 2000m,
            CedentCommission = 10m,
            SignedSharePctCedent = 20m,
            IdCurrencyNavigation = new Currency { DecimalPlaces = 3 }
        };

        var valid = PremiumDebitNoteFinancials.TryCalculateInvoiceablePremium(
            contract, out var result, out var error);

        Assert.True(valid, error);
        Assert.Equal(2000m, result!.ContractGrossPremium);
        Assert.Equal(200m, result.CedentCommissionAmount);
        Assert.Equal(1800m, result.InvoiceableNetPremium);
    }

    [Fact]
    public void SignedInvoiceablePremium_ConvertsPercentagesAndAppliesCedentSignedShare()
    {
        var contract = new Contract
        {
            GrossPremium = 500m,
            CedentCommission = 10m,
            SignedSharePctCedent = 10m,
            IdCurrencyNavigation = new Currency { DecimalPlaces = 3 },
            ContractFac = new ContractFac { SumInsured = 500000m, PremiumRate = 0.1m }
        };

        var valid = PremiumDebitNoteFinancials.TryCalculateSignedInvoiceablePremium(
            contract, out var result, out var error);

        Assert.True(valid, error);
        Assert.Equal(500m, result!.ContractGrossPremium);
        Assert.Equal(10m, result.CedentCommissionPercentage);
        Assert.Equal(50m, result.CedentCommissionAmount);
        Assert.Equal(10m, result.ContractSignedSharePercentage);
        Assert.Equal(50m, result.SignedGrossPremium);
        Assert.Equal(5m, result.SignedCedentCommissionAmount);
        Assert.Equal(45m, result.InvoiceableNetPremium);
    }

    [Theory]
    [InlineData(500, 10, 10, 45)]
    [InlineData(800, 5, 25, 190)]
    public void SignedInvoiceablePremium_IsCalculatedDynamically(
        decimal gross, decimal commissionPercentage, decimal signedSharePercentage, decimal expectedNet)
    {
        var contract = new Contract
        {
            GrossPremium = gross,
            CedentCommission = commissionPercentage,
            SignedSharePctCedent = signedSharePercentage,
            IdCurrencyNavigation = new Currency { DecimalPlaces = 3 }
        };

        Assert.True(PremiumDebitNoteFinancials.TryCalculateSignedInvoiceablePremium(
            contract, out var result, out var error), error);
        Assert.Equal(expectedNet, result!.InvoiceableNetPremium);
    }

    [Fact]
    public void SignedInvoiceablePremium_RejectsInvalidSignedShareAndCommission()
    {
        var contract = new Contract { GrossPremium = 500m, CedentCommission = 10m, SignedSharePctCedent = 0m };

        Assert.False(PremiumDebitNoteFinancials.TryCalculateSignedInvoiceablePremium(contract, out _, out var error));
        Assert.Contains("contractSignedSharePercentage", error);

        contract.SignedSharePctCedent = 10m;
        contract.CedentCommission = 101m;
        Assert.False(PremiumDebitNoteFinancials.TryCalculateSignedInvoiceablePremium(contract, out _, out error));
        Assert.Contains("cedentCommissionPercentage", error);
    }
}
