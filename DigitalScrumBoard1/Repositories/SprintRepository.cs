using DigitalScrumBoard1.Utilities;
using DigitalScrumBoard1.Data;
using DigitalScrumBoard1.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalScrumBoard1.Repositories;

public sealed class SprintRepository : ISprintRepository
{
    private readonly DigitalScrumBoardContext _db;

    public SprintRepository(DigitalScrumBoardContext db)
    {
        _db = db;
    }

    public Task<bool> UserExistsAsync(int userId, CancellationToken ct)
    {
        return _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.UserID == userId && !u.Disabled, ct);
    }

    public Task<bool> TeamExistsAsync(int teamId, CancellationToken ct)
    {
        return _db.Teams
            .AsNoTracking()
            .AnyAsync(t => t.TeamID == teamId, ct);
    }

    public async Task<List<int>> GetActiveUserIdsForTeamAsync(int teamId, CancellationToken ct)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.TeamID == teamId && !u.Disabled)
            .Select(u => u.UserID)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<string?> GetTeamNameAsync(int teamId, CancellationToken ct)
    {
        return await _db.Teams
            .AsNoTracking()
            .Where(t => t.TeamID == teamId)
            .Select(t => t.TeamName)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<Sprint>> GetAllAsync(CancellationToken ct)
    {
        return await _db.Sprints
            .AsNoTracking()
            .OrderByDescending(s => s.SprintID)
            .ToListAsync(ct);
    }

    public async Task<Sprint?> GetByIdAsync(int sprintId, CancellationToken ct)
    {
        return await _db.Sprints
            .AsNoTracking()
            .Include(s => s.Manager)
            .Include(s => s.Team)
            .FirstOrDefaultAsync(s => s.SprintID == sprintId, ct);
    }

    public async Task<Sprint?> GetTrackedByIdAsync(int sprintId, CancellationToken ct)
    {
        return await _db.Sprints
            .Include(s => s.Manager)
            .Include(s => s.Team)
            .FirstOrDefaultAsync(s => s.SprintID == sprintId, ct);
    }

    public async Task<List<WorkItem>> GetTrackedSprintWorkItemsAsync(int sprintId, CancellationToken ct)
    {
        return await _db.WorkItems
            .Where(w => w.SprintID == sprintId && !w.IsDeleted)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Sprint sprint, CancellationToken ct)
    {
        await _db.Sprints.AddAsync(sprint, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }

    public async Task<int> DeleteSprintAndUnassignWorkItemsAsync(int sprintId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var sprint = await _db.Sprints
                .FirstOrDefaultAsync(s => s.SprintID == sprintId, ct);

            if (sprint is null)
                return -1;

            var now = DateTimeHelper.Now;

            var linkedWorkItems = await _db.WorkItems
                .IgnoreQueryFilters()
                .Where(w => w.SprintID == sprintId)
                .ToListAsync(ct);

            var returnedToBacklogCount = linkedWorkItems.Count(w => !w.IsDeleted && w.Status != "Completed");

            foreach (var workItem in linkedWorkItems)
            {
                workItem.SprintID = null;
                // Only return unfinished items to backlog as To-do; completed items keep their status
                if (!string.Equals(workItem.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    workItem.Status = "To-do";
                }
                workItem.UpdatedAt = now;
            }

            // Clear existing notification references to this sprint
            await _db.Notifications
                .Where(n => n.RelatedSprintID == sprintId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.RelatedSprintID, (int?)null), ct);

            _db.Sprints.Remove(sprint);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return returnedToBacklogCount;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task StartSprintAsync(int sprintId, CancellationToken ct)
    {
        var sprint = await _db.Sprints.FirstOrDefaultAsync(s => s.SprintID == sprintId, ct);
        if (sprint is null)
            return;

        sprint.Status = "Active";
        sprint.UpdatedAt = DateTimeHelper.Now;

        await _db.SaveChangesAsync(ct);
    }

    public async Task StopSprintAsync(Sprint sprint, CancellationToken ct)
    {
        sprint.Status = "Planned";
        sprint.UpdatedAt = DateTimeHelper.Now;

        await _db.SaveChangesAsync(ct);
    }

    public async Task CompleteSprintAsync(Sprint sprint, List<WorkItem> sprintWorkItems, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = DateTimeHelper.Now;

            foreach (var workItem in sprintWorkItems)
            {
                workItem.SprintID = null;
                // Only return unfinished items to backlog as To-do; completed items keep their status
                if (!string.Equals(workItem.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    workItem.Status = "To-do";
                }
                workItem.UpdatedAt = now;
            }

            // Clear notification references to this sprint to avoid FK constraint violation
            await _db.Notifications
                .Where(n => n.RelatedSprintID == sprint.SprintID)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.RelatedSprintID, (int?)null), ct);

            _db.Sprints.Remove(sprint);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public Task<bool> HasAnyWorkItemsAsync(int sprintId, CancellationToken ct)
    {
        return _db.WorkItems
            .AsNoTracking()
            .AnyAsync(w => w.SprintID == sprintId && !w.IsDeleted, ct);
    }

    public async Task<List<WorkItem>> GetSprintWorkItemsMissingAssigneeAsync(int sprintId, CancellationToken ct)
    {
        return await _db.WorkItems
            .AsNoTracking()
            .Where(w =>
                w.SprintID == sprintId &&
                !w.IsDeleted &&
                !w.AssignedUserID.HasValue)
            .OrderBy(w => w.WorkItemID)
            .ToListAsync(ct);
    }

    public async Task<List<int>> GetSprintAssignedUserIdsAsync(int sprintId, CancellationToken ct)
    {
        return await _db.WorkItems
            .AsNoTracking()
            .Where(w =>
                w.SprintID == sprintId &&
                !w.IsDeleted &&
                w.AssignedUserID.HasValue)
            .Select(w => w.AssignedUserID!.Value)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task AddNotificationsAsync(IEnumerable<Notification> notifications, CancellationToken ct)
    {
        var items = notifications.ToList();
        if (items.Count == 0)
            return;

        await _db.Notifications.AddRangeAsync(items, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateSprintAndAddNotificationsAsync(
        Sprint sprint,
        IEnumerable<Notification> notifications,
        CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var items = notifications.ToList();

            sprint.UpdatedAt = DateTimeHelper.Now;

            if (items.Count > 0)
                await _db.Notifications.AddRangeAsync(items, ct);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<(List<object> Items, int Total)> GetPagedAsync(
        string? status,
        int? teamId,
        int? managedBy,
        DateOnly? from,
        DateOnly? to,
        string? search,
        string? sortBy,
        string? sortDirection,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        var q = _db.Sprints.AsNoTracking().Include(s => s.Manager).Include(s => s.Team).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim();
            q = q.Where(s => s.Status == normalizedStatus);
        }

        if (teamId.HasValue && teamId.Value != Utilities.SystemDefaults.DefaultTeamId)
            q = q.Where(s => s.TeamID == teamId.Value);

        if (managedBy.HasValue)
            q = q.Where(s => s.ManagedBy == managedBy.Value);

        if (from.HasValue)
            q = q.Where(s => s.StartDate >= from.Value);

        if (to.HasValue)
            q = q.Where(s => s.EndDate <= to.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(t =>
                t.SprintName.ToLower().Contains(s) ||
                (t.Goal != null && t.Goal.ToLower().Contains(s)));
        }

        q = ApplySprintSorting(q, sortBy, sortDirection);

        var total = await q.CountAsync(ct);

        var sprints = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var sprintIds = sprints.Select(s => s.SprintID).ToList();
        
        var storyTypeId = await _db.WorkItemTypes
            .Where(wt => wt.TypeName == "Story")
            .Select(wt => wt.WorkItemTypeID)
            .FirstOrDefaultAsync(ct);
        
        var taskTypeId = await _db.WorkItemTypes
            .Where(wt => wt.TypeName == "Task")
            .Select(wt => wt.WorkItemTypeID)
            .FirstOrDefaultAsync(ct);

        var counts = await _db.WorkItems
            .AsNoTracking()
            .Where(w => w.SprintID.HasValue && sprintIds.Contains(w.SprintID.Value) && !w.IsDeleted)
            .GroupBy(w => w.SprintID)
            .Select(g => new
            {
                SprintID = g.Key!.Value,
                StoryCount = g.Count(w => w.WorkItemTypeID == storyTypeId),
                TaskCount = g.Count(w => w.WorkItemTypeID == taskTypeId)
            })
            .ToListAsync(ct);

        var countsLookup = counts.ToDictionary(c => c.SprintID, c => new { c.StoryCount, c.TaskCount });

        var items = sprints.Select(s => new
        {
            s.SprintID,
            s.SprintName,
            s.Goal,
            s.StartDate,
            s.EndDate,
            s.Status,
            s.ManagedBy,
            ManagedByName = FormatManagerDisplayName(s.Manager),
            s.TeamID,
            TeamName = s.Team?.TeamName,
            s.CreatedAt,
            s.UpdatedAt,
            StoryCount = countsLookup.TryGetValue(s.SprintID, out var cnt) ? cnt.StoryCount : 0,
            TaskCount = countsLookup.TryGetValue(s.SprintID, out var cnt2) ? cnt2.TaskCount : 0
        }).ToList();

        return (items.Cast<object>().ToList(), total);
    }

    private static string? FormatManagerDisplayName(User? u)
    {
        if (u is null) return null;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(u.FirstName)) parts.Add(u.FirstName.Trim());
        if (!string.IsNullOrWhiteSpace(u.MiddleName)) parts.Add(u.MiddleName.Trim());
        if (!string.IsNullOrWhiteSpace(u.LastName)) parts.Add(u.LastName.Trim());
        if (!string.IsNullOrWhiteSpace(u.NameExtension)) parts.Add(u.NameExtension.Trim());
        return parts.Count > 0 ? string.Join(' ', parts) : null;
    }

    private static IQueryable<Sprint> ApplySprintSorting(
        IQueryable<Sprint> q,
        string? sortBy,
        string? sortDirection)
    {
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return sortBy?.Trim() switch
        {
            "SprintName" => descending ? q.OrderByDescending(s => s.SprintName) : q.OrderBy(s => s.SprintName),
            "StartDate" => descending ? q.OrderByDescending(s => s.StartDate) : q.OrderBy(s => s.StartDate),
            "EndDate" => descending ? q.OrderByDescending(s => s.EndDate) : q.OrderBy(s => s.EndDate),
            "Status" => descending ? q.OrderByDescending(s => s.Status).ThenBy(s => s.SprintName) : q.OrderBy(s => s.Status).ThenBy(s => s.SprintName),
            "CreatedAt" => descending ? q.OrderByDescending(s => s.CreatedAt) : q.OrderBy(s => s.CreatedAt),
            "UpdatedAt" => descending ? q.OrderByDescending(s => s.UpdatedAt) : q.OrderBy(s => s.UpdatedAt),
            _ => q.OrderByDescending(s => s.SprintID)
        };
    }
}