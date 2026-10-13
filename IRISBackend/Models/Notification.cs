namespace IRISBackend.Models;

public partial class Notification
{
    public int IdNotification { get; set; }
    public string RecipientUserId { get; set; } = string.Empty;
    public string? RecipientRole { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public int? ClosingId { get; set; }
    public string? TargetRoute { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public string? CreatedBy { get; set; }
}
