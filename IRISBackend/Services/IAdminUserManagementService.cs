using IRISBackend.DTOs;
using System.Security.Claims;

namespace IRISBackend.Services;

public interface IAdminUserManagementService
{
    Task<ServiceResult<IReadOnlyList<AdminUserListItemDto>>> ListUsersForAdminAsync(string? search);
    Task<ServiceResult<IReadOnlyList<string>>> GetUserModulesForAdminAsync(string userId);
    Task<ServiceResult<IReadOnlyList<string>>> SetUserModulesForAdminAsync(string userId, IReadOnlyCollection<string>? modules);
    Task<ServiceResult<UserAccountStatusResponseDto>> SetUserEnabledForAdminAsync(string userId, bool isEnabled, ClaimsPrincipal actor);
}
