using DigitalScrumBoard1.DTOs;

namespace DigitalScrumBoard1.Services
{
    public interface IBoardService
    {
        Task<List<ActiveBoardDto>> GetActiveBoardsAsync(CancellationToken ct);

        Task MoveWorkItemAsync(
            int workItemId,
            string newStatus,
            int userId,
            string role,
            int? userTeamId,
            string ipAddress,
            CancellationToken ct
        );

        Task ReorderWorkItemAsync(
            int workItemId,
            int newPosition,
            int userId,
            string role,
            int? userTeamId,
            string ipAddress,
            CancellationToken ct
        );

        Task<BoardResponseDto> GetBoardAsync(
            int sprintId,
            int? assigneeId,
            string? priority,
            string? status,
            string? workItemType,
            string? sortBy,
            string? sortDirection,
            CancellationToken ct
        );

        Task<SprintMetricsDto> GetSprintMetricsAsync(
            int sprintId,
            CancellationToken ct
        );
    }
}