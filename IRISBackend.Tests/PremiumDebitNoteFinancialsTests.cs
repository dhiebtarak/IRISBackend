using IRISBackend.Models;
using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class PremiumDebitNoteFinancialsTests
{
    [Fact]
    public void ResolveContractGrossPremium_UsesCalculatedGrossPremiumBeforePersistedValue()
    {
        var contract = new Contract
        {
            GrossPremium = 5000m,
            ContractFac = new ContractFac { SumInsured = 100000m, PremiumRate = 0.05m }
        };

        Assert.Equal(50m, PremiumDebitNoteFinancials.ResolveContractGrossPremium(contract));
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
}
