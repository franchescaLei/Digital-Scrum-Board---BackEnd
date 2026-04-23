using DigitalScrumBoard1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DigitalScrumBoard1.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(AuthenticationSchemes = "MyCookieAuth")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications)
    {
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<IActionResult> GetMine(
        [FromQuery] bool? isRead,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        var result = await _notifications.GetMyNotificationsAsync(
            userId.Value,
            isRead,
            type,
            page,
            pageSize,
            ct);

        return Ok(result);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        var count = await _notifications.GetMyUnreadCountAsync(userId.Value, ct);
        return Ok(new { unreadCount = count });
    }

    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkAsRead([FromRoute] int id, CancellationToken ct)
    {
        if (id <= 0)
            return BadRequest(new { message = "NotificationID must be greater than 0." });

        var userId = GetUserId();
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        var found = await _notifications.MarkAsReadAsync(userId.Value, id, ct);
        if (!found)
            return NotFound(new { message = "Notification not found." });

        return Ok(new { message = "Notification marked as read." });
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        var marked = await _notifications.MarkAllAsReadAsync(userId.Value, ct);
        return Ok(new
        {
            message = "Notifications marked as read.",
            markedCount = marked
        });
    }

    private int? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }
}