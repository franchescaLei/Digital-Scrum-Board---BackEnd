using DigitalScrumBoard1.Dtos;
using DigitalScrumBoard1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DigitalScrumBoard1.Controllers
{
    [ApiController]
    [Route("api/teams")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth", Roles = "Administrator")]
    public sealed class TeamsController : ControllerBase
    {
        private readonly ITeamService _teams;

        public TeamsController(ITeamService teams)
        {
            _teams = teams;
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? search,
            [FromQuery] bool? isActive,
            [FromQuery] string? sortBy,
            [FromQuery] string? sortDirection,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            var result = await _teams.ListTeamsAsync(
                search,
                isActive,
                sortBy,
                sortDirection,
                page,
                pageSize,
                ct);

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateTeamRequestDto req, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var actorId = GetActorUserId() ?? 0;
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                var created = await _teams.CreateTeamAsync(req, actorId, ip, ct);
                var teamId = (int)created.GetType().GetProperty("TeamID")!.GetValue(created)!;
                return CreatedAtAction(nameof(GetById), new { id = teamId }, created);
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                    return Conflict(new { message = ex.Message });

                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken ct)
        {
            var team = await _teams.GetTeamByIdAsync(id, ct);
            return team is null ? NotFound(new { message = "Team not found." }) : Ok(team);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken ct)
        {
            var actorId = GetActorUserId() ?? 0;
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                var result = await _teams.DisableTeamAsync(id, actorId, ip, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    return NotFound(new { message = ex.Message });

                return BadRequest(new { message = ex.Message });
            }
        }

        private int? GetActorUserId()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(id, out var parsed) ? parsed : null;
        }
    }
}