using DigitalScrumBoard1.Dtos.Lookups;
using DigitalScrumBoard1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalScrumBoard1.Controllers;

[ApiController]
[Route("api/lookups")]
[Authorize]
public sealed class LookupsController : ControllerBase
{
    private readonly ILookupService _svc;

    public LookupsController(ILookupService svc)
    {
        _svc = svc;
    }

    // GET /api/lookups/teams?search=dev&limit=25
    [HttpGet("teams")]
    public async Task<ActionResult<List<TeamLookupDto>>> SearchTeams(
        [FromQuery] string? search,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var rows = await _svc.SearchTeamsAsync(search, limit, ct);
        return Ok(rows);
    }

    // GET /api/lookups/users?search=lei&teamId=2&limit=25
    [HttpGet("users")]
    public async Task<ActionResult<List<UserLookupDto>>> SearchUsers(
        [FromQuery] string? search,
        [FromQuery] int? teamId,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var rows = await _svc.SearchUsersAsync(search, teamId, limit, ct);
        return Ok(rows);
    }
}