using IRISBackend.DTOs;
using System.Security.Claims;

namespace IRISBackend.Services
{
    public class WorkspaceAccessService : IWorkspaceAccessService
    {
        private readonly IUserService _userService;

        public WorkspaceAccessService(IUserService userService)
        {
            _userService = userService;
        }

        public async Task<ServiceResult<WorkspaceAccessResponseDto>> GetCurrentWorkspaceAsync(ClaimsPrincipal principal)
        {
            var userResult = await _userService.GetCurrentUserAsync(principal);
            if (!userResult.Succeeded || userResult.Value is null)
            {
                return ServiceResult<WorkspaceAccessResponseDto>.Failure(
                    userResult.Error?.Message ?? "Unable to resolve workspace access.",
                    userResult.Error?.Errors,
                    userResult.StatusCode);
            }

            var user = userResult.Value;
            var workspaceKey = NormalizeKey($"{user.Department}-{user.Position}");

            return ServiceResult<WorkspaceAccessResponseDto>.Success(new WorkspaceAccessResponseDto
            {
                WorkspaceKey = workspaceKey,
                DisplayName = $"{user.Department} / {user.Position}",
                Department = user.Department,
                Position = user.Position,
                Roles = user.Roles,
                Permissions = ["read"]
            });
        }

        private static string NormalizeKey(string value)
        {
            var characters = value
                .Trim()
                .ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray();

            return string.Join('-', new string(characters).Split('-', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
