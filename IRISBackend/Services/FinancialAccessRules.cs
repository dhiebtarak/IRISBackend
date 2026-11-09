using System.Security.Claims;

namespace IRISBackend.Services;

public static class FinancialAccessRules
{
    public static bool IsFinanceOfficer(ClaimsPrincipal user) =>
        HasModule(user, ModuleAccessCatalog.Encaissement) ||
        HasModule(user, ModuleAccessCatalog.Decaissement) ||
        HasModule(user, ModuleAccessCatalog.CounterpartyBankAccounts);
    public static bool IsBackOfficer(ClaimsPrincipal user) => HasModule(user, ModuleAccessCatalog.BackOffice);
    public static bool IsFinanceOrBackOfficer(ClaimsPrincipal user) => IsFinanceOfficer(user) || IsBackOfficer(user);

    private static bool HasModule(ClaimsPrincipal user, string module)
    {
        return user.Identity?.IsAuthenticated == true &&
               !user.IsInRole(IdentityRoles.Admin) && user.HasClaim(ModuleAccessCatalog.ClaimType, module);
    }
}
