using IRISBackend.DTOs;
using System.Security.Claims;

namespace IRISBackend.Services
{
    public interface IUserService
    {
        Task<ServiceResult<UserProfileResponseDto>> GetCurrentUserAsync(ClaimsPrincipal principal);
    }
}
