using DigitalScrumBoard1.DTOs.WorkItems;
using DigitalScrumBoard1.Models;

namespace DigitalScrumBoard1.Repositories;

public interface IWorkItemRepository
{
    Task<int?> GetWorkItemTypeIdByNameAsync(string typeName, CancellationToken ct);

    Task<(int WorkItemID, int WorkItemTypeID, bool IsDeleted, DateOnly? DueDate)?> GetWorkItemTypeInfoByIdAsync(
        int id,
        CancellationToken ct);

    Task<WorkItem?> GetByIdAsync(int id, CancellationToken ct);
    Task<WorkItem?> GetTrackedByIdAsync(int id, CancellationToken ct);

    Task AddAsync(WorkItem item, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);

    Task AddWithAuditAsync(WorkItem item, AuditLog audit, CancellationToken ct);

    Task<List<(int WorkItemID, string Title, string TypeName, DateOnly? DueDate)>> ListParentsAsync(int[] allowedTypeIds, CancellationToken ct);

    Task<List<EpicTileDto>> GetEpicTilesAsync(CancellationToken ct);

    Task<WorkItemDetailsResponseDto?> GetWorkItemDetailsAsync(int workItemId, CancellationToken ct);

    Task<AgendasResponseDto> GetAgendasAsync(CancellationToken ct);

    Task<Sprint?> GetSprintByIdAsync(int sprintId, CancellationToken ct);

    Task<int?> GetSprintManagerUserIdAsync(int sprintId, CancellationToken ct);

    Task AssignToSprintAsync(WorkItem workItem, int sprintId, CancellationToken ct);

    Task RemoveFromSprintAsync(WorkItem workItem, CancellationToken ct);

    Task<bool> UserExistsAsync(int userId, CancellationToken ct);

    Task<bool> TeamExistsAsync(int teamId, CancellationToken ct);

    Task<bool> HasActiveChildrenAsync(int workItemId, CancellationToken ct);
    Task<List<int>> GetActiveDescendantIdsAsync(int workItemId, CancellationToken ct);

    Task AddHistoryAsync(WorkItemHistory history, CancellationToken ct);

    Task AddNotificationsAsync(IEnumerable<Notification> notifications, CancellationToken ct);

    Task<List<WorkItemCommentDto>> GetCommentsAsync(int workItemId, CancellationToken ct);
    Task<WorkItemComment?> GetCommentByIdAsync(int commentId, CancellationToken ct);
    Task<List<int>> GetUsersByTeamIdAsync(int teamId, CancellationToken ct);

    /// <summary>Active (non-disabled) users assigned to the team.</summary>
    Task<List<int>> GetActiveUserIdsForTeamAsync(int teamId, CancellationToken ct);

    Task AddCommentAsync(WorkItemComment comment, CancellationToken ct);

    Task<List<EpicTileDto>> GetEpicTilesFilteredAsync(
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken ct);

    Task<List<WorkItemDto>> GetWorkItemsByParentIdAsync(int parentId, string typeName, CancellationToken ct);

    Task<List<AgendaWorkItemDto>> GetBacklogItemsAsync(CancellationToken ct);

    Task<AgendasResponseDto> GetAgendasFilteredAsync(
        string? status,
        string? priority,
        string? workItemType,
        int? teamId,
        int? assigneeId,
        string? sortBy,
        string? sortDirection,
        CancellationToken ct);
    
    Task<List<WorkItem>> GetWorkItemsBySprintIdAsync(int sprintId, CancellationToken ct);

    Task<List<WorkItem>> GetChildTasksByParentIdAsync(int parentId, CancellationToken ct);

    /// <summary>
    /// Gets the derived status for a Story based on its child Tasks.
    /// Returns null if not a Story or if no child Tasks exist.
    /// </summary>
    Task<string?> GetDerivedStoryStatusAsync(int storyId, CancellationToken ct);

    Task<WorkItemHierarchyDto?> GetEpicHierarchyAsync(int epicId, CancellationToken ct);
}