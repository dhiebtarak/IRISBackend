using System.Security.Claims;

namespace IRISBackend.Services;

public static class FinancialAccessRules
{
    public static bool IsFinanceOfficer(ClaimsPrincipal user) => HasPosition(user, "finance officer");
    public static bool IsBackOfficer(ClaimsPrincipal user) => HasPosition(user, "back officer");
    public static bool IsFinanceOrBackOfficer(ClaimsPrincipal user) => IsFinanceOfficer(user) || IsBackOfficer(user);

    private static bool HasPosition(ClaimsPrincipal user, string position)
    {
        var actual = user.FindFirstValue("position");
        return user.Identity?.IsAuthenticated == true &&
               string.Equals(actual?.Trim(), position, StringComparison.OrdinalIgnoreCase);
    }
}
