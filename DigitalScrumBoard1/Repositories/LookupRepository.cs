using DigitalScrumBoard1.Data;
using DigitalScrumBoard1.Dtos.Lookups;
using Microsoft.EntityFrameworkCore;

namespace DigitalScrumBoard1.Repositories;

public sealed class LookupRepository : ILookupRepository
{
    private readonly DigitalScrumBoardContext _db;

    public LookupRepository(DigitalScrumBoardContext db)
    {
        _db = db;
    }

    public async Task<List<TeamLookupDto>> SearchTeamsAsync(string? search, int limit, CancellationToken ct)
    {
        // Always cap to prevent abuse
        if (limit <= 0) limit = 25;
        if (limit > 100) limit = 100;

        var q = _db.Teams
            .AsNoTracking()
            .Where(t => t.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(t => t.TeamName.Contains(s));
        }

        return await q
            .OrderBy(t => t.TeamName)
            .Take(limit)
            .Select(t => new TeamLookupDto
            {
                TeamID = t.TeamID,
                TeamName = t.TeamName
            })
            .ToListAsync(ct);
    }

    public async Task<List<UserLookupDto>> SearchUsersAsync(string? search, int? teamId, int limit, CancellationToken ct)
    {
        if (limit <= 0) limit = 25;
        if (limit > 100) limit = 100;

        var q =
            from u in _db.Users.AsNoTracking()
            join t in _db.Teams.AsNoTracking() on u.TeamID equals t.TeamID into tj
            from t in tj.DefaultIfEmpty()
            where !u.Disabled
            select new { u, t };

        if (teamId.HasValue && teamId.Value != 1)
            q = q.Where(x => x.u.TeamID == teamId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                x.u.FirstName.Contains(s) ||
                x.u.LastName.Contains(s) ||
                x.u.EmailAddress.Contains(s));
        }

        return await q
            .OrderBy(x => x.u.LastName)
            .ThenBy(x => x.u.FirstName)
            .Take(limit)
            .Select(x => new UserLookupDto
            {
                UserID = x.u.UserID,
                DisplayName = ((x.u.FirstName + " " + x.u.LastName).Trim()),
                EmailAddress = x.u.EmailAddress,
                TeamID = x.u.TeamID,
                TeamName = x.t != null ? x.t.TeamName : null
            })
            .ToListAsync(ct);
    }
}