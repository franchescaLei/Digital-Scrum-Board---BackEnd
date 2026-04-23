using DigitalScrumBoard1.Data;
using DigitalScrumBoard1.DTOs.Notifications;
using DigitalScrumBoard1.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalScrumBoard1.Repositories;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly DigitalScrumBoardContext _db;

    public NotificationRepository(DigitalScrumBoardContext db)
    {
        _db = db;
    }

    public async Task<(List<NotificationListItemDto> Items, int Total)> GetForUserAsync(
        int userId,
        bool? isRead,
        string? type,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var q = _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserID == userId);

        if (isRead.HasValue)
            q = q.Where(n => n.IsRead == isRead.Value);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var normalizedType = type.Trim();
            q = q.Where(n => n.NotificationType == normalizedType);
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.NotificationID)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationListItemDto
            {
                NotificationID = n.NotificationID,
                NotificationType = n.NotificationType,
                Message = n.Message,
                RelatedWorkItemID = n.RelatedWorkItemID,
                RelatedSprintID = n.RelatedSprintID,
                IsRead = n.IsRead,
                ReadAt = n.ReadAt,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<int> GetUnreadCountAsync(int userId, CancellationToken ct)
    {
        return _db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserID == userId && !n.IsRead, ct);
    }

    public Task<Notification?> GetTrackedByIdForUserAsync(int notificationId, int userId, CancellationToken ct)
    {
        return _db.Notifications
            .FirstOrDefaultAsync(n => n.NotificationID == notificationId && n.UserID == userId, ct);
    }

    public async Task<int> MarkAllAsReadAsync(int userId, DateTime readAtUtc, CancellationToken ct)
    {
        var rows = await _db.Notifications
            .Where(n => n.UserID == userId && !n.IsRead)
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            row.IsRead = true;
            row.ReadAt = readAtUtc;
        }

        await _db.SaveChangesAsync(ct);
        return rows.Count;
    }

    public async Task AddAsync(Notification notification, CancellationToken ct)
    {
        await _db.Notifications.AddAsync(notification, ct);
    }

    public async Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct)
    {
        var items = notifications.ToList();
        if (items.Count == 0)
            return;

        await _db.Notifications.AddRangeAsync(items, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}