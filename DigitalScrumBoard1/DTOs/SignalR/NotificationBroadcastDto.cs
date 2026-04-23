namespace DigitalScrumBoard1.DTOs.SignalR;

/// <summary>
/// Notification data for real-time push to clients.
/// </summary>
public sealed class NotificationBroadcastDto
{
    public int NotificationID { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int? RelatedWorkItemID { get; set; }
    public int? RelatedSprintID { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Unread notification count update for real-time badge updates.
/// </summary>
public sealed class UnreadCountBroadcastDto
{
    public int UserID { get; set; }
    public int UnreadCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}
