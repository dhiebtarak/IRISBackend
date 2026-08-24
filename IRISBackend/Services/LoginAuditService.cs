using IRISBackend.Models;
using IRISBackend.Repositories;

namespace IRISBackend.Services
{
    public class LoginAuditService : ILoginAuditService
    {
        private readonly ILoginAuditRepository _loginAuditRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public LoginAuditService(
            ILoginAuditRepository loginAuditRepository,
            IHttpContextAccessor httpContextAccessor)
        {
            _loginAuditRepository = loginAuditRepository;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task RecordAsync(string? userId, string emailOrUsername, bool succeeded, string? failureReason = null)
        {
            var httpContext = _httpContextAccessor.HttpContext;

            await _loginAuditRepository.AddAsync(new UserLoginAudit
            {
                UserId = userId,
                EmailOrUsername = emailOrUsername,
                Succeeded = succeeded,
                FailureReason = failureReason,
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString(),
                CreatedAtUtc = DateTime.UtcNow
            });

            await _loginAuditRepository.SaveChangesAsync();
        }
    }
}
