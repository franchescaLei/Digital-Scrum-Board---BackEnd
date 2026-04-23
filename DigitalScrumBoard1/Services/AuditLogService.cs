using DigitalScrumBoard1.Utilities;
using System.Text;
using DigitalScrumBoard1.Data;
using DigitalScrumBoard1.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalScrumBoard1.Services
{
    public sealed class AuditLogService : IAuditLogService
    {
        private readonly DigitalScrumBoardContext _db;

        public AuditLogService(DigitalScrumBoardContext db)
        {
            _db = db;
        }

        public async Task<object?> GetByIdAsync(int id, CancellationToken ct)
        {
            return await _db.AuditLogs
                .AsNoTracking()
                .Where(x => x.LogID == id)
                .Select(x => new
                {
                    x.LogID,
                    x.UserID,
                    x.Action,
                    x.Success,
                    x.IPAddress,
                    x.Timestamp,
                    x.TargetType,
                    x.TargetID,
                    x.Details
                })
                .SingleOrDefaultAsync(ct);
        }

        public async Task<object> GetPagedAsync(
            int? userId, string? action, bool? success, DateTime? from, DateTime? to,
            string? targetType, int? targetId, string? ipAddress,
            int page, int pageSize, CancellationToken ct)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 1;
            if (pageSize > 200) pageSize = 200;

            var q = BuildFilteredQuery(userId, action, success, from, to, targetType, targetId, ipAddress);

            var total = await q.CountAsync(ct);

            var items = await q
                .OrderByDescending(x => x.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.LogID,
                    x.UserID,
                    x.Action,
                    x.Success,
                    x.IPAddress,
                    x.Timestamp,
                    x.TargetType,
                    x.TargetID,
                    x.Details
                })
                .ToListAsync(ct);

            return new { page, pageSize, total, items };
        }

        public async Task<(byte[] bytes, string fileName)> ExportCsvAsync(
            int? userId, string? action, bool? success, DateTime? from, DateTime? to,
            string? targetType, int? targetId, string? ipAddress,
            CancellationToken ct)
        {
            const int maxRows = 5000;

            var q = BuildFilteredQuery(userId, action, success, from, to, targetType, targetId, ipAddress);

            var rows = await q
                .OrderByDescending(x => x.Timestamp)
                .Take(maxRows)
                .Select(x => new
                {
                    x.LogID,
                    x.UserID,
                    x.Action,
                    x.Success,
                    x.IPAddress,
                    x.Timestamp,
                    x.TargetType,
                    x.TargetID,
                    x.Details
                })
                .ToListAsync(ct);

            var csv = BuildCsv(rows);
            var fileName = $"audit-logs-{DateTimeHelper.Now:yyyyMMdd-HHmmss}.csv";
            return (Encoding.UTF8.GetBytes(csv), fileName);
        }

        private IQueryable<AuditLog> BuildFilteredQuery(
            int? userId,
            string? action,
            bool? success,
            DateTime? from,
            DateTime? to,
            string? targetType,
            int? targetId,
            string? ipAddress)
        {
            var q = _db.AuditLogs.AsNoTracking();

            if (userId.HasValue) q = q.Where(x => x.UserID == userId.Value);

            if (!string.IsNullOrWhiteSpace(action))
            {
                var a = action.Trim();
                q = q.Where(x => x.Action == a);
            }

            if (success.HasValue) q = q.Where(x => x.Success == success.Value);
            if (from.HasValue) q = q.Where(x => x.Timestamp >= from.Value);
            if (to.HasValue) q = q.Where(x => x.Timestamp <= to.Value);

            if (!string.IsNullOrWhiteSpace(targetType))
            {
                var tt = targetType.Trim();
                q = q.Where(x => x.TargetType == tt);
            }

            if (targetId.HasValue) q = q.Where(x => x.TargetID == targetId.Value);

            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                var ip = ipAddress.Trim();
                q = q.Where(x => x.IPAddress == ip);
            }

            return q;
        }

        private static string BuildCsv<T>(IEnumerable<T> rows)
        {
            var props = typeof(T).GetProperties();

            static string Escape(string? s)
            {
                s ??= "";
                var needsQuotes = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
                s = s.Replace("\"", "\"\"");
                return needsQuotes ? $"\"{s}\"" : s;
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", props.Select(p => Escape(p.Name))));

            foreach (var row in rows)
            {
                var values = props.Select(p => Escape(p.GetValue(row)?.ToString()));
                sb.AppendLine(string.Join(",", values));
            }

            return sb.ToString();
        }
    }
}