using IRISBackend.Models;
using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class PremiumDebitNoteAllocationCalculatorTests
{
    [Fact]
    public void Calculate_UsesContractCedentCommissionForEveryPanelAndPersistsExpectedAmounts()
    {
        var contract = new Contract
        {
            CedentCommission = 10m,
            IdCurrencyNavigation = new Currency { CurrencyCode = "TND" }
        };
        var panels = new[]
        {
            new ContractPanel
            {
                IdPanel = 1,
                SignedSharePct = 50m,
                CedentCommissionPct = null,
                BrokeragePct = 10m,
                GrossPremium = 25m,
                Deductible = 500m,
                IdCounterpartyNavigation = new Counterparty { Name = "AMI" }
            },
            new ContractPanel
            {
                IdPanel = 2,
                SignedSharePct = 50m,
                CedentCommissionPct = null,
                BrokeragePct = 20m,
                GrossPremium = 25m,
                Deductible = 500m,
                IdCounterpartyNavigation = new Counterparty { Name = "STAR Assurances" }
            }
        };

        var result = PremiumDebitNoteAllocationCalculator.Calculate(contract, panels, 50m);

        Assert.Equal(50m, result.Allocations.Sum(line => line.GrossPremium));
        Assert.Equal(new decimal?[] { 10m, 10m }, result.Allocations.Select(line => line.CedentCommissionPct));
        Assert.Equal(new[] { 2.5m, 2.5m }, result.Allocations.Select(line => line.CedentCommissionAmount));
        Assert.Equal(new[] { 20m, 17.5m }, result.Allocations.Select(line => line.NetAmount));
        Assert.Equal(37.5m, result.TotalNetAmount);
    }
}
