using IRISBackend.Models;
using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class PremiumDebitNoteAllocationCalculatorTests
{
    [Fact]
    public void Calculate_GrossesUpNetInvoiceAndAllocatesCommissionOnce()
    {
        var (contract, panels) = CreateExample();

        var result = PremiumDebitNoteAllocationCalculator.Calculate(contract, panels, 45m, 3);

        Assert.Equal(50m, result.InvoiceGrossEquivalent);
        Assert.Equal(5m, result.TotalCedentCommissionAmount);
        Assert.Equal(45m, result.TotalAllocatedNetPremium);
        Assert.Equal(45m, result.TotalNetAmount);
        Assert.Equal(42.5m, result.TotalNetPayableAmount);
        Assert.Equal(new[] { 25m, 25m }, result.Allocations.Select(line => line.AllocatedGrossPremium));
        Assert.Equal(new[] { 50m, 50m }, result.Allocations.Select(line => line.AllocationRatioPercentage));
        Assert.Equal(new[] { 2.5m, 2.5m }, result.Allocations.Select(line => line.CedentCommissionAmount));
        Assert.Equal(new[] { 22.5m, 22.5m }, result.Allocations.Select(line => line.AllocatedNetPremium));
        Assert.Equal(new[] { 22.5m, 22.5m }, result.Allocations.Select(line => line.NetAmount));
        Assert.Equal(new[] { 2.5m, 0m }, result.Allocations.Select(line => line.IrisBrokerageAmount));
        Assert.Equal(new[] { 20m, 22.5m }, result.Allocations.Select(line => line.NetPayableAmount));
        Assert.Equal(45m, result.Allocations.Sum(line => line.AllocatedNetPremium));
    }

    [Fact]
    public void CalculateUsesFullContractPremiumEvenWhenCedentSignedShareIsTwentyPercent()
    {
        var contract = new Contract
        {
            GrossPremium = 2000m,
            CedentCommission = 10m,
            SignedSharePctCedent = 20m,
            IdCurrencyNavigation = new Currency { CurrencyCode = "TND", DecimalPlaces = 3 }
        };
        var panels = new[]
        {
            new ContractPanel { IdPanel = 1, IdCounterparty = 1, SignedSharePct = 10m },
            new ContractPanel { IdPanel = 2, IdCounterparty = 2, SignedSharePct = 10m }
        };

        var result = PremiumDebitNoteAllocationCalculator.Calculate(contract, panels, 1800m, 3);

        Assert.Equal(2000m, result.InvoiceGrossEquivalent);
        Assert.Equal(200m, result.TotalCedentCommissionAmount);
        Assert.Equal(1800m, result.TotalAllocatedNetPremium);
        Assert.Equal(new[] { 1000m, 1000m }, result.Allocations.Select(line => line.AllocatedGrossPremium));
    }

    [Fact]
    public void Calculate_BrokerageUsesAllocatedGrossPremium()
    {
        var contract = new Contract
        {
            GrossPremium = 44.445m,
            CedentCommission = 10m,
            SignedSharePctCedent = 100m,
            IdCurrencyNavigation = new Currency { CurrencyCode = "TND", DecimalPlaces = 3 }
        };
        var panels = new[]
        {
            new ContractPanel
            {
                IdPanel = 3,
                IdCounterparty = 3,
                SignedSharePct = 100m,
                BrokeragePct = 10m,
                OtherDeductions = 0.025m,
                GrossPremium = 44.445m,
                IdCounterpartyNavigation = new Counterparty { Name = "Reinsurer" }
            }
        };

        var result = PremiumDebitNoteAllocationCalculator.Calculate(contract, panels, 40m, 3);

        Assert.Equal(44.445m, result.Allocations[0].AllocatedGrossPremium);
        Assert.Equal(40m, result.Allocations[0].AllocatedNetPremium);
        Assert.Equal(10m, result.Allocations[0].IrisBrokeragePercentage);
        Assert.Equal(4.445m, result.Allocations[0].IrisBrokerageAmount);
        Assert.Equal(4.445m, result.Allocations[0].BrokerageAmount);
        Assert.Equal(35.53m, result.Allocations[0].NetPayableAmount);
        Assert.Equal(1.111m, PremiumDebitNoteAllocationCalculator.CalculateIrisBrokerage(11.111m, 10m, 3));
    }

    [Fact]
    public void Calculate_UsesPanelOtherDeductionsPercentageForInvoiceAndAvoirAllocationSnapshots()
    {
        var (contract, panels) = CreateExample();
        panels[0].OtherDeductionsPercentage = 2m;

        var result = PremiumDebitNoteAllocationCalculator.Calculate(contract, panels, 45m, 3);

        Assert.Equal(0.5m, result.Allocations[0].OtherDeductionsAmount);
        Assert.Equal(2m, result.Allocations[0].OtherDeductionsPercentage);
        Assert.Equal(19.5m, result.Allocations[0].NetPayableAmount);
        Assert.Equal(0m, result.Allocations[1].OtherDeductionsAmount);
    }

    [Fact]
    public void Calculate_SupportsZeroAndDifferentBrokerageRates()
    {
        var contract = new Contract
        {
            GrossPremium = 100m,
            CedentCommission = 0m,
            SignedSharePctCedent = 100m,
            IdCurrencyNavigation = new Currency { CurrencyCode = "TND", DecimalPlaces = 3 }
        };
        var panels = new[]
        {
            new ContractPanel { IdPanel = 1, SignedSharePct = 60m, GrossPremium = 60m, BrokeragePct = 10m },
            new ContractPanel { IdPanel = 2, SignedSharePct = 40m, GrossPremium = 40m, BrokeragePct = 0m }
        };

        var result = PremiumDebitNoteAllocationCalculator.Calculate(contract, panels, 100m, 3);

        Assert.Equal(new[] { 6m, 0m }, result.Allocations.Select(line => line.IrisBrokerageAmount));
        Assert.Equal(new[] { 54m, 40m }, result.Allocations.Select(line => line.NetPayableAmount));
    }

    [Fact]
    public void Calculate_PartialInvoiceUsesTheSameGrossUpAndDistribution()
    {
        var (contract, panels) = CreateExample();

        var result = PremiumDebitNoteAllocationCalculator.Calculate(contract, panels, 22.5m, 3);

        Assert.Equal(25m, result.InvoiceGrossEquivalent);
        Assert.Equal(2.5m, result.TotalCedentCommissionAmount);
        Assert.Equal(22.5m, result.TotalAllocatedNetPremium);
        Assert.Equal(new[] { 12.5m, 12.5m }, result.Allocations.Select(line => line.AllocatedGrossPremium));
        Assert.Equal(new[] { 11.25m, 11.25m }, result.Allocations.Select(line => line.AllocatedNetPremium));
        Assert.Equal(22.5m, result.Allocations.Sum(line => line.AllocatedNetPremium));
    }

    [Fact]
    public void Calculate_AssignsCurrencyRoundingRemainderDeterministically()
    {
        var contract = new Contract
        {
            GrossPremium = 100m,
            CedentCommission = 10m,
            SignedSharePctCedent = 100m,
            IdCurrencyNavigation = new Currency { CurrencyCode = "USD", DecimalPlaces = 2 }
        };
        var panels = new[]
        {
            new ContractPanel { IdPanel = 1, SignedSharePct = 50m, GrossPremium = 50m, IdCounterparty = 1 },
            new ContractPanel { IdPanel = 2, SignedSharePct = 50m, GrossPremium = 50m, IdCounterparty = 2 }
        };

        var result = PremiumDebitNoteAllocationCalculator.Calculate(contract, panels, 10.01m, 2);

        Assert.Equal(10.01m, result.Allocations.Sum(line => line.AllocatedNetPremium));
        Assert.Equal(result.InvoiceGrossEquivalent, result.Allocations.Sum(line => line.AllocatedGrossPremium));
        Assert.Equal(result.TotalCedentCommissionAmount, result.Allocations.Sum(line => line.CedentCommissionAmount));
    }

    [Fact]
    public void CalculateUsesPanelRatiosWithoutRequiringContractSignedShareReconciliation()
    {
        var (contract, panels) = CreateExample();
        panels[1].SignedSharePct = 4m;

        var result = PremiumDebitNoteAllocationCalculator.Calculate(contract, panels, 45m, 3);

        Assert.Equal(50m, result.InvoiceGrossEquivalent);
        Assert.Equal(45m, result.TotalAllocatedNetPremium);
        Assert.Equal(100m, result.Allocations.Sum(line => line.AllocationRatioPercentage));
    }

    private static (Contract Contract, ContractPanel[] Panels) CreateExample()
    {
        var contract = new Contract
        {
            GrossPremium = 500m,
            CedentCommission = 10m,
            SignedSharePctCedent = 10m,
            IdCurrencyNavigation = new Currency { CurrencyCode = "TND", DecimalPlaces = 3 },
            ContractFac = new ContractFac { SumInsured = 500000m, PremiumRate = 0.1m }
        };
        var panels = new[]
        {
            new ContractPanel
            {
                IdPanel = 1,
                IdCounterparty = 1,
                SignedSharePct = 5m,
                BrokeragePct = 10m,
                OtherDeductions = 0m,
                GrossPremium = 25m,
                IdCounterpartyNavigation = new Counterparty { Name = "AMI" }
            },
            new ContractPanel
            {
                IdPanel = 2,
                IdCounterparty = 2,
                SignedSharePct = 5m,
                BrokeragePct = 0m,
                OtherDeductions = 0m,
                GrossPremium = 25m,
                IdCounterpartyNavigation = new Counterparty { Name = "STAR Assurances" }
            }
        };
        return (contract, panels);
    }
}
