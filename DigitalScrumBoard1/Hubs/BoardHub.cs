using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace DigitalScrumBoard1.Hubs;

/// <summary>
/// SignalR hub for real-time sprint board updates.
/// Clients join sprint-specific groups to receive board updates.
/// </summary>
[Authorize(AuthenticationSchemes = "MyCookieAuth")]
public sealed class BoardHub : Hub
{
    /// <summary>
    /// Join a sprint board group for real-time updates.
    /// </summary>
    public async Task JoinSprintBoard(int sprintId)
    {
        if (sprintId <= 0)
            throw new HubException("SprintID must be greater than 0.");

        var userId = GetUserId();
        if (!userId.HasValue)
            throw new HubException("User not authenticated.");

        // Note: Basic access - any authenticated user can join
        // Future enhancement: verify user has access to this sprint
        // (is team member, scrum master, or administrator)
        
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