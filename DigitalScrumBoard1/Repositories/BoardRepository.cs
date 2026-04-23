using DigitalScrumBoard1.Data;
using DigitalScrumBoard1.DTOs;
using DigitalScrumBoard1.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalScrumBoard1.Repositories;

public class BoardRepository : IBoardRepository
{
    private readonly DigitalScrumBoardContext _db;

    public BoardRepository(DigitalScrumBoardContext db)
    {
        _db = db;
    }

    public async Task<WorkItem?> GetWorkItemAsync(int id, CancellationToken ct)
    {
        return await _db.WorkItems
            .Where(w => w.WorkItemID == id && !w.IsDeleted)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Sprint?> GetSprintAsync(int sprintId, CancellationToken ct)
    {
        return await _db.Sprints
            .AsNoTracking()
            .Include(s => s.Manager)
            .Include(s => s.Team)
            .FirstOrDefaultAsync(s => s.SprintID == sprintId, ct);
    }

    public async Task<List<WorkItem>> GetSprintWorkItemsAsync(int sprintId, CancellationToken ct)
    {
        return await _db.WorkItems
            .AsNoTracking()
            .Include(w => w.WorkItemType)
            .Include(w => w.AssignedUser)
            .Where(w => w.SprintID == sprintId && !w.IsDeleted && w.WorkItemType.TypeName == "Task")
            .ToListAsync(ct);
    }

    public async Task<List<WorkItem>> GetTrackedColumnWorkItemsAsync(
        int sprintId,
        string status,
        CancellationToken ct)
    {
        return await _db.WorkItems
            .Where(w =>
                w.SprintID == sprintId &&
                !w.IsDeleted &&
                w.Status == status &&
                w.WorkItemType.TypeName == "Task")
            .OrderBy(w => w.BoardOrder)
            .ThenBy(w => w.CreatedAt)
            .ThenBy(w => w.WorkItemID)
            .ToListAsync(ct);
    }

    public async Task<List<ActiveBoardDto>> GetActiveBoardsAsync(CancellationToken ct)
    {
        return await _db.Sprints
            .AsNoTracking()
            .Where(s => s.Status == "Active")
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SprintID)
            .Select(s => new ActiveBoardDto
            {
                SprintID = s.SprintID,
                SprintName = s.SprintName
            })
            .ToListAsync(ct);
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddHistoryAsync(WorkItemHistory history, CancellationToken ct)
    {
        await _db.WorkItemHistories.AddAsync(history, ct);
    }

    public async Task AddNotificationAsync(Notification notification, CancellationToken ct)
    {
        await _db.Notifications.AddAsync(notification, ct);
    }
}