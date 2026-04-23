using DigitalScrumBoard1.Utilities;
using DigitalScrumBoard1.Data;
using DigitalScrumBoard1.DTOs.SignalR;
using DigitalScrumBoard1.Hubs;
using DigitalScrumBoard1.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DigitalScrumBoard1.Services
{
    public sealed class AuditService : IAuditService
    {
        private readonly DigitalScrumBoardContext _db;
        private readonly IHubContext<NotificationHub> _hub;

        public AuditService(DigitalScrumBoardContext db, IHubContext<NotificationHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        public async Task LogAsync(
            int actorUserId,
            string action,
            string targetType,
            int targetId,
            bool success,
            string details,
            string ipAddress,
            CancellationToken ct = default)
        {
            var log = new AuditLog
            {
                UserID = actorUserId,
                Action = action,
                IPAddress = ipAddress,
                Timestamp = DateTimeHelper.Now,
                Success = success,
                Details = details,
                TargetType = targetType,
                TargetID = targetId
            };

            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            // Broadcast to admins for real-time audit log panel
            var dto = new AuditLogBroadcastDto
            {
                LogID = log.LogID,
                UserID = log.UserID,
                Action = log.Action,
                IPAddress = log.IPAddress,
                Timestamp = log.Timestamp,
                Success = log.Success,
                Details = log.Details,
                TargetType = log.TargetType,
                TargetID = log.TargetID,
            };

            await _hub.Clients.Group("admins").SendAsync("AuditLogCreated", dto, ct);
        }
    }
}