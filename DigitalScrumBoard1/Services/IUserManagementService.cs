using DigitalScrumBoard1.Dtos;
using DigitalScrumBoard1.DTOs.Authentication;

namespace DigitalScrumBoard1.Services
{
    public interface IUserManagementService
    {
        Task<object> ListUsersAsync(
            int? teamId,
            int? roleId,
            bool? disabled,
            bool? locked,
            bool? emailVerified,
            string? search,
            string? sortBy,
            string? sortDirection,
            int page,
            int pageSize,
            CancellationToken ct);

        Task<object?> GetUserByIdAsync(int id, CancellationToken ct);

        Task<List<object>> GetRolesAsync(CancellationToken ct);

        Task<object> CreateUserAsync(CreateUserRequestDto req, int actorUserId, string ipAddress, CancellationToken ct);

        Task<string> DisableUserAsync(int id, int actorUserId, string ipAddress, CancellationToken ct);

        Task<string> EnableUserAsync(int id, int actorUserId, string ipAddress, CancellationToken ct);

        Task<object> UpdateAccessAsync(int id, UpdateUserAccessDto req, int actorUserId, string ipAddress, CancellationToken ct);

        Task<string> ResetUserPasswordAsync(int id, int actorUserId, string ipAddress, CancellationToken ct);

        Task<string> ForceAccountLockoutAsync(int id, int actorUserId, string ipAddress, CancellationToken ct);
    }
}