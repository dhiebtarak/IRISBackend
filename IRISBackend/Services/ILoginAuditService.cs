namespace IRISBackend.Services
{
    public interface ILoginAuditService
    {
        Task RecordAsync(string? userId, string emailOrUsername, bool succeeded, string? failureReason = null);
    }
}
