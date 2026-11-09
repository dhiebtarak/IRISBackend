namespace IRISBackend.Models;

public sealed class AuthSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string SessionIdentifier { get; set; } = "";
    public DateTime LoginAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public DateTime? LogoutAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }
    public string Status { get; set; } = "Active";
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Browser { get; set; }
    public string? OperatingSystem { get; set; }
    public string? DeviceType { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public AspNetUser User { get; set; } = null!;
}
