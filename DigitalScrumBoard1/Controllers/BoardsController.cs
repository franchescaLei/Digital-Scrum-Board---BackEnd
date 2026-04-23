using DigitalScrumBoard1.DTOs;
using DigitalScrumBoard1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DigitalScrumBoard1.Controllers;

[ApiController]
[Route("api/boards")]
[Authorize(AuthenticationSchemes = "MyCookieAuth")]
public class BoardsController : ControllerBase
{
    private readonly IBoardService _service;

    public BoardsController(IBoardService service)
    {
        _service = service;
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActiveBoards(CancellationToken ct)
    {
        var boards = await _service.GetActiveBoardsAsync(ct);
        return Ok(boards);
    }

    [HttpGet("{sprintId:int}")]
    public async Task<IActionResult> GetBoard(
        [FromRoute] int sprintId,
        [FromQuery] int? assigneeId,
        [FromQuery] string? priority,
        [FromQuery] string? status,
        [FromQuery] string? workItemType,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken ct)
    {
        if (sprintId <= 0)
            return BadRequest(new { message = "SprintID must be greater than 0." });

        try
        {
            var board = await _service.GetBoardAsync(
                sprintId,
                assigneeId,
                priority,
                status,
                workItemType,
                sortBy,
                sortDirection,
                ct);

            return Ok(board);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPatch("workitems/{id:int}/move")]
    public async Task<IActionResult> MoveWorkItem(
        [FromRoute] int id,
        [FromBody] MoveWorkItemRequestDto req,
        CancellationToken ct)
    {
        if (id <= 0)
            return BadRequest(new { message = "WorkItemID must be greater than 0." });

        if (req is null || string.IsNullOrWhiteSpace(req.NewStatus))
            return BadRequest(new { message = "NewStatus is required." });

        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdRaw, out var userId))
            return Unauthorized(new { message = "Missing or invalid user identity." });

        var role = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrWhiteSpace(role))
            return Unauthorized(new { message = "Missing user role." });

        var teamClaim = User.FindFirst("TeamID");
        int? userTeamId = null;
        if (teamClaim is not null && int.TryParse(teamClaim.Value, out var tid))
            userTeamId = tid;

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        try
        {
            await _service.MoveWorkItemAsync(
                id,
                req.NewStatus,
                userId,
                role,
                userTeamId,
                ipAddress,
                ct);

            return Ok(new { message = "Work item moved successfully." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPatch("workitems/{id:int}/reorder")]
    public async Task<IActionResult> ReorderWorkItem(
        [FromRoute] int id,
        [FromBody] ReorderWorkItemDto req,
        CancellationToken ct)
    {
        if (id <= 0)
            return BadRequest(new { message = "WorkItemID must be greater than 0." });

        if (req is null)
            return BadRequest(new { message = "Request body is required." });

        if (req.NewPosition < 0)
            return BadRequest(new { message = "NewPosition must be 0 or greater." });

        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdRaw, out var userId))
            return Unauthorized(new { message = "Missing or invalid user identity." });

        var role = User.FindFirstValue(ClaimTypes.Role);
        if (string.IsNullOrWhiteSpace(role))
            return Unauthorized(new { message = "Missing user role." });

        var teamClaim = User.FindFirst("TeamID");
        int? userTeamId = null;
        if (teamClaim is not null && int.TryParse(teamClaim.Value, out var tid))
            userTeamId = tid;

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        try
        {
            await _service.ReorderWorkItemAsync(
                id,
                req.NewPosition,
                userId,
                role,
                userTeamId,
                ipAddress,
                ct);

            return Ok(new { message = "Work item reordered successfully." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}