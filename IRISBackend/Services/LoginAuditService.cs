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

        public Task RecordAsync(string? userId, string emailOrUsername, bool succeeded, string? failureReason = null) =>
            RecordEventAsync(userId, emailOrUsername, succeeded ? "LoginSuccessful" : "LoginFailed", reason: failureReason);

        public async Task RecordEventAsync(string? userId, string identifier, string eventType, Guid? sessionId = null,
            string? actorUserId = null, string? reason = null, CancellationToken cancellationToken = default)
        {
            var context = _httpContextAccessor.HttpContext;
            await _loginAuditRepository.AddAsync(new UserLoginAudit
            {
                UserId = userId, EmailOrUsername = SafeText(identifier, 256) ?? "",
                EventType = eventType, SessionId = sessionId, ActorUserId = actorUserId,
                Succeeded = eventType != "LoginFailed", FailureReason = SafeText(reason, 200),
                IpAddress = context?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = SafeText(context?.Request.Headers.UserAgent.ToString(), 512),
                CreatedAtUtc = DateTime.UtcNow
            }, cancellationToken);
            await _loginAuditRepository.SaveChangesAsync(cancellationToken);
        }

        public static string? SafeText(string? value, int maxLength) => value is null ? null :
            new string(value.Where(c => !char.IsControl(c)).Take(maxLength).ToArray());
    }
}
