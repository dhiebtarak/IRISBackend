using IRISBackend.Models;
using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class ContractPanelFinancialCalculatorTests
{
    private readonly PremiumCalculationService _calculator = new();

    [Fact]
    public void CalculatePanel_UsesPersistedContractNetAndRelativePanelShare()
    {
        var contract = CreateContract();
        var panel = CreatePanel(signedShare: 5m, brokerage: 10m);

        var result = _calculator.CalculatePanel(contract, panel, 3);

        Assert.Equal(15m, result.Contract.CedentCommissionAmount);
        Assert.Equal(135m, result.Contract.NetPremium);
        Assert.Equal(75m, result.PanelGrossPremium);
        Assert.Equal(7.5m, result.BrokerageAmount);
        Assert.Equal(60m, result.PanelNetPremium);
    }

    [Fact]
    public void CalculatePanel_SubtractsOtherDeductionsAsMoney()
    {
        var panel = CreatePanel(signedShare: 5m, brokerage: 10m);
        panel.OtherDeductions = 2.25m;

        var result = _calculator.CalculatePanel(CreateContract(), panel, 3);

        Assert.Equal(2.25m, result.OtherDeductionsAmount);
        Assert.Equal(57.75m, result.PanelNetPremium);
    }

    [Fact]
    public void CalculatePanel_CalculatesOtherDeductionsFromDecimalPercentage()
    {
        var panel = CreatePanel(signedShare: 10m, brokerage: 10m);
        panel.OtherDeductionsPercentage = 2.5m;

        var result = _calculator.CalculatePanel(CreateContract(), panel, 3);

        Assert.Equal(150m, result.PanelGrossPremium);
        Assert.Equal(3.75m, result.OtherDeductionsAmount);
        Assert.Equal(116.25m, result.PanelNetPremium);
        Assert.Equal(2.5m, panel.OtherDeductionsPercentage);
        Assert.Equal(result.OtherDeductionsAmount, panel.OtherDeductions);
    }

    [Fact]
    public void CalculatePanel_PreservesLegacyMonetaryDeductionWhenPercentageWasNeverSet()
    {
        var panel = CreatePanel(signedShare: 5m, brokerage: 10m);
        panel.OtherDeductions = 2.25m;

        var result = _calculator.CalculatePanel(CreateContract(), panel, 3);

        Assert.Null(panel.OtherDeductionsPercentage);
        Assert.Equal(2.25m, result.OtherDeductionsAmount);
    }

    [Fact]
    public void RecalculatePanels_DistributesAndReconcilesContractNetPremium()
    {
        var contract = CreateContract();
        contract.ContractPanels.Add(CreatePanel(signedShare: 3.33333333m, brokerage: 0m, id: 1));
        contract.ContractPanels.Add(CreatePanel(signedShare: 3.33333333m, brokerage: 0m, id: 2));
        contract.ContractPanels.Add(CreatePanel(signedShare: 3.33333334m, brokerage: 0m, id: 3));

        var errors = _calculator.RecalculatePanels(contract, 3);

        Assert.Empty(errors);
        Assert.Equal(150m, contract.ContractPanels.Sum(panel => panel.GrossPremium ?? 0m));
    }

    [Fact]
    public void RecalculatePanels_UpdatesAfterContractOrPanelInputsChange_AndIsIdempotent()
    {
        var contract = CreateContract();
        var panel = CreatePanel(signedShare: 5m, brokerage: 10m);
        contract.ContractPanels.Add(panel);
        Assert.Empty(_calculator.RecalculatePanels(contract, 3));
        Assert.Equal(75m, panel.GrossPremium);

        contract.GrossPremium = 300m;
        contract.CedentCommission = 20m;
        panel.BrokeragePct = 15m;
        Assert.Empty(_calculator.RecalculatePanels(contract, 3));
        var grossAfterUpdate = panel.GrossPremium;
        var netAfterUpdate = panel.NetPremium;

        Assert.Equal(150m, grossAfterUpdate);
        Assert.Equal(97.5m, netAfterUpdate);
        Assert.Empty(_calculator.RecalculatePanels(contract, 3));
        Assert.Equal(grossAfterUpdate, panel.GrossPremium);
        Assert.Equal(netAfterUpdate, panel.NetPremium);
    }

    [Fact]
    public void RecalculatePanels_DoesNotModifyHistoricalInvoiceValues()
    {
        var contract = CreateContract();
        var historicalInvoice = new Invoice { GrossPremium = 83.25m, NetPremium = 70m };
        contract.Invoices.Add(historicalInvoice);
        contract.ContractPanels.Add(CreatePanel(signedShare: 5m, brokerage: 10m));

        Assert.Empty(_calculator.RecalculatePanels(contract, 3));

        Assert.Equal(83.25m, historicalInvoice.GrossPremium);
        Assert.Equal(70m, historicalInvoice.NetPremium);
    }

    [Theory]
    [InlineData(null)]
    public void ValidateContract_RejectsMissingOrZeroSignedShare(decimal? share)
    {
        var contract = CreateContract();
        contract.SignedSharePctCedent = share;

        Assert.Contains(_calculator.ValidateContract(contract), error => error.Contains("signedSharePctCedent"));
    }

    [Fact]
    public void ValidateContract_RejectsZeroSignedShare()
    {
        var contract = CreateContract();
        contract.SignedSharePctCedent = 0m;

        Assert.Contains(_calculator.ValidateContract(contract), error => error.Contains("signedSharePctCedent"));
    }

    [Fact]
    public void ValidatePanel_RejectsInvalidPercentagesAndNegativeDeductions()
    {
        var contract = CreateContract();
        var panel = CreatePanel(signedShare: 12m, brokerage: 101m);
        panel.WrittenSharePct = 10m;
        panel.OtherDeductions = -1m;
        panel.OtherDeductionsPercentage = 100.01m;

        var errors = _calculator.ValidatePanel(contract, panel);

        Assert.Contains(errors, error => error.Contains("signedSharePct cannot exceed writtenSharePct"));
        Assert.Contains(errors, error => error.Contains("brokeragePct"));
        Assert.Contains(errors, error => error.Contains("otherDeductions"));
        Assert.Contains(errors, error => error.Contains("otherDeductionsPercentage"));
    }

    [Fact]
    public void CalculateContract_ZeroCedentCommissionKeepsNetEqualToGross()
    {
        var contract = CreateContract();
        contract.CedentCommission = 0m;

        var result = _calculator.CalculateContract(contract, 3);

        Assert.Equal(0m, result.CedentCommissionAmount);
        Assert.Equal(150m, result.NetPremium);
    }

    [Fact]
    public void CalculatePanel_DoesNotUsePanelTsiOrClientTotalsAsPremiumSource()
    {
        var panel = CreatePanel(signedShare: 5m, brokerage: 0m);
        panel.Tsi = 999999m;
        panel.PremiumRatePct = 99m;
        panel.GrossPremium = 999999m;
        panel.NetPremium = 999999m;

        var result = _calculator.CalculatePanel(CreateContract(), panel, 3);

        Assert.Equal(75m, result.PanelGrossPremium);
        Assert.Equal(75m, panel.GrossPremium);
        Assert.Equal(67.5m, panel.NetPremium);
    }

    private static Contract CreateContract() => new()
    {
        GrossPremium = 150m,
        CedentCommission = 10m,
        SignedSharePctCedent = 10m,
        ContractPanels = []
    };

    private static ContractPanel CreatePanel(decimal signedShare, decimal brokerage, int id = 1) => new()
    {
        IdPanel = id,
        IdContract = 1,
        IdCounterparty = id,
        Role = "Reinsurer",
        Status = ContractConstants.DraftStatus,
        WrittenSharePct = signedShare,
        SignedSharePct = signedShare,
        BrokeragePct = brokerage,
        OtherDeductions = 0m
    };
}
