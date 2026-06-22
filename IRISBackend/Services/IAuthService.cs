using IRISBackend.DTOs;

namespace IRISBackend.Services
{
    public interface IAuthService
    {
        Task<ServiceResult<AuthResponseDto>> RegisterAsync(RegisterRequestDto request);

        Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginRequestDto request);

        Task LogoutAsync();
    }
}
