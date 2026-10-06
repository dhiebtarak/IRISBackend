using IRISBackend.Services;
using System.Security.Claims;
using Xunit;

namespace IRISBackend.Tests;

public sealed class FinancialAccessRulesTests
{
    [Fact]
    public void FinanceOfficer_IsAllowedFinanceOperations()
    {
        var user = Principal("Finance officer");
        Assert.True(FinancialAccessRules.IsFinanceOfficer(user));
        Assert.False(FinancialAccessRules.IsBackOfficer(user));
    }

    [Fact]
    public void BackOfficer_IsAllowedBackOfficeOperations()
    {
        var user = Principal("Back officer");
        Assert.True(FinancialAccessRules.IsBackOfficer(user));
        Assert.False(FinancialAccessRules.IsFinanceOfficer(user));
    }

    [Fact]
    public void UnrelatedPosition_IsRejected()
    {
        var user = Principal("Front officer");
        Assert.False(FinancialAccessRules.IsFinanceOrBackOfficer(user));
    }

    private static ClaimsPrincipal Principal(string position) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, "user-1"),
        new Claim("position", position)
    ], "test"));
}
