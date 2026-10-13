using IRISBackend.Services;
using Xunit;

namespace IRISBackend.Tests;

public sealed class PanelRemittanceCalculatorTests
{
    [Theory]
    [InlineData(1000, 999, 3, false)]
    [InlineData(1000, 1000, 3, true)]
    [InlineData(1000, 1001, 3, false)]
    [InlineData(0, 0, 2, false)]
    public void FullPairingRequiresEquality(decimal received, decimal allocated, int precision, bool expected) =>
        Assert.Equal(expected, PanelRemittanceCalculator.IsFullyPaired(received, allocated, precision));

    [Fact]
    public void PairingToleranceUsesHalfMinorUnit()
    {
        Assert.True(PanelRemittanceCalculator.IsFullyPaired(1m, 0.9996m, 3));
        Assert.False(PanelRemittanceCalculator.IsFullyPaired(1m, 0.9995m, 3));
    }

    [Fact]
    public void DistributionUsesNetPremiumWeights() =>
        Assert.Equal(new[] { 600m, 400m }, PanelRemittanceCalculator.Distribute(1000m, [600m, 400m], [600m, 400m], 3));

    [Fact]
    public void FinalPanelReceivesRoundingRemainder() =>
        Assert.Equal(new[] { 0.333m, 0.333m, 0.334m }, PanelRemittanceCalculator.Distribute(1m, [1m, 1m, 1m], [1m, 1m, 1m], 3));

    [Fact]
    public void DistributionRespectsOutstandingAmounts() =>
        Assert.Equal(new[] { 100m, 400m }, PanelRemittanceCalculator.Distribute(500m, [600m, 400m], [100m, 400m], 3));

    [Fact]
    public void InsufficientCapacityIsRejected() =>
        Assert.Throws<OutgoingRemittanceValidationException>(() => PanelRemittanceCalculator.Distribute(1000m, [600m, 400m], [100m, 400m], 3));

    [Fact]
    public void RoundingCannotExceedFinalPanelCapacity()
    {
        var amounts = PanelRemittanceCalculator.Distribute(0.02m, [1m, 1m, 1m], [0.01m, 0.01m, 0.01m], 2);
        Assert.Equal(0.02m, amounts.Sum());
        Assert.All(amounts, x => Assert.InRange(x, 0m, 0.01m));
    }
}
