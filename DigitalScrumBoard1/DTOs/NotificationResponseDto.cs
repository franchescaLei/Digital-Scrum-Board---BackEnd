namespace DigitalScrumBoard1.DTOs;

public class NotificationResponseDto
{
    public int NotificationID { get; set; }

    public string Message { get; set; } = "";

    public string NotificationType { get; set; } = "";

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }
}