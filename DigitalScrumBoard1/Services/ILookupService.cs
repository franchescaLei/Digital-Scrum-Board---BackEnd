using DigitalScrumBoard1.Dtos.Lookups;

namespace DigitalScrumBoard1.Services;

public interface ILookupService
{
    Task<List<TeamLookupDto>> SearchTeamsAsync(string? search, int? limit, CancellationToken ct);

    Task<List<UserLookupDto>> SearchUsersAsync(string? search, int? teamId, int? limit, CancellationToken ct);
}