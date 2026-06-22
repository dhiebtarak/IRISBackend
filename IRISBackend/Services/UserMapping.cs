using IRISBackend.DTOs;
using IRISBackend.Models;

namespace IRISBackend.Services
{
    internal static class UserMapping
    {
        public static UserProfileResponseDto ToProfile(ApplicationUser user, IEnumerable<string> roles)
        {
            return new UserProfileResponseDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Username = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                Department = user.Department,
                Position = user.Position,
                Roles = roles
            };
        }
    }
}
