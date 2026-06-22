using IRISBackend.DTOs;
using IRISBackend.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace IRISBackend.Services
{
    public class UserService : IUserService
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public UserService(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<ServiceResult<UserProfileResponseDto>> GetCurrentUserAsync(ClaimsPrincipal principal)
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "User identity was not found in the token.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return ServiceResult<UserProfileResponseDto>.Failure(
                    "User was not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var roles = await _userManager.GetRolesAsync(user);
            return ServiceResult<UserProfileResponseDto>.Success(UserMapping.ToProfile(user, roles));
        }
    }
}
