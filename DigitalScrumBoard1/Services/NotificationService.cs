using DigitalScrumBoard1.Utilities;
using DigitalScrumBoard1.DTOs.Notifications;
using DigitalScrumBoard1.DTOs.SignalR;
using DigitalScrumBoard1.Hubs;
using DigitalScrumBoard1.Models;
using DigitalScrumBoard1.Repositories;
using Microsoft.AspNetCore.SignalR;

namespace DigitalScrumBoard1.Services;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repo;
    private readonly IHubContext<NotificationHub> _hub;

    public NotificationService(INotificationRepository repo, IHubContext<NotificationHub> hub)
    {
        _repo = repo;
        _hub = hub;
    }

    public async Task<NotificationListResponseDto> GetMyNotificationsAsync(
        int userId,
        bool? isRead,
        string? type,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var unreadCount = await _repo.GetUnreadCountAsync(userId, ct);
        var result = await _repo.GetForUserAsync(userId, isRead, type, page, pageSize, ct);

        return new NotificationListResponseDto
        {
            Page = page < 1 ? 1 : page,
            PageSize = pageSize < 1 ? 1 : Math.Min(pageSize, 100),
            Total = result.Total,
            UnreadCount = unreadCount,
            Items = result.Items
        };
    }

    public Task<int> GetMyUnreadCountAsync(int userId, CancellationToken ct)
        => _repo.GetUnreadCountAsync(userId, ct);

    public async Task<bool> MarkAsReadAsync(int userId, int notificationId, CancellationToken ct)
    {
        var row = await _repo.GetTrackedByIdForUserAsync(notificationId, userId, ct);
        if (row is null)
            return false;

        if (!row.IsRead)
        {
            row.IsRead = true;
            row.ReadAt = DateTimeHelper.Now;
            await _repo.SaveChangesAsync(ct);
            
            // Broadcast unread count update to user's other clients
            var newUnreadCount = await _repo.GetUnreadCountAsync(userId, ct);
            await _hub.Clients.Group($"user-{userId}").SendAsync("NotificationRead", new UnreadCountBroadcastDto
            {
                UserID = userId,
                UnreadCount = newUnreadCount,
                UpdatedAt = DateTimeHelper.Now
            }, ct);
        }

        return true;
    }

    public async Task<int> MarkAllAsReadAsync(int userId, CancellationToken ct)
    {
        var marked = await _repo.MarkAllAsReadAsync(userId, DateTimeHelper.Now, ct);
        
        if (marked > 0)
        {
            // Broadcast unread count update to user's other clients
            await _hub.Clients.Group($"user-{userId}").SendAsync("NotificationRead", new UnreadCountBroadcastDto
            {
                UserID = userId,
                UnreadCount = 0,
                UpdatedAt = DateTimeHelper.Now
            }, ct);
        }
        
        return marked;
    }

    public List<int> BuildRecipientList(int actorUserId, params int?[] candidateUserIds)
    {
        return candidateUserIds
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Where(x => x > 0 && x != actorUserId)
            .Distinct()
            .ToList();
    }

    public async Task AddNotificationsAsync(IEnumerable<Notification> notifications, CancellationToken ct)
    {
        var items = notifications.ToList();
        if (items.Count == 0)
            return;

        await _repo.AddRangeAsync(items, ct);
        await _repo.SaveChangesAsync(ct);
        
        // Push notifications to recipients in real-time
        var notificationsByUser = items.GroupBy(n => n.UserID);
        foreach (var userGroup in notificationsByUser)
        {
            foreach (var notification in userGroup)
            {
                await _hub.Clients.Group($"user-{notification.UserID}").SendAsync("NotificationReceived", new NotificationBroadcastDto
                {
                    NotificationID = notification.NotificationID,
                    NotificationType = notification.NotificationType,
                    Message = notification.Message,
                    RelatedWorkItemID = notification.RelatedWorkItemID,
                    RelatedSprintID = notification.RelatedSprintID,
                    IsRead = notification.IsRead,
                    CreatedAt = notification.CreatedAt
                }, ct);
            }
        }
    }
}