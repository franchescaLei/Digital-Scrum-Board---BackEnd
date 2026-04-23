using DigitalScrumBoard1.Dtos.Lookups;
using DigitalScrumBoard1.Repositories;

namespace DigitalScrumBoard1.Services;

public sealed class LookupService : ILookupService
{
    private readonly ILookupRepository _repo;

    public LookupService(ILookupRepository repo)
    {
        _repo = repo;
    }

    public Task<List<TeamLookupDto>> SearchTeamsAsync(string? search, int? limit, CancellationToken ct)
        => _repo.SearchTeamsAsync(Norm(search), limit ?? 25, ct);

    public Task<List<UserLookupDto>> SearchUsersAsync(string? search, int? teamId, int? limit, CancellationToken ct)
        => _repo.SearchUsersAsync(Norm(search), teamId, limit ?? 25, ct);

    private static string? Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }
}