using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public static class FrontOfficerAccessRules
    {
        public static bool IsFrontOfficer(UserProfileResponseDto user)
        {
            return user.Roles.Contains(IdentityRoles.Employee, StringComparer.OrdinalIgnoreCase) &&
                   string.Equals(user.Department, ContractConstants.FrontOfficerDepartment, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(user.Position, ContractConstants.FrontOfficerPosition, StringComparison.OrdinalIgnoreCase);
        }
    }
}
