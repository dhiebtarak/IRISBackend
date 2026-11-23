using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class ClaimNetLossTests
{
    [Theory]
    [InlineData(15000, 1000, 150, 1000, 15150)]
    [InlineData(15000, 1000, 150, 0, 16150)]
    [InlineData(15000, 1000, 150, null, 16150)]
    [InlineData(15000, 1000, 150, 17000, -850)]
    public void UsesClaimDeductibleExactlyOnce(decimal paid, decimal expert, decimal other, int? deductible, decimal expected)
        => Assert.Equal(expected, FinancialWorkflowService.CalculateClaimNetLoss(paid, expert, other, deductible, 2));

    [Fact]
    public void KeepsDecimalArithmeticAndCurrencyRounding()
    {
        Assert.Equal(0.2m, FinancialWorkflowService.CalculateClaimNetLoss(0.1m, 0.2m, null, 0.1m, 3));
        Assert.Equal(15149.8766m, FinancialWorkflowService.CalculateClaimNetLoss(15000m, 1000m, 150m, 1000.1234m, 4));
        Assert.Equal(15149.88m, FinancialWorkflowService.CalculateClaimNetLoss(15000m, 1000m, 150m, 1000.1234m, 2));
        Assert.Equal(0m, FinancialWorkflowService.CalculateClaimNetLoss(null, null, null, null, 3));
    }
    [Fact]
    public void UsesActualPanelPercentagesWithoutNormalizingTheirCombinedShare()
    {
        Assert.Equal(new[] {750m,750m},FinancialWorkflowService.CalculateClaimShareAmounts(15000m,new[]{5m,5m},3));
        Assert.Equal(new[] {750m,1500m},FinancialWorkflowService.CalculateClaimShareAmounts(15000m,new[]{5m,10m},3));
        Assert.Equal(new[] {15000m},FinancialWorkflowService.CalculateClaimShareAmounts(15000m,new[]{100m},3));
        Assert.Equal(new[] {0.001m,0.001m,0m,0m},FinancialWorkflowService.CalculateClaimShareAmounts(0.02m,new[]{2.5m,2.5m,2.5m,2.5m},3));
        Assert.Throws<ArgumentException>(() => FinancialWorkflowService.CalculateClaimShareAmounts(15000m,new[]{60m,50m},3));
        Assert.Throws<ArgumentException>(() => FinancialWorkflowService.CalculateClaimShareAmounts(-1m,new[]{5m,5m},3));
    }

}
