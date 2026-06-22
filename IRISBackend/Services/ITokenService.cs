using IRISBackend.Models;

namespace IRISBackend.Services
{
    public interface ITokenService
    {
        Task<string> CreateTokenAsync(ApplicationUser user);
    }
}
