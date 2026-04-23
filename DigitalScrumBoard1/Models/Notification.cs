namespace DigitalScrumBoard1.Models;

public class Notification
{
    public int NotificationID { get; set; }

    public int UserID { get; set; }
    public User User { get; set; } = null!;

    public int? RelatedWorkItemID { get; set; }
    public WorkItem? RelatedWorkItem { get; set; }

    public int? RelatedSprintID { get; set; }
    public Sprint? RelatedSprint { get; set; }

    public string NotificationType { get; set; } = null!;
    public string Message { get; set; } = null!;

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; }
}