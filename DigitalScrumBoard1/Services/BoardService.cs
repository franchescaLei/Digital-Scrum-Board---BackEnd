using DigitalScrumBoard1.Utilities;
using DigitalScrumBoard1.Data;
using DigitalScrumBoard1.DTOs;
using DigitalScrumBoard1.DTOs.SignalR;
using DigitalScrumBoard1.Hubs;
using DigitalScrumBoard1.Models;
using DigitalScrumBoard1.Repositories;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DigitalScrumBoard1.Services;

public class BoardService : IBoardService
{
    private readonly IBoardRepository _repo;
    private readonly IHubContext<BoardHub> _hub;
    private readonly DigitalScrumBoardContext _db;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;

    public BoardService(
        IBoardRepository repo,
        IHubContext<BoardHub> hub,
        DigitalScrumBoardContext db,
        IAuditService audit,
        INotificationService notifications)
    {
        _repo = repo;
        _hub = hub;
        _db = db;
        _audit = audit;
        _notifications = notifications;
    }

    public Task<List<ActiveBoardDto>> GetActiveBoardsAsync(CancellationToken ct)
    {
        return _repo.GetActiveBoardsAsync(ct);
    }

    public async Task<BoardResponseDto> GetBoardAsync(
        int sprintId,
        int? assigneeId,
        string? priority,
        string? status,
        string? workItemType,
        string? sortBy,
        string? sortDirection,
        CancellationToken ct)
    {
        if (sprintId <= 0)
            throw new ArgumentException("SprintID must be greater than 0.");

        var normalizedPriority = NormalizePriority(priority);
        if (priority is not null && normalizedPriority is null)
            throw new ArgumentException("Invalid priority. Allowed values: Low, Medium, High, Critical.");

        var normalizedStatus = NormalizeStatus(status);
        if (status is not null && normalizedStatus is null)
            throw new ArgumentException("Invalid status. Allowed values: To-do, Ongoing, For Checking, Completed.");

        var normalizedWorkItemType = NormalizeWorkItemType(workItemType);
        if (workItemType is not null && normalizedWorkItemType is null)
            throw new ArgumentException("Invalid workItemType. Allowed values: Epic, Story, Task.");

        var normalizedSortBy = NormalizeSortBy(sortBy);
        if (sortBy is not null && normalizedSortBy is null)
            throw new ArgumentException("Invalid sortBy. Allowed values: Priority, DueDate, CreatedDate, UpdatedDate, Title, Assignee.");

        var normalizedSortDirection = NormalizeSortDirection(sortDirection);
        if (sortDirection is not null && normalizedSortDirection is null)
            throw new ArgumentException("Invalid sortDirection. Allowed values: asc, desc.");

        var sprint = await _repo.GetSprintAsync(sprintId, ct);
        if (sprint is null)
            throw new KeyNotFoundException("Sprint not found.");

        if (!string.Equals(sprint.Status, "Active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only active sprint boards can be viewed.");

        var items = await _repo.GetSprintWorkItemsAsync(sprintId, ct);

        IEnumerable<WorkItem> filtered = items;

        if (assigneeId.HasValue)
            filtered = filtered.Where(w => w.AssignedUserID == assigneeId.Value);

        if (normalizedPriority is not null)
            filtered = filtered.Where(w => string.Equals(w.Priority, normalizedPriority, StringComparison.OrdinalIgnoreCase));

        if (normalizedStatus is not null)
            filtered = filtered.Where(w => string.Equals(w.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase));

        if (normalizedWorkItemType is not null)
            filtered = filtered.Where(w =>
                w.WorkItemType != null &&
                string.Equals(w.WorkItemType.TypeName, normalizedWorkItemType, StringComparison.OrdinalIgnoreCase));

        var filteredList = filtered.ToList();

        // Resolve sprint manager name from loaded navigation property
        string? sprintManagerName = null;
        if (sprint.Manager is not null)
            sprintManagerName = $"{sprint.Manager.FirstName} {sprint.Manager.LastName}".Trim();

        // Resolve sprint team info (TeamID=1 means "Unassigned")
        string? sprintTeamName = null;
        if (sprint.Team is not null)
            sprintTeamName = sprint.TeamID == 1 ? "Unassigned" : sprint.Team.TeamName;

        return new BoardResponseDto
        {
            SprintID = sprint.SprintID,
            SprintName = sprint.SprintName,
            SprintManagerId = sprint.ManagedBy,
            SprintManagerName = sprintManagerName,
            SprintTeamID = sprint.TeamID,
            SprintTeamName = sprintTeamName,
            Todo = BuildColumn(filteredList, "To-do", normalizedSortBy, normalizedSortDirection),
            Ongoing = BuildColumn(filteredList, "Ongoing", normalizedSortBy, normalizedSortDirection),
            ForChecking = BuildColumn(filteredList, "For Checking", normalizedSortBy, normalizedSortDirection),
            Completed = BuildColumn(filteredList, "Completed", normalizedSortBy, normalizedSortDirection)
        };
    }

    public async Task MoveWorkItemAsync(
        int workItemId,
        string newStatus,
        int userId,
        string role,
        int? userTeamId,
        string ipAddress,
        CancellationToken ct)
    {
        var item = await _repo.GetWorkItemAsync(workItemId, ct)
            ?? throw new KeyNotFoundException("Work item not found.");

        if (!item.SprintID.HasValue)
            throw new InvalidOperationException("Work item is not assigned to a sprint.");

        var normalizedStatus = NormalizeStatus(newStatus);
        if (normalizedStatus is null)
            throw new ArgumentException("Invalid status. Allowed values: To-do, Ongoing, For Checking, Completed.");

        var sprint = await _repo.GetSprintAsync(item.SprintID.Value, ct);
        if (sprint is null)
            throw new InvalidOperationException("Sprint not found.");

        // Authorization: Admin/Scrum Master, work item assignee, or Sprint Manager
        var allowed =
            string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "ScrumMaster", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Scrum Master", StringComparison.OrdinalIgnoreCase) ||
            item.AssignedUserID == userId ||
            (sprint.ManagedBy.HasValue && sprint.ManagedBy.Value == userId);

        if (!allowed)
        {
            var detail = $"role={role}; userId={userId}; userTeamId={userTeamId?.ToString() ?? "null"}; " +
                         $"itemAssignedTo={item.AssignedUserID?.ToString() ?? "null"}; " +
                         $"sprintManagedBy={sprint.ManagedBy?.ToString() ?? "null"}; " +
                         $"sprintTeamId={sprint.TeamID?.ToString() ?? "null"}";

            await _audit.LogAsync(
                userId,
                "WorkItem.MoveBoardStatus",
                "WorkItem",
                item.WorkItemID,
                false,
                $"Unauthorized move attempt for WorkItemID={item.WorkItemID}; RequestedStatus={normalizedStatus}; SprintID={item.SprintID}; Details: {detail}",
                string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress,
                ct
            );

            throw new UnauthorizedAccessException("You are not allowed to move this work item.");
        }

        if (!string.Equals(sprint.Status, "Active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sprint not active.");

        if (string.Equals(item.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase))
            return;

        if (!BoardWorkflowRules.IsValidTransition(item.Status, normalizedStatus))
            throw new ArgumentException($"Invalid status transition from '{item.Status}' to '{normalizedStatus}'.");

        var oldStatus = item.Status;
        var oldBoardOrder = item.BoardOrder;
        var now = DateTimeHelper.Now;

        var sourceColumnItems = await _repo.GetTrackedColumnWorkItemsAsync(
            item.SprintID.Value,
            oldStatus,
            ct);

        var destinationColumnItems = await _repo.GetTrackedColumnWorkItemsAsync(
            item.SprintID.Value,
            normalizedStatus,
            ct);

        var sourceOrdered = sourceColumnItems
            .OrderBy(w => w.BoardOrder)
            .ThenBy(w => w.CreatedAt)
            .ThenBy(w => w.WorkItemID)
            .Where(w => w.WorkItemID != item.WorkItemID)
            .ToList();

        var destinationOrdered = destinationColumnItems
            .OrderBy(w => w.BoardOrder)
            .ThenBy(w => w.CreatedAt)
            .ThenBy(w => w.WorkItemID)
            .Where(w => w.WorkItemID != item.WorkItemID)
            .ToList();

        item.Status = normalizedStatus;
        item.UpdatedAt = now;
        destinationOrdered.Add(item);

        var changedBoardOrders = new List<(WorkItem Item, int OldOrder, int NewOrder)>();

        NormalizeColumnBoardOrder(sourceOrdered, now, changedBoardOrders);
        NormalizeColumnBoardOrder(destinationOrdered, now, changedBoardOrders);

        Notification? assigneeStatusNotification = null;
        if (item.AssignedUserID.HasValue && item.AssignedUserID.Value != userId)
        {
            assigneeStatusNotification = new Notification
            {
                UserID = item.AssignedUserID.Value,
                Message = $"Work item '{item.Title}' was moved from {oldStatus} to {normalizedStatus}.",
                NotificationType = "StatusChanged",
                RelatedWorkItemID = item.WorkItemID,
                RelatedSprintID = item.SprintID,
                CreatedAt = now,
                IsRead = false
            };
        }

        // Notify the sprint manager of all board movements in their sprint (unless they did it)
        Notification? managerStatusNotification = null;
        if (sprint.ManagedBy.HasValue && sprint.ManagedBy.Value != userId)
        {
            // Avoid duplicate notification if sprint manager is also the assignee
            if (!item.AssignedUserID.HasValue || item.AssignedUserID.Value != sprint.ManagedBy.Value)
            {
                managerStatusNotification = new Notification
                {
                    UserID = sprint.ManagedBy.Value,
                    Message = $"Work item '{item.Title}' was moved from {oldStatus} to {normalizedStatus}.",
                    NotificationType = "StatusChanged",
                    RelatedWorkItemID = item.WorkItemID,
                    RelatedSprintID = item.SprintID,
                    CreatedAt = now,
                    IsRead = false
                };
            }
        }

        // TEAM NOTIFICATIONS (per user, deduplicated)
        var teamNotifications = new List<Notification>();

        if (sprint.TeamID.HasValue)
        {
            var teamUserIds = await _db.Users
                .AsNoTracking()
                .Where(u => u.TeamID == sprint.TeamID.Value && !u.Disabled)
                .Select(u => u.UserID)
                .ToListAsync(ct);

            foreach (var teamUserId in teamUserIds)
            {
                // ❗ Skip actor
                if (teamUserId == userId)
                    continue;

                // ❗ Dedup: skip if already receiving as assignee or manager
                if (item.AssignedUserID == teamUserId)
                    continue;

                if (sprint.ManagedBy.HasValue && sprint.ManagedBy.Value == teamUserId)
                    continue;

                teamNotifications.Add(new Notification
                {
                    UserID = teamUserId,
                    Message = $"Work item '{item.Title}' was moved from {oldStatus} to {normalizedStatus}.",
                    NotificationType = "StatusChanged",
                    RelatedWorkItemID = item.WorkItemID,
                    RelatedSprintID = item.SprintID,
                    CreatedAt = now,
                    IsRead = false
                });
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _repo.AddHistoryAsync(new WorkItemHistory
            {
                WorkItemID = item.WorkItemID,
                FieldChanged = "Status",
                OldValue = oldStatus,
                NewValue = normalizedStatus,
                ChangedAt = now,
                ChangedBy = userId
            }, ct);

            foreach (var changed in changedBoardOrders)
            {
                await _repo.AddHistoryAsync(new WorkItemHistory
                {
                    WorkItemID = changed.Item.WorkItemID,
                    FieldChanged = "BoardOrder",
                    OldValue = changed.OldOrder.ToString(),
                    NewValue = changed.NewOrder.ToString(),
                    ChangedAt = now,
                    ChangedBy = userId
                }, ct);
            }

            await _repo.SaveAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException("The board was updated by another user. Refresh and try again.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        var notificationsToSend = new List<Notification>();

        if (assigneeStatusNotification is not null)
            notificationsToSend.Add(assigneeStatusNotification);

        if (managerStatusNotification is not null)
            notificationsToSend.Add(managerStatusNotification);

        if (teamNotifications.Count > 0)
            notificationsToSend.AddRange(teamNotifications);

        if (notificationsToSend.Count > 0)
            await _notifications.AddNotificationsAsync(notificationsToSend, ct);

        await _audit.LogAsync(
            userId,
            "WorkItem.MoveBoardStatus",
            "WorkItem",
            item.WorkItemID,
            true,
            $"Moved WorkItemID={item.WorkItemID} from {oldStatus} to {normalizedStatus}; OldBoardOrder={oldBoardOrder}; NewBoardOrder={item.BoardOrder}; SprintID={item.SprintID}",
            string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress,
            ct
        );

        // Build complete work item broadcast data
        var assigneeName = item.AssignedUserID.HasValue
            ? await _db.Users.AsNoTracking().Where(u => u.UserID == item.AssignedUserID.Value)
                .Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct)
            : null;

        var workItemType = await _db.WorkItemTypes.AsNoTracking()
            .Where(t => t.WorkItemTypeID == item.WorkItemTypeID)
            .Select(t => t.TypeName)
            .FirstOrDefaultAsync(ct);

        await _hub.Clients
            .Group($"sprint-{item.SprintID.Value}")
            .SendAsync("WorkItemMoved", new WorkItemBroadcastDto
            {
                WorkItemID = item.WorkItemID,
                Title = item.Title,
                Description = item.Description,
                Status = normalizedStatus,
                Priority = item.Priority,
                DueDate = item.DueDate,
                AssignedUserID = item.AssignedUserID,
                AssignedUserName = assigneeName,
                WorkItemTypeID = item.WorkItemTypeID,
                WorkItemType = workItemType ?? string.Empty,
                ParentWorkItemID = item.ParentWorkItemID,
                TeamID = item.TeamID,
                SprintID = item.SprintID,
                BoardOrder = item.BoardOrder,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            }, ct);
    }

    public async Task ReorderWorkItemAsync(
        int workItemId,
        int newPosition,
        int userId,
        string role,
        int? userTeamId,
        string ipAddress,
        CancellationToken ct)
    {
        if (workItemId <= 0)
            throw new ArgumentException("WorkItemID must be greater than 0.");

        if (newPosition < 0)
            throw new ArgumentException("NewPosition must be 0 or greater.");

        var item = await _repo.GetWorkItemAsync(workItemId, ct)
            ?? throw new KeyNotFoundException("Work item not found.");

        if (!item.SprintID.HasValue)
            throw new InvalidOperationException("Work item is not assigned to a sprint.");

        var sprint = await _repo.GetSprintAsync(item.SprintID.Value, ct);
        if (sprint is null)
            throw new InvalidOperationException("Sprint not found.");

        // Authorization: Admin/Scrum Master, work item assignee, or Sprint Manager
        var allowed =
            string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "ScrumMaster", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Scrum Master", StringComparison.OrdinalIgnoreCase) ||
            item.AssignedUserID == userId ||
            (sprint.ManagedBy.HasValue && sprint.ManagedBy.Value == userId);

        if (!allowed)
        {
            await _audit.LogAsync(
                userId,
                "WorkItem.ReorderBoardPosition",
                "WorkItem",
                item.WorkItemID,
                false,
                $"Unauthorized reorder attempt for WorkItemID={item.WorkItemID}; RequestedPosition={newPosition}; SprintID={item.SprintID}",
                string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress,
                ct
            );

            throw new UnauthorizedAccessException("You are not allowed to reorder this work item.");
        }

        if (!string.Equals(sprint.Status, "Active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Sprint not active.");

        var columnItems = await _repo.GetTrackedColumnWorkItemsAsync(
            item.SprintID.Value,
            item.Status,
            ct);

        var orderedColumnItems = columnItems
            .OrderBy(w => w.BoardOrder)
            .ThenBy(w => w.CreatedAt)
            .ThenBy(w => w.WorkItemID)
            .ToList();

        var existingIndex = orderedColumnItems.FindIndex(w => w.WorkItemID == item.WorkItemID);
        if (existingIndex < 0)
            throw new InvalidOperationException("Work item is not part of its current board column.");

        if (newPosition >= orderedColumnItems.Count)
            throw new ArgumentException($"NewPosition must be between 0 and {orderedColumnItems.Count - 1} for this column.");

        if (existingIndex == newPosition)
            return;

        var now = DateTimeHelper.Now;

        orderedColumnItems.RemoveAt(existingIndex);
        orderedColumnItems.Insert(newPosition, item);

        var changedItems = new List<(WorkItem Item, int OldOrder, int NewOrder)>();
        NormalizeColumnBoardOrder(orderedColumnItems, now, changedItems);

        if (changedItems.Count == 0)
            return;

        Notification? assigneeReorderNotification = null;
        if (item.AssignedUserID.HasValue && item.AssignedUserID.Value != userId)
        {
            assigneeReorderNotification = new Notification
            {
                UserID = item.AssignedUserID.Value,
                Message = $"Work item '{item.Title}' was reordered on the board.",
                NotificationType = "WorkItemReordered",
                RelatedWorkItemID = item.WorkItemID,
                RelatedSprintID = item.SprintID,
                CreatedAt = now,
                IsRead = false
            };
        }

        Notification? managerReorderNotification = null;
        if (sprint.ManagedBy.HasValue && sprint.ManagedBy.Value != userId)
        {
            if (!item.AssignedUserID.HasValue || item.AssignedUserID.Value != sprint.ManagedBy.Value)
            {
                managerReorderNotification = new Notification
                {
                    UserID = sprint.ManagedBy.Value,
                    Message = $"Work item '{item.Title}' was reordered on the board.",
                    NotificationType = "WorkItemReordered",
                    RelatedWorkItemID = item.WorkItemID,
                    RelatedSprintID = item.SprintID,
                    CreatedAt = now,
                    IsRead = false
                };
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var changed in changedItems)
            {
                await _repo.AddHistoryAsync(new WorkItemHistory
                {
                    WorkItemID = changed.Item.WorkItemID,
                    FieldChanged = "BoardOrder",
                    OldValue = changed.OldOrder.ToString(),
                    NewValue = changed.NewOrder.ToString(),
                    ChangedAt = now,
                    ChangedBy = userId
                }, ct);
            }

            await _repo.SaveAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException("The board was updated by another user. Refresh and try again.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        var reorderNotifications = new List<Notification>();
        if (assigneeReorderNotification is not null)
            reorderNotifications.Add(assigneeReorderNotification);
        if (managerReorderNotification is not null)
            reorderNotifications.Add(managerReorderNotification);

        if (reorderNotifications.Count > 0)
            await _notifications.AddNotificationsAsync(reorderNotifications, ct);

        await _audit.LogAsync(
            userId,
            "WorkItem.ReorderBoardPosition",
            "WorkItem",
            item.WorkItemID,
            true,
            $"Reordered WorkItemID={item.WorkItemID} within Status={item.Status}; NewPosition={newPosition}; AffectedCount={changedItems.Count}; SprintID={item.SprintID}",
            string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress,
            ct
        );

        // Build complete work item broadcast data for reorder
        var assigneeName = item.AssignedUserID.HasValue
            ? await _db.Users.AsNoTracking().Where(u => u.UserID == item.AssignedUserID.Value)
                .Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct)
            : null;

        var workItemType = await _db.WorkItemTypes.AsNoTracking()
            .Where(t => t.WorkItemTypeID == item.WorkItemTypeID)
            .Select(t => t.TypeName)
            .FirstOrDefaultAsync(ct);

        // Broadcast full work item data so frontend can update without refetch
        await _hub.Clients
            .Group($"sprint-{item.SprintID.Value}")
            .SendAsync("WorkItemReordered", new
            {
                workItem = new WorkItemBroadcastDto
                {
                    WorkItemID = item.WorkItemID,
                    Title = item.Title,
                    Description = item.Description,
                    Status = item.Status,
                    Priority = item.Priority,
                    DueDate = item.DueDate,
                    AssignedUserID = item.AssignedUserID,
                    AssignedUserName = assigneeName,
                    WorkItemTypeID = item.WorkItemTypeID,
                    WorkItemType = workItemType ?? string.Empty,
                    ParentWorkItemID = item.ParentWorkItemID,
                    TeamID = item.TeamID,
                    SprintID = item.SprintID,
                    BoardOrder = newPosition,
                    CreatedAt = item.CreatedAt,
                    UpdatedAt = item.UpdatedAt
                },
                newPosition,
                sprintID = item.SprintID.Value
            }, ct);
    }

    public async Task<SprintMetricsDto> GetSprintMetricsAsync(
        int sprintId,
        CancellationToken ct)
    {
        var items = await _repo.GetSprintWorkItemsAsync(sprintId, ct);

        var total = items.Count;
        var completed = items.Count(i => i.Status == "Completed");
        var remaining = total - completed;

        return new SprintMetricsDto
        {
            TotalItems = total,
            CompletedItems = completed,
            RemainingItems = remaining,
            CompletionRate = total == 0 ? 0 : (double)completed / total * 100,
            TotalStoryPoints = 0,
            CompletedStoryPoints = 0
        };
    }

    private static void NormalizeColumnBoardOrder(
        List<WorkItem> items,
        DateTime now,
        List<(WorkItem Item, int OldOrder, int NewOrder)> changedItems)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var workItem = items[i];
            var oldOrder = workItem.BoardOrder;

            if (oldOrder != i)
            {
                workItem.BoardOrder = i;
                workItem.UpdatedAt = now;
                changedItems.Add((workItem, oldOrder, i));
            }
        }
    }

    private static List<WorkItemBoardDto> BuildColumn(
        List<WorkItem> items,
        string status,
        string? sortBy,
        string? sortDirection)
    {
        var columnItems = items
            .Where(w => string.Equals(w.Status, status, StringComparison.OrdinalIgnoreCase));

        var ordered = ApplyColumnSorting(columnItems, sortBy, sortDirection);

        return ordered
            .Select(MapBoardItem)
            .ToList();
    }

    private static IEnumerable<WorkItem> ApplyColumnSorting(
        IEnumerable<WorkItem> items,
        string? sortBy,
        string? sortDirection)
    {
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return items
                .OrderBy(w => w.BoardOrder)
                .ThenBy(w => w.CreatedAt);
        }

        return sortBy switch
        {
            "Priority" => descending
                ? items.OrderByDescending(w => GetPriorityRank(w.Priority)).ThenBy(w => w.BoardOrder).ThenBy(w => w.CreatedAt)
                : items.OrderBy(w => GetPriorityRank(w.Priority)).ThenBy(w => w.BoardOrder).ThenBy(w => w.CreatedAt),

            "DueDate" => descending
                ? items.OrderByDescending(w => w.DueDate.HasValue).ThenByDescending(w => w.DueDate).ThenBy(w => w.BoardOrder).ThenBy(w => w.CreatedAt)
                : items.OrderBy(w => w.DueDate.HasValue ? 0 : 1).ThenBy(w => w.DueDate).ThenBy(w => w.BoardOrder).ThenBy(w => w.CreatedAt),

            "CreatedDate" => descending
                ? items.OrderByDescending(w => w.CreatedAt).ThenBy(w => w.BoardOrder)
                : items.OrderBy(w => w.CreatedAt).ThenBy(w => w.BoardOrder),

            "UpdatedDate" => descending
                ? items.OrderByDescending(w => w.UpdatedAt).ThenBy(w => w.BoardOrder).ThenBy(w => w.CreatedAt)
                : items.OrderBy(w => w.UpdatedAt).ThenBy(w => w.BoardOrder).ThenBy(w => w.CreatedAt),

            "Title" => descending
                ? items.OrderByDescending(w => w.Title ?? string.Empty).ThenBy(w => w.BoardOrder).ThenBy(w => w.CreatedAt)
                : items.OrderBy(w => w.Title ?? string.Empty).ThenBy(w => w.BoardOrder).ThenBy(w => w.CreatedAt),

            "Assignee" => descending
                ? items.OrderByDescending(w => w.AssignedUserID.HasValue).ThenByDescending(w => w.AssignedUserID).ThenBy(w => w.BoardOrder).ThenBy(w => w.CreatedAt)
                : items.OrderBy(w => w.AssignedUserID.HasValue ? 0 : 1).ThenBy(w => w.AssignedUserID).ThenBy(w => w.BoardOrder).ThenBy(w => w.CreatedAt),

            _ => items.OrderBy(w => w.BoardOrder).ThenBy(w => w.CreatedAt)
        };
    }

    private static int GetPriorityRank(string? priority)
    {
        return priority?.Trim().ToLowerInvariant() switch
        {
            "low" => 1,
            "medium" => 2,
            "high" => 3,
            "critical" => 4,
            _ => 0
        };
    }

    private static WorkItemBoardDto MapBoardItem(WorkItem item)
    {
        return new WorkItemBoardDto
        {
            WorkItemID = item.WorkItemID,
            Title = item.Title ?? string.Empty,
            Status = item.Status ?? string.Empty,
            TypeName = item.WorkItemType?.TypeName,
            Priority = item.Priority,
            AssignedUserID = item.AssignedUserID,
            AssignedUserName = item.AssignedUser != null ? $"{item.AssignedUser.FirstName} {item.AssignedUser.LastName}".Trim() : null,
            TeamID = item.TeamID,
            CommentCount = 0,
            DueDate = item.DueDate
        };
    }

    private static string? NormalizePriority(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim().ToLowerInvariant() switch
        {
            "low" => "Low",
            "medium" => "Medium",
            "high" => "High",
            "critical" => "Critical",
            _ => null
        };
    }

    private static string? NormalizeStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim().ToLowerInvariant() switch
        {
            "to-do" => "To-do",
            "todo" => "To-do",
            "ongoing" => "Ongoing",
            "for checking" => "For Checking",
            "for-checking" => "For Checking",
            "completed" => "Completed",
            _ => null
        };
    }

    private static string? NormalizeWorkItemType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim().ToLowerInvariant() switch
        {
            "epic" => "Epic",
            "story" => "Story",
            "task" => "Task",
            _ => null
        };
    }

    private static string? NormalizeSortBy(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim().ToLowerInvariant() switch
        {
            "priority" => "Priority",
            "duedate" => "DueDate",
            "due-date" => "DueDate",
            "createddate" => "CreatedDate",
            "created-date" => "CreatedDate",
            "updateddate" => "UpdatedDate",
            "updated-date" => "UpdatedDate",
            "title" => "Title",
            "assignee" => "Assignee",
            _ => null
        };
    }

    private static string? NormalizeSortDirection(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim().ToLowerInvariant() switch
        {
            "asc" => "asc",
            "desc" => "desc",
            _ => null
        };
    }
}