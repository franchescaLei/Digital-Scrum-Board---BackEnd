using DigitalScrumBoard1.Dtos;

namespace DigitalScrumBoard1.Services
{
    public interface ITeamService
    {
        Task<object> CreateTeamAsync(CreateTeamRequestDto req, int actorUserId, string ipAddress, CancellationToken ct);

        Task<object> DisableTeamAsync(int teamId, int actorUserId, string ipAddress, CancellationToken ct);

        Task<object?> GetTeamByIdAsync(int id, CancellationToken ct);

        Task<object> ListTeamsAsync(
            string? search,
            bool? isActive,
            string? sortBy,
            string? sortDirection,
            int page,
            int pageSize,
            CancellationToken ct);
    }
}