using DigitalScrumBoard1.DTOs.Notifications;
using DigitalScrumBoard1.Models;

namespace DigitalScrumBoard1.Services;

public interface INotificationService
{
    Task<NotificationListResponseDto> GetMyNotificationsAsync(
        int userId,
        bool? isRead,
        string? type,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<int> GetMyUnreadCountAsync(int userId, CancellationToken ct);

    Task<bool> MarkAsReadAsync(int userId, int notificationId, CancellationToken ct);

    Task<int> MarkAllAsReadAsync(int userId, CancellationToken ct);

    List<int> BuildRecipientList(int actorUserId, params int?[] candidateUserIds);

    Task AddNotificationsAsync(IEnumerable<Notification> notifications, CancellationToken ct);
}