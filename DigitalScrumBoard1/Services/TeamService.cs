using DigitalScrumBoard1.Utilities;
using DigitalScrumBoard1.Data;
using DigitalScrumBoard1.Dtos;
using DigitalScrumBoard1.Hubs;
using DigitalScrumBoard1.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DigitalScrumBoard1.Services
{
    public sealed class TeamService : ITeamService
    {
        private readonly DigitalScrumBoardContext _db;
        private readonly IAuditService _audit;
        private readonly IHubContext<NotificationHub> _hub;

        public TeamService(DigitalScrumBoardContext db, IAuditService audit, IHubContext<NotificationHub> hub)
        {
            _db = db;
            _audit = audit;
            _hub = hub;
        }

        public async Task<object> CreateTeamAsync(CreateTeamRequestDto req, int actorUserId, string ipAddress, CancellationToken ct)
        {
            var name = (req.TeamName ?? string.Empty).Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Team name is required.");

            var team = new Team
            {
                TeamName = name,
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                IsActive = true,
                CreatedAt = DateTimeHelper.Now
            };

            _db.Teams.Add(team);
            await _db.SaveChangesAsync(ct);

            await _audit.LogAsync(
                actorUserId,
                "CREATE_TEAM",
                "Team",
                team.TeamID,
                true,
                $"Created team {team.TeamName}",
                ipAddress,
                ct);

            await _hub.Clients.All.SendAsync("AdminDirectoryChanged", new { reason = "teams" }, ct);

            return new
            {
                team.TeamID,
                team.TeamName,
                team.Description,
                team.IsActive,
                team.CreatedAt
            };
        }

        public async Task<object> DisableTeamAsync(int teamId, int actorUserId, string ipAddress, CancellationToken ct)
        {
            if (teamId <= 0)
                throw new InvalidOperationException("TeamID must be greater than 0.");

            if (teamId == SystemDefaults.DefaultTeamId)
                throw new InvalidOperationException("Default Team cannot be deleted.");

            var team = await _db.Teams.SingleOrDefaultAsync(t => t.TeamID == teamId, ct);
            if (team is null)
                throw new InvalidOperationException("Team not found.");

            var members = await _db.Users
                .Where(u => u.TeamID == teamId)
                .ToListAsync(ct);

            var memberIds = members.Select(u => u.UserID).ToList();
            var memberNames = members
                .Select(u => $"{u.FirstName} {u.LastName}".Trim())
                .ToList();

            var teamWorkItems = await _db.WorkItems
                .IgnoreQueryFilters()
                .Where(w => w.TeamID == teamId)
                .ToListAsync(ct);

            var sprintRows = await _db.Sprints
                .Where(s => s.TeamID == teamId)
                .ToListAsync(ct);

            foreach (var member in members)
            {
                member.TeamID = SystemDefaults.DefaultTeamId;
                member.UpdatedAt = DateTimeHelper.Now;
            }

            foreach (var wi in teamWorkItems)
            {
                wi.TeamID = SystemDefaults.DefaultTeamId;
                wi.UpdatedAt = DateTimeHelper.Now;
            }

            foreach (var sprint in sprintRows)
            {
                sprint.TeamID = SystemDefaults.DefaultTeamId;
                sprint.UpdatedAt = DateTimeHelper.Now;
            }

            _db.Teams.Remove(team);
            await _db.SaveChangesAsync(ct);

            if (memberIds.Count > 0)
            {
                var notifications = memberIds
                    .Select(uid => new Notification
                    {
                        UserID = uid,
                        NotificationType = "UserAccessUpdated",
                        Message = $"Your team '{team.TeamName}' was deleted. You were moved to Default Team (unassigned).",
                        CreatedAt = DateTimeHelper.Now,
                        IsRead = false
                    })
                    .ToList();

                await _db.Notifications.AddRangeAsync(notifications, ct);
                await _db.SaveChangesAsync(ct);

                foreach (var userId in memberIds)
                {
                    await _hub.Clients.Group($"user-{userId}")
                        .SendAsync("UserPermissionsChanged", new { userId }, ct);
                }
            }

            await _audit.LogAsync(
                actorUserId,
                "DELETE_TEAM",
                "Team",
                team.TeamID,
                true,
                $"Deleted team {team.TeamName}; reassigned {memberIds.Count} member(s), {teamWorkItems.Count} work item(s), and {sprintRows.Count} sprint(s) to Default Team.",
                ipAddress,
                ct);

            await _hub.Clients.All.SendAsync("AdminDirectoryChanged", new { reason = "teams" }, ct);

            return new
            {
                message = "Team deleted successfully.",
                teamID = team.TeamID,
                teamName = team.TeamName,
                reassignedCount = memberIds.Count,
                reassignedUserIds = memberIds,
                reassignedUserNames = memberNames,
                reassignedWorkItemCount = teamWorkItems.Count,
                reassignedSprintCount = sprintRows.Count
            };
        }

        public async Task<object?> GetTeamByIdAsync(int id, CancellationToken ct)
        {
            return await _db.Teams
                .AsNoTracking()
                .Where(t => t.TeamID == id)
                .Select(t => new
                {
                    t.TeamID,
                    t.TeamName,
                    t.Description,
                    t.IsActive,
                    t.CreatedAt
                })
                .SingleOrDefaultAsync(ct);
        }

        public async Task<object> ListTeamsAsync(
            string? search,
            bool? isActive,
            string? sortBy,
            string? sortDirection,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 1;
            if (pageSize > 200) pageSize = 200;

            var q = _db.Teams.AsNoTracking().AsQueryable();

            if (isActive.HasValue)
                q = q.Where(t => t.IsActive == isActive.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLowerInvariant();
                q = q.Where(t =>
                    t.TeamName.ToLower().Contains(s) ||
                    (t.Description != null && t.Description.ToLower().Contains(s)));
            }

            q = ApplyTeamSorting(q, sortBy, sortDirection);

            var total = await q.CountAsync(ct);

            var items = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new
                {
                    t.TeamID,
                    t.TeamName,
                    t.Description,
                    t.IsActive,
                    t.CreatedAt
                })
                .ToListAsync(ct);

            return new
            {
                page,
                pageSize,
                total,
                items
            };
        }

        private static IQueryable<Team> ApplyTeamSorting(
            IQueryable<Team> q,
            string? sortBy,
            string? sortDirection)
        {
            var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

            return sortBy?.Trim() switch
            {
                "TeamName" => descending ? q.OrderByDescending(t => t.TeamName) : q.OrderBy(t => t.TeamName),
                "CreatedAt" => descending ? q.OrderByDescending(t => t.CreatedAt) : q.OrderBy(t => t.CreatedAt),
                "IsActive" => descending ? q.OrderByDescending(t => t.IsActive).ThenBy(t => t.TeamName) : q.OrderBy(t => t.IsActive).ThenBy(t => t.TeamName),
                _ => q.OrderBy(t => t.TeamName)
            };
        }
    }
}