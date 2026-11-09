namespace IRISBackend.DTOs;
public sealed class AccessActivityQuery
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public string? UserId { get; set; }
    public string? Role { get; set; }
    public string? Department { get; set; }
    public string? AccountStatus { get; set; }
    public string? SessionStatus { get; set; }
    public string? EventType { get; set; }
    public string? Result { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string SortBy { get; set; } = "loginAtUtc";
    public string SortDirection { get; set; } = "desc";
}
public sealed class RevokeSessionRequest { public string Reason { get; set; } = "Session revoked by Administrator"; }
public sealed record ActivityPage<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize);
public sealed class AccessActivityRow
{
    public Guid? SessionId { get; set; }
    public int? AuditEventId { get; set; }
    public string? UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string Department { get; set; } = "";
    public string AccountStatus { get; set; } = "";
    public DateTime LoginAtUtc { get; set; }
    public DateTime? LastActivityAtUtc { get; set; }
    public DateTime? LogoutAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public double? DurationSeconds { get; set; }
    public string? SessionStatus { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Browser { get; set; }
    public string? OperatingSystem { get; set; }
    public string? DeviceType { get; set; }
    public string? RevocationReason { get; set; }
    public string? RevokedByUserId { get; set; }
    public string Result { get; set; } = "Successful";
    public string EventType { get; set; } = "LoginSuccessful";
    public bool IsCurrentSession { get; set; }
}
public sealed record AccessActivitySummary(int ActiveSessions, int SuccessfulLoginsToday, int FailedLoginsLast24Hours,
    int LockedOrDisabledAccounts, int RevokedSessionsToday, double AverageSessionDurationSeconds);
public sealed record AccessAuditEvent(int Id, string EventType, DateTime CreatedAtUtc, string Identifier,
    string? Reason, string? IpAddress, string? UserAgent, Guid? SessionId, string? ActorUserId);
