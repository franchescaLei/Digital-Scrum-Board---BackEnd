using DigitalScrumBoard1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DigitalScrumBoard1.Controllers
{
    [ApiController]
    [Route("api/audit-logs")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth", Roles = "Administrator")]
    public sealed class AuditLogsController : ControllerBase
    {
        private readonly IAuditLogService _auditLogs;

        public AuditLogsController(IAuditLogService auditLogs)
        {
            _auditLogs = auditLogs;
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken ct = default)
        {
            var item = await _auditLogs.GetByIdAsync(id, ct);
            return item is null
                ? NotFound(new { message = "Audit log not found." })
                : Ok(item);
        }

        [HttpGet]
        public async Task<IActionResult> Get(
            [FromQuery] int? userId,
            [FromQuery] string? action,
            [FromQuery] bool? success,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? targetType,
            [FromQuery] int? targetId,
            [FromQuery] string? ipAddress,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            var result = await _auditLogs.GetPagedAsync(userId, action, success, from, to, targetType, targetId, ipAddress, page, pageSize, ct);
            return Ok(result);
        }

        [HttpGet("export.csv")]
        public async Task<IActionResult> ExportCsv(
            [FromQuery] int? userId,
            [FromQuery] string? action,
            [FromQuery] bool? success,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? targetType,
            [FromQuery] int? targetId,
            [FromQuery] string? ipAddress,
            CancellationToken ct = default)
        {
            var (bytes, fileName) = await _auditLogs.ExportCsvAsync(userId, action, success, from, to, targetType, targetId, ipAddress, ct);
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }
    }
}