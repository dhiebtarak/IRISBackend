using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public interface IAuthService
    {
        Task<ServiceResult<UserProfileResponseDto>> CreateUserAsync(RegisterRequestDto request);

        Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginRequestDto request);

        Task LogoutAsync();
    }
}
