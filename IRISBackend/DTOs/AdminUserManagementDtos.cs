namespace IRISBackend.DTOs;

public sealed class AdminUserListItemDto
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = [];
    public IReadOnlyList<string> Modules { get; set; } = [];
    public DateTime? LastSuccessfulLoginAtUtc { get; set; }
    public int ActiveSessionCount { get; set; }
    public bool IsLocked { get; set; }
    public string AccountStatus { get; set; } = "Enabled";
    public bool IsEnabled { get; set; }
}

public sealed class UpdateUserModulesRequestDto
{
    public List<string>? Modules { get; set; }
}

public sealed class UpdateUserAccountStatusRequestDto
{
    public bool IsEnabled { get; set; }
}

public sealed class UserAccountStatusResponseDto
{
    public string UserId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}
