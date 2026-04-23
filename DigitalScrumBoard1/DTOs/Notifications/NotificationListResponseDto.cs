using System.Collections.Generic;

namespace DigitalScrumBoard1.DTOs.Notifications;

public sealed class NotificationListResponseDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int UnreadCount { get; set; }
    public List<NotificationListItemDto> Items { get; set; } = new();
}