using IRISBackend.Data;
using IRISBackend.Models;

namespace IRISBackend.Services
{
    public class LoginAuditService : ILoginAuditService
    {
        private readonly AppDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public LoginAuditService(AppDbContext dbContext, IHttpContextAccessor httpContextAccessor)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task RecordAsync(string? userId, string emailOrUsername, bool succeeded, string? failureReason = null)
        {
            var httpContext = _httpContextAccessor.HttpContext;

            _dbContext.UserLoginAudits.Add(new UserLoginAudit
            {
                UserId = userId,
                EmailOrUsername = emailOrUsername,
                Succeeded = succeeded,
                FailureReason = failureReason,
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString(),
                CreatedAtUtc = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();
        }
    }
}
