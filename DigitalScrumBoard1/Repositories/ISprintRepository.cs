using DigitalScrumBoard1.Models;

namespace DigitalScrumBoard1.Repositories;

public interface ISprintRepository
{
    Task<bool> UserExistsAsync(int userId, CancellationToken ct);
    Task<bool> TeamExistsAsync(int teamId, CancellationToken ct);

    /// <summary>Active (non-disabled) users assigned to the team.</summary>
    Task<List<int>> GetActiveUserIdsForTeamAsync(int teamId, CancellationToken ct);

    Task<string?> GetTeamNameAsync(int teamId, CancellationToken ct);

    Task<List<Sprint>> GetAllAsync(CancellationToken ct);
    Task<Sprint?> GetByIdAsync(int sprintId, CancellationToken ct);
    Task<Sprint?> GetTrackedByIdAsync(int sprintId, CancellationToken ct);
    Task<List<WorkItem>> GetTrackedSprintWorkItemsAsync(int sprintId, CancellationToken ct);

    Task AddAsync(Sprint sprint, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);

    Task<int> DeleteSprintAndUnassignWorkItemsAsync(int sprintId, CancellationToken ct);

    Task StartSprintAsync(int sprintId, CancellationToken ct);
    Task StopSprintAsync(Sprint sprint, CancellationToken ct);
    Task CompleteSprintAsync(Sprint sprint, List<WorkItem> sprintWorkItems, CancellationToken ct);

    Task<bool> HasAnyWorkItemsAsync(int sprintId, CancellationToken ct);
    Task<List<WorkItem>> GetSprintWorkItemsMissingAssigneeAsync(int sprintId, CancellationToken ct);
    Task<List<int>> GetSprintAssignedUserIdsAsync(int sprintId, CancellationToken ct);

    Task AddNotificationsAsync(IEnumerable<Notification> notifications, CancellationToken ct);

    Task UpdateSprintAndAddNotificationsAsync(
        Sprint sprint,
        IEnumerable<Notification> notifications,
        CancellationToken ct);

    Task<(List<object> Items, int Total)> GetPagedAsync(
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
        CancellationToken ct);
}