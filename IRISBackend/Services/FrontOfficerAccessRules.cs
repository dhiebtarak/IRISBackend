using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public static class FrontOfficerAccessRules
    {
        public static bool IsFrontOfficer(UserProfileResponseDto user)
        {
            return user.Roles.Contains(IdentityRoles.Employee, StringComparer.OrdinalIgnoreCase) &&
                   EffectiveModules(user).Contains(ModuleAccessCatalog.Contracts, StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsFrontOrBackOfficer(UserProfileResponseDto user)
        {
            return user.Roles.Contains(IdentityRoles.Employee, StringComparer.OrdinalIgnoreCase) &&
                   (IsFrontOfficer(user) || IsBackOfficer(user));
        }

        public static bool IsBackOfficer(UserProfileResponseDto user)
        {
            return user.Roles.Contains(IdentityRoles.Employee, StringComparer.OrdinalIgnoreCase) &&
                   EffectiveModules(user).Contains(ModuleAccessCatalog.BackOffice, StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<string> EffectiveModules(UserProfileResponseDto user) =>
            user.ModuleAssignmentsConfigured
                ? user.Modules.ToArray()
                : ModuleAccessCatalog.LegacyModules(user.Department, user.Position);
    }
}
