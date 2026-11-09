namespace IRISBackend.Services
{
    public interface ILoginAuditService
    {
        Task RecordAsync(string? userId, string emailOrUsername, bool succeeded, string? failureReason = null);
        Task RecordEventAsync(string? userId, string identifier, string eventType, Guid? sessionId = null, string? actorUserId = null, string? reason = null, CancellationToken cancellationToken = default);
    }
}
