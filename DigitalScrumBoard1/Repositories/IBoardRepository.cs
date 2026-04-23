using DigitalScrumBoard1.DTOs;
using DigitalScrumBoard1.Models;

namespace DigitalScrumBoard1.Repositories;

public interface IBoardRepository
{
    Task<WorkItem?> GetWorkItemAsync(int id, CancellationToken ct);

    Task<Sprint?> GetSprintAsync(int sprintId, CancellationToken ct);

    Task<List<WorkItem>> GetSprintWorkItemsAsync(int sprintId, CancellationToken ct);

    Task<List<WorkItem>> GetTrackedColumnWorkItemsAsync(
        int sprintId,
        string status,
        CancellationToken ct);

    Task<List<ActiveBoardDto>> GetActiveBoardsAsync(CancellationToken ct);

    Task SaveAsync(CancellationToken ct);

    Task AddHistoryAsync(WorkItemHistory history, CancellationToken ct);

    Task AddNotificationAsync(Notification notification, CancellationToken ct);
}