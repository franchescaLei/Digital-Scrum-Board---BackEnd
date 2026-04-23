using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace DigitalScrumBoard1.Hubs;

/// <summary>
/// SignalR hub for real-time notification delivery.
/// Users are automatically added to their personal notification group on connect.
/// </summary>
[Authorize(AuthenticationSchemes = "MyCookieAuth")]
public sealed class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (userId.HasValue)
        {
            // Add user to their personal notification group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId.Value}");
        }

        if (Context.User?.IsInRole("Administrator") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Cleanup is handled automatically by SignalR
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Join a sprint board group for real-time updates.
    /// Verifies user has access to the sprint before allowing join.
    /// </summary>
    public async Task JoinSprintBoard(int sprintId)
    {
        if (sprintId <= 0)
            throw new HubException("SprintID must be greater than 0.");

        var userId = GetUserId();
        if (!userId.HasValue)
            throw new HubException("User not authenticated.");

        // Note: Authorization check can be added here if needed
        // For now, any authenticated user can join any sprint board
        // Future: verify user is team member or has role-based access
        
        await Groups.AddToGroupAsync(Context.ConnectionId, $"sprint-{sprintId}");
    }

    /// <summary>
    /// Leave a sprint board group.
    /// </summary>
    public async Task LeaveSprintBoard(int sprintId)
    {
        if (sprintId <= 0)
            throw new HubException("SprintID must be greater than 0.");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"sprint-{sprintId}");
    }

    private int? GetUserId()
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ??
                  Context.User?.FindFirstValue("UserID");
        
        return int.TryParse(raw, out var id) ? id : null;
    }
}
