using DigitalScrumBoard1.DTOs.Notifications;
using DigitalScrumBoard1.Models;

namespace DigitalScrumBoard1.Repositories;

public interface INotificationRepository
{
    Task<(List<NotificationListItemDto> Items, int Total)> GetForUserAsync(
        int userId,
        bool? isRead,
        string? type,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<int> GetUnreadCountAsync(int userId, CancellationToken ct);

    Task<Notification?> GetTrackedByIdForUserAsync(int notificationId, int userId, CancellationToken ct);

    Task<int> MarkAllAsReadAsync(int userId, DateTime readAtUtc, CancellationToken ct);

    Task AddAsync(Notification notification, CancellationToken ct);

    Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}