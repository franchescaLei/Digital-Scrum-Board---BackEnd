namespace DigitalScrumBoard1.DTOs.Notifications;

public sealed class NotificationListItemDto
{
    public int NotificationID { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public int? RelatedWorkItemID { get; set; }
    public int? RelatedSprintID { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}