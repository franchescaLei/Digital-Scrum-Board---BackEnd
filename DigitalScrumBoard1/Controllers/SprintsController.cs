using DigitalScrumBoard1.Utilities;
using DigitalScrumBoard1.DTOs.Sprints;
using DigitalScrumBoard1.DTOs.SignalR;
using DigitalScrumBoard1.Hubs;
using DigitalScrumBoard1.Models;
using DigitalScrumBoard1.Repositories;
using DigitalScrumBoard1.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace DigitalScrumBoard1.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class SprintsController : ControllerBase
{
    private readonly ISprintRepository _repo;
    private readonly IAuditService _audit;
    private readonly IHubContext<BoardHub> _hub;
    private readonly INotificationService _notifications;

    public SprintsController(
        ISprintRepository repo,
        IAuditService audit,
        IHubContext<BoardHub> hub,
        INotificationService notifications)
    {
        _repo = repo;
        _audit = audit;
        _hub = hub;
        _notifications = notifications;
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<ActionResult<SprintCreatedResponseDto>> Create(
        [FromBody] CreateSprintRequestDto req,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        if (!IsElevatedSprintRole())
            return BadRequest(new { message = "You do not have permission to create sprints. Only Administrators and Scrum Masters can create sprints." });

        var sprintName = (req.SprintName ?? "").Trim();
        var goal = (req.Goal ?? "").Trim();

        if (sprintName.Length == 0)
            return BadRequest(new { message = "SprintName is required." });

        if (goal.Length == 0)
            return BadRequest(new { message = "Goal is required." });

        if (!req.StartDate.HasValue)
            return BadRequest(new { message = "StartDate is required." });

        if (!req.EndDate.HasValue)
            return BadRequest(new { message = "EndDate is required." });

        if (!req.ManagedBy.HasValue)
            return BadRequest(new { message = "ManagedBy is required." });

        if (req.EndDate.Value < req.StartDate.Value)
            return BadRequest(new { message = "EndDate cannot be earlier than StartDate." });

        if (req.StartDate.Value < DateTimeHelper.Today)
            return BadRequest(new { message = "Sprint start date cannot be in the past." });

        var managerExists = await _repo.UserExistsAsync(req.ManagedBy.Value, ct);
        if (!managerExists)
            return BadRequest(new { message = "ManagedBy user was not found or is disabled." });

        if (req.TeamID.HasValue)
        {
            var teamExists = await _repo.TeamExistsAsync(req.TeamID.Value, ct);
            if (!teamExists)
                return BadRequest(new { message = "Team not found." });
        }

        var now = DateTimeHelper.Now;

        var sprint = new Sprint
        {
            SprintName = sprintName,
            Goal = goal,
            StartDate = req.StartDate.Value,
            EndDate = req.EndDate.Value,
            Status = "Planned",
            ManagedBy = req.ManagedBy.Value,
            TeamID = req.TeamID,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repo.AddAsync(sprint, ct);
        await _repo.SaveChangesAsync(ct);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        await _audit.LogAsync(
            userId.Value,
            "Sprint.Create",
            "Sprint",
            sprint.SprintID,
            true,
            $"SprintName={sprint.SprintName}; Goal={sprint.Goal}; StartDate={sprint.StartDate}; EndDate={sprint.EndDate}; ManagedBy={sprint.ManagedBy}; TeamID={sprint.TeamID}",
            ip,
            ct
        );

        // Broadcast sprint creation to all clients (for backlog/sprint list views)
        await _hub.Clients.All.SendAsync("SprintCreated", new SprintBroadcastDto
        {
            SprintID = sprint.SprintID,
            SprintName = sprint.SprintName,
            Goal = sprint.Goal,
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate,
            Status = sprint.Status,
            ManagedBy = sprint.ManagedBy,
            ManagedByName = FormatManagerDisplayName(sprint.Manager),
            TeamID = sprint.TeamID,
            TeamName = sprint.Team?.TeamName,
            CreatedAt = sprint.CreatedAt,
            UpdatedAt = sprint.UpdatedAt
        }, ct);

        // In-app notifications + SignalR toasts (NotificationHub) for manager / team
        var notificationRows = new List<Notification>();
        if (req.ManagedBy.Value != userId.Value)
        {
            notificationRows.Add(new Notification
            {
                UserID = req.ManagedBy.Value,
                RelatedSprintID = sprint.SprintID,
                NotificationType = "SprintManagerAssigned",
                Message = $"You were assigned as sprint manager for \"{sprintName}\".",
                CreatedAt = now,
                IsRead = false
            });
        }

        // TeamID=1 (Default Team) is not a real team — no team-based notifications
        if (req.TeamID.HasValue && req.TeamID.Value != Utilities.SystemDefaults.DefaultTeamId)
        {
            var teamUserIds = await _repo.GetActiveUserIdsForTeamAsync(req.TeamID.Value, ct);
            var teamName = await _repo.GetTeamNameAsync(req.TeamID.Value, ct) ?? "your team";
            foreach (var memberId in teamUserIds)
            {
                if (memberId == userId.Value)
                    continue;
                if (memberId == req.ManagedBy.Value && req.ManagedBy.Value != userId.Value)
                    continue;
                notificationRows.Add(new Notification
                {
                    UserID = memberId,
                    RelatedSprintID = sprint.SprintID,
                    NotificationType = "SprintCreatedForTeam",
                    Message = $"Sprint \"{sprintName}\" was created for team {teamName}.",
                    CreatedAt = now,
                    IsRead = false
                });
            }
        }

        if (notificationRows.Count > 0)
            await _notifications.AddNotificationsAsync(notificationRows, ct);

        var resp = new SprintCreatedResponseDto
        {
            SprintID = sprint.SprintID,
            SprintName = sprint.SprintName,
            Goal = sprint.Goal ?? "",
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate,
            Status = sprint.Status,
            ManagedBy = sprint.ManagedBy,
            ManagedByName = FormatManagerDisplayName(sprint.Manager),
            TeamID = sprint.TeamID,
            TeamName = sprint.Team?.TeamName
        };

        return CreatedAtAction(nameof(GetById), new { id = sprint.SprintID }, resp);
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<ActionResult<IEnumerable<object>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] int? teamId,
        [FromQuery] int? managedBy,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _repo.GetPagedAsync(
            status,
            teamId,
            managedBy,
            from,
            to,
            search,
            sortBy,
            sortDirection,
            page,
            pageSize,
            ct);

        return Ok(new
        {
            page,
            pageSize,
            total = result.Total,
            items = result.Items
        });
    }

    [HttpGet("{id:int}")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<ActionResult<object>> GetById([FromRoute] int id, CancellationToken ct)
    {
        if (id <= 0)
            return BadRequest(new { message = "SprintID must be greater than 0." });

        var sprint = await _repo.GetByIdAsync(id, ct);
        if (sprint is null)
            return NotFound(new { message = "Sprint not found." });

        return Ok(new
        {
            sprintID = sprint.SprintID,
            sprintName = sprint.SprintName,
            goal = sprint.Goal,
            startDate = sprint.StartDate,
            endDate = sprint.EndDate,
            status = sprint.Status,
            managedBy = sprint.ManagedBy,
            managedByName = FormatManagerDisplayName(sprint.Manager),
            teamID = sprint.TeamID,
            teamName = sprint.Team?.TeamName,
            createdAt = sprint.CreatedAt,
            updatedAt = sprint.UpdatedAt
        });
    }

    [HttpPatch("{id:int}")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<IActionResult> Patch(
        [FromRoute] int id,
        [FromBody] UpdateSprintRequestDto req,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (id <= 0)
            return BadRequest(new { message = "SprintID must be greater than 0." });

        if (req is null)
            return BadRequest(new { message = "Request body is required." });

        var hasAnyPatchField =
            req.SprintName is not null ||
            req.Goal is not null ||
            req.StartDate.HasValue ||
            req.EndDate.HasValue ||
            req.TeamID.HasValue ||
            req.ManagedBy.HasValue;

        if (!hasAnyPatchField)
            return BadRequest(new { message = "At least one field must be provided." });

        var sprint = await _repo.GetTrackedByIdAsync(id, ct);
        if (sprint is null)
            return NotFound(new { message = "Sprint not found." });

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        if (!CanManageSprint(userId.Value, sprint.ManagedBy))
            return Forbid();

        var isElevatedRole = IsElevatedSprintRole();

        // Sprint managers can only change team assignment; all other sprint metadata is Admin/SM only.
        if (!isElevatedRole)
        {
            var hasNonTeamChange =
                req.SprintName is not null ||
                req.Goal is not null ||
                req.StartDate.HasValue ||
                req.EndDate.HasValue ||
                req.ManagedBy.HasValue;

            if (hasNonTeamChange || !req.TeamID.HasValue)
                return StatusCode(403, new { message = "Only administrators and scrum masters can edit sprint details. Sprint managers can only change sprint team assignment." });
        }

        if (req.ManagedBy.HasValue && !isElevatedRole)
            return Forbid();

        var changedFields = new List<string>();
        int? oldManagerIdBeforeUpdate = sprint.ManagedBy;

        if (req.SprintName is not null)
        {
            var newSprintName = req.SprintName.Trim();
            if (newSprintName.Length == 0)
                return BadRequest(new { message = "SprintName cannot be empty." });

            if (!string.Equals(sprint.SprintName, newSprintName, StringComparison.Ordinal))
            {
                changedFields.Add($"SprintName:{sprint.SprintName}->{newSprintName}");
                sprint.SprintName = newSprintName;
            }
        }

        if (req.Goal is not null)
        {
            var newGoal = req.Goal.Trim();
            if (newGoal.Length == 0)
                return BadRequest(new { message = "Goal cannot be empty." });

            if (!string.Equals(sprint.Goal ?? "", newGoal, StringComparison.Ordinal))
            {
                changedFields.Add($"Goal:{sprint.Goal}->{newGoal}");
                sprint.Goal = newGoal;
            }
        }

        if (req.TeamID.HasValue)
        {
            var teamExists = await _repo.TeamExistsAsync(req.TeamID.Value, ct);
            if (!teamExists)
                return BadRequest(new { message = "Team not found." });

            if (sprint.TeamID != req.TeamID.Value)
            {
                changedFields.Add($"TeamID:{sprint.TeamID}->{req.TeamID.Value}");
                sprint.TeamID = req.TeamID.Value;
            }
        }

        if (req.ManagedBy.HasValue)
        {
            var managerExists = await _repo.UserExistsAsync(req.ManagedBy.Value, ct);
            if (!managerExists)
                return BadRequest(new { message = "ManagedBy user was not found or is disabled." });

            if (sprint.ManagedBy != req.ManagedBy.Value)
            {
                changedFields.Add($"ManagedBy:{sprint.ManagedBy}->{req.ManagedBy.Value}");
                sprint.ManagedBy = req.ManagedBy.Value;
            }
        }

        var finalStartDate = req.StartDate ?? sprint.StartDate;
        var finalEndDate = req.EndDate ?? sprint.EndDate;

        if (finalEndDate < finalStartDate)
            return BadRequest(new { message = "EndDate cannot be earlier than StartDate." });

        if (req.StartDate.HasValue && sprint.StartDate != req.StartDate.Value)
        {
            changedFields.Add($"StartDate:{sprint.StartDate}->{req.StartDate.Value}");
            sprint.StartDate = req.StartDate.Value;
        }

        if (req.EndDate.HasValue && sprint.EndDate != req.EndDate.Value)
        {
            changedFields.Add($"EndDate:{sprint.EndDate}->{req.EndDate.Value}");
            sprint.EndDate = req.EndDate.Value;
        }

        if (changedFields.Count == 0)
        {
            return Ok(new
            {
                message = "No changes detected.",
                sprintID = sprint.SprintID
            });
        }

        var assignedUserIds = await _repo.GetSprintAssignedUserIdsAsync(id, ct);
        var notificationSet = new HashSet<int>();
        var notifications = new List<Notification>();

        // Notify users with work items in this sprint
        foreach (var assignedUserId in assignedUserIds)
        {
            if (notificationSet.Add(assignedUserId))
            {
                notifications.Add(new Notification
                {
                    UserID = assignedUserId,
                    RelatedSprintID = id,
                    NotificationType = "SprintUpdated",
                    Message = $"Sprint '{sprint.SprintName}' has been updated.",
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }
        }

        // ADDITIVE: Notify team members if sprint has a team
        // TeamID=1 (Default Team) is not a real team — no team-based notifications
        if (sprint.TeamID.HasValue && sprint.TeamID.Value != Utilities.SystemDefaults.DefaultTeamId)
        {
            var teamUserIds = await _repo.GetActiveUserIdsForTeamAsync(sprint.TeamID.Value, ct);
            foreach (var teamMemberId in teamUserIds)
            {
                if (teamMemberId == userId.Value) continue;
                if (notificationSet.Add(teamMemberId))
                {
                    notifications.Add(new Notification
                    {
                        UserID = teamMemberId,
                        RelatedSprintID = id,
                        NotificationType = "SprintUpdated",
                        Message = $"Sprint '{sprint.SprintName}' has been updated for your team.",
                        CreatedAt = DateTimeHelper.Now,
                        IsRead = false
                    });
                }
            }
        }

        // Notify old and new manager when ManagedBy changes
        var managedByChanged = changedFields.Any(f => f.StartsWith("ManagedBy:"));
        if (managedByChanged)
        {
            var newManagerId = req.ManagedBy;

            // Notify old manager (if different from new manager and not self)
            if (oldManagerIdBeforeUpdate.HasValue && oldManagerIdBeforeUpdate != newManagerId && oldManagerIdBeforeUpdate != userId)
            {
                notifications.Add(new Notification
                {
                    UserID = oldManagerIdBeforeUpdate.Value,
                    RelatedSprintID = id,
                    NotificationType = "SprintManagerRemoved",
                    Message = $"You are no longer the manager of sprint '{sprint.SprintName}'.",
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }

            // Notify new manager (if not self)
            if (newManagerId.HasValue && newManagerId != userId)
            {
                notifications.Add(new Notification
                {
                    UserID = newManagerId.Value,
                    RelatedSprintID = id,
                    NotificationType = "SprintManagerAssigned",
                    Message = $"You have been assigned as the manager of sprint '{sprint.SprintName}'.",
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }
        }

        sprint.UpdatedAt = DateTimeHelper.Now;
        await _repo.SaveChangesAsync(ct);
        if (notifications.Count > 0)
            await _notifications.AddNotificationsAsync(notifications, ct);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        await _audit.LogAsync(
            userId.Value,
            "Sprint.Update",
            "Sprint",
            sprint.SprintID,
            true,
            $"Updated SprintID={sprint.SprintID}; Changes={string.Join("; ", changedFields)}",
            ip,
            ct
        );

        // Broadcast to all clients so all planning workspaces stay in sync.
        await _hub.Clients.All.SendAsync("SprintUpdated", new SprintBroadcastDto
        {
            SprintID = sprint.SprintID,
            SprintName = sprint.SprintName,
            Goal = sprint.Goal,
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate,
            Status = sprint.Status,
            ManagedBy = sprint.ManagedBy,
            ManagedByName = FormatManagerDisplayName(sprint.Manager),
            TeamID = sprint.TeamID,
            TeamName = sprint.Team?.TeamName,
            UpdatedAt = sprint.UpdatedAt
        }, ct);

        return Ok(new
        {
            message = "Sprint updated successfully.",
            sprintID = sprint.SprintID,
            changedFields,
            sprint = new
            {
                sprintID = sprint.SprintID,
                sprintName = sprint.SprintName,
                goal = sprint.Goal,
                startDate = sprint.StartDate,
                endDate = sprint.EndDate,
                status = sprint.Status,
                managedBy = sprint.ManagedBy,
                managedByName = FormatManagerDisplayName(sprint.Manager),
                teamID = sprint.TeamID,
                teamName = sprint.Team?.TeamName,
                createdAt = sprint.CreatedAt,
                updatedAt = sprint.UpdatedAt
            }
        });
    }

    [HttpPut("{id:int}/start")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<IActionResult> Start([FromRoute] int id, CancellationToken ct)
    {
        var sprint = await _repo.GetByIdAsync(id, ct);
        if (sprint is null)
            return NotFound(new { message = "Sprint not found." });

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        if (!CanManageSprint(userId.Value, sprint.ManagedBy))
            return Forbid();

        if (sprint.Status == "Active")
            return BadRequest(new { message = "Sprint is already active." });

        if (sprint.Status == "Completed")
            return BadRequest(new { message = "Completed sprint cannot be started." });

        var hasWorkItems = await _repo.HasAnyWorkItemsAsync(id, ct);
        if (!hasWorkItems)
        {
            return Conflict(new
            {
                message = "Sprint cannot be started because it has no work items."
            });
        }

        var workItemsMissingAssignee = await _repo.GetSprintWorkItemsMissingAssigneeAsync(id, ct);
        if (workItemsMissingAssignee.Count > 0)
        {
            var unassignedWorkItems = workItemsMissingAssignee
                .Select(w => new
                {
                    w.WorkItemID,
                    Title = w.Title
                })
                .ToList();

            return Conflict(new
            {
                message = "Sprint cannot be started because some work items do not have an assigned user.",
                unassignedWorkItems
            });
        }

        await _repo.StartSprintAsync(id, ct);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        await _audit.LogAsync(
            userId.Value,
            "Sprint.Start",
            "Sprint",
            id,
            true,
            $"Started SprintID={id}; SprintName={sprint.SprintName}",
            ip,
            ct
        );

        var assignedUserIds = await _repo.GetSprintAssignedUserIdsAsync(id, ct);
        var notificationSet = new HashSet<int>();
        var notifications = new List<Notification>();

        foreach (var assignedUserId in assignedUserIds)
        {
            if (assignedUserId == userId.Value) continue;
            if (notificationSet.Add(assignedUserId))
            {
                notifications.Add(new Notification
                {
                    UserID = assignedUserId,
                    RelatedSprintID = id,
                    NotificationType = "SprintStarted",
                    Message = $"Sprint '{sprint.SprintName}' has started.",
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }
        }

        // ADDITIVE: Also notify all active team members if sprint has a team
        // TeamID=1 (Default Team) is not a real team — no team-based notifications
        if (sprint.TeamID.HasValue && sprint.TeamID.Value != Utilities.SystemDefaults.DefaultTeamId)
        {
            var teamUserIds = await _repo.GetActiveUserIdsForTeamAsync(sprint.TeamID.Value, ct);
            foreach (var teamMemberId in teamUserIds)
            {
                if (teamMemberId == userId.Value) continue;
                if (notificationSet.Add(teamMemberId))
                {
                    notifications.Add(new Notification
                    {
                        UserID = teamMemberId,
                        RelatedSprintID = id,
                        NotificationType = "SprintStarted",
                        Message = $"Sprint '{sprint.SprintName}' has started for your team.",
                        CreatedAt = DateTimeHelper.Now,
                        IsRead = false
                    });
                }
            }
        }

        if (notifications.Count > 0)
            await _notifications.AddNotificationsAsync(notifications, ct);

        // Broadcast to all clients so all planning workspaces stay in sync.
        await _hub.Clients.All.SendAsync("SprintStarted", new SprintLifecycleBroadcastDto
        {
            SprintID = id,
            SprintName = sprint.SprintName,
            Status = "Active",
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate,
            Goal = sprint.Goal,
            ManagedBy = sprint.ManagedBy,
            TeamID = sprint.TeamID,
            ChangedAt = DateTimeHelper.Now
        }, ct);

        return Ok(new
        {
            message = "Sprint started successfully.",
            sprintID = id,
            status = "Active"
        });
    }

    [HttpPut("{id:int}/stop")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<IActionResult> Stop(
        [FromRoute] int id,
        [FromBody] SprintLifecycleRequestDto? req,
        CancellationToken ct)
    {
        if (id <= 0)
            return BadRequest(new { message = "SprintID must be greater than 0." });

        req ??= new SprintLifecycleRequestDto();

        var sprint = await _repo.GetTrackedByIdAsync(id, ct);
        if (sprint is null)
            return NotFound(new { message = "Sprint not found." });

        if (!string.Equals(sprint.Status, "Active", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only active sprints can be stopped." });

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        if (!CanManageSprint(userId.Value, sprint.ManagedBy))
            return Forbid();

        var sprintWorkItems = await _repo.GetTrackedSprintWorkItemsAsync(id, ct);
        var unfinishedCount = sprintWorkItems.Count(w => !string.Equals(w.Status, "Completed", StringComparison.OrdinalIgnoreCase));
        var completedCount = sprintWorkItems.Count(w => string.Equals(w.Status, "Completed", StringComparison.OrdinalIgnoreCase));

        if (unfinishedCount > 0 && !req.Confirm)
        {
            return Conflict(new
            {
                message = "This sprint still has unfinished work items. Confirm to stop the sprint.",
                requiresConfirmation = true,
                unfinishedCount,
                completedCount
            });
        }

        await _repo.StopSprintAsync(sprint, ct);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        await _audit.LogAsync(
            userId.Value,
            "Sprint.Stop",
            "Sprint",
            id,
            true,
            $"Stopped SprintID={id}; SprintName={sprint.SprintName}; UnfinishedCount={unfinishedCount}; CompletedCount={completedCount}",
            ip,
            ct
        );

        var affectedUserIds = sprintWorkItems
            .Where(w => w.AssignedUserID.HasValue)
            .Select(w => w.AssignedUserID!.Value)
            .Distinct()
            .ToList();

        var notificationSet = new HashSet<int>();
        var notifications = new List<Notification>();

        foreach (var assignedUserId in affectedUserIds)
        {
            if (assignedUserId == userId.Value) continue;
            if (notificationSet.Add(assignedUserId))
            {
                notifications.Add(new Notification
                {
                    UserID = assignedUserId,
                    RelatedSprintID = id,
                    NotificationType = "SprintStopped",
                    Message = $"Sprint '{sprint.SprintName}' has been stopped.",
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }
        }

        // ADDITIVE: Also notify all active team members if sprint has a team
        if (sprint.TeamID.HasValue)
        {
            var teamUserIds = await _repo.GetActiveUserIdsForTeamAsync(sprint.TeamID.Value, ct);
            foreach (var teamMemberId in teamUserIds)
            {
                if (teamMemberId == userId.Value) continue;
                if (notificationSet.Add(teamMemberId))
                {
                    notifications.Add(new Notification
                    {
                        UserID = teamMemberId,
                        RelatedSprintID = id,
                        NotificationType = "SprintStopped",
                        Message = $"Sprint '{sprint.SprintName}' has been stopped for your team.",
                        CreatedAt = DateTimeHelper.Now,
                        IsRead = false
                    });
                }
            }
        }

        if (notifications.Count > 0)
            await _notifications.AddNotificationsAsync(notifications, ct);

        // Broadcast to all clients so all planning workspaces stay in sync.
        await _hub.Clients.All.SendAsync("SprintStopped", new SprintLifecycleBroadcastDto
        {
            SprintID = id,
            SprintName = sprint.SprintName,
            Status = "Planned",
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate,
            Goal = sprint.Goal,
            ManagedBy = sprint.ManagedBy,
            TeamID = sprint.TeamID,
            UnfinishedCount = unfinishedCount,
            CompletedCount = completedCount,
            ChangedAt = DateTimeHelper.Now
        }, ct);

        return Ok(new
        {
            message = "Sprint stopped successfully.",
            sprintID = id,
            status = "Planned",
            unfinishedCount,
            completedCount
        });
    }

    [HttpPut("{id:int}/complete")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<IActionResult> Complete(
        [FromRoute] int id,
        [FromBody] SprintLifecycleRequestDto? req,
        CancellationToken ct)
    {
        if (id <= 0)
            return BadRequest(new { message = "SprintID must be greater than 0." });

        req ??= new SprintLifecycleRequestDto();

        var sprint = await _repo.GetTrackedByIdAsync(id, ct);
        if (sprint is null)
            return NotFound(new { message = "Sprint not found." });

        if (!string.Equals(sprint.Status, "Active", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only active sprints can be completed." });

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        if (!CanManageSprint(userId.Value, sprint.ManagedBy))
            return Forbid();

        var sprintWorkItems = await _repo.GetTrackedSprintWorkItemsAsync(id, ct);
        var unfinishedCount = sprintWorkItems.Count(w => !string.Equals(w.Status, "Completed", StringComparison.OrdinalIgnoreCase));
        var completedCount = sprintWorkItems.Count(w => string.Equals(w.Status, "Completed", StringComparison.OrdinalIgnoreCase));

        if (unfinishedCount > 0 && !req.Confirm)
        {
            return Conflict(new
            {
                message = "This sprint still has unfinished work items. Confirm to complete the sprint and return unfinished items to backlog.",
                requiresConfirmation = true,
                unfinishedCount,
                completedCount
            });
        }

        var sprintName = sprint.SprintName;

        var affectedUserIds = sprintWorkItems
            .Where(w => w.AssignedUserID.HasValue)
            .Select(w => w.AssignedUserID!.Value)
            .Distinct()
            .ToList();

        await _repo.CompleteSprintAsync(sprint, sprintWorkItems, ct);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        await _audit.LogAsync(
            userId.Value,
            "Sprint.Complete",
            "Sprint",
            id,
            true,
            $"Completed SprintID={id}; SprintName={sprintName}; ReturnedToBacklogCount={unfinishedCount}; CompletedRecordCount={completedCount}",
            ip,
            ct
        );

        var notifications = new List<Notification>();
        var notificationSet = new HashSet<int>();

        foreach (var assignedUserId in affectedUserIds)
        {
            if (assignedUserId == userId.Value) continue;
            if (notificationSet.Add(assignedUserId))
            {
                notifications.Add(new Notification
                {
                    UserID = assignedUserId,
                    RelatedSprintID = null,
                    NotificationType = "SprintCompleted",
                    Message = unfinishedCount > 0
                        ? $"Sprint '{sprintName}' has been completed. {unfinishedCount} unfinished work item(s) were returned to backlog."
                        : $"Sprint '{sprintName}' has been completed.",
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }
        }

        // ADDITIVE: Also notify all active team members if sprint has a team
        if (sprint.TeamID.HasValue)
        {
            var teamUserIds = await _repo.GetActiveUserIdsForTeamAsync(sprint.TeamID.Value, ct);
            foreach (var teamMemberId in teamUserIds)
            {
                if (teamMemberId == userId.Value) continue;
                if (notificationSet.Add(teamMemberId))
                {
                    notifications.Add(new Notification
                    {
                        UserID = teamMemberId,
                        RelatedSprintID = null,
                        NotificationType = "SprintCompleted",
                        Message = unfinishedCount > 0
                            ? $"Sprint '{sprintName}' has been completed for your team. {unfinishedCount} unfinished work item(s) were returned to backlog."
                            : $"Sprint '{sprintName}' has been completed for your team.",
                        CreatedAt = DateTimeHelper.Now,
                        IsRead = false
                    });
                }
            }
        }

        if (notifications.Count > 0)
            await _notifications.AddNotificationsAsync(notifications, ct);

        // Broadcast to all clients so all planning workspaces stay in sync.
        await _hub.Clients.All.SendAsync("SprintCompleted", new SprintLifecycleBroadcastDto
        {
            SprintID = id,
            SprintName = sprintName,
            Status = "Completed",
            UnfinishedCount = unfinishedCount,
            CompletedCount = completedCount,
            ReturnedToBacklogCount = unfinishedCount,
            ChangedAt = DateTimeHelper.Now
        }, ct);

        return Ok(new
        {
            message = "Sprint completed successfully.",
            sprintID = id,
            returnedToBacklogCount = unfinishedCount,
            completedRecordCount = completedCount
        });
    }

    [HttpDelete("{id:int}")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken ct)
    {
        var sprint = await _repo.GetByIdAsync(id, ct);

        if (sprint is null)
            return NotFound(new { message = "Sprint not found." });

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        // Only Administrators and Scrum Masters can delete sprints (not Sprint Owners)
        if (!IsElevatedSprintRole())
            return BadRequest(new { message = "You do not have permission to delete this sprint. Only Administrators and Scrum Masters can delete sprints." });

        // Get affected user IDs before deletion for notification
        var affectedUserIds = await _repo.GetSprintAssignedUserIdsAsync(id, ct);

        var returnedToBacklogCount = await _repo.DeleteSprintAndUnassignWorkItemsAsync(id, ct);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        await _audit.LogAsync(
            userId.Value,
            "Sprint.Delete",
            "Sprint",
            id,
            true,
            $"Deleted SprintID={id}; SprintName={sprint.SprintName}; ReturnedToBacklogCount={returnedToBacklogCount}",
            ip,
            ct
        );

        // Create notifications for affected users with actor exclusion and deduplication
        var notificationSet = new HashSet<int>();
        var notifications = new List<Notification>();

        foreach (var assigneeUserId in affectedUserIds)
        {
            if (assigneeUserId == userId.Value) continue;
            if (notificationSet.Add(assigneeUserId))
            {
                notifications.Add(new Notification
                {
                    UserID = assigneeUserId,
                    RelatedSprintID = null,
                    NotificationType = "SprintDeleted",
                    Message = $"Sprint '{sprint.SprintName}' has been deleted. {returnedToBacklogCount} work item(s) returned to backlog.",
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }
        }

        // ADDITIVE: Also notify all active team members if sprint has a team
        // TeamID=1 (Default Team) is not a real team — no team-based notifications
        if (sprint.TeamID.HasValue && sprint.TeamID.Value != Utilities.SystemDefaults.DefaultTeamId)
        {
            var teamUserIds = await _repo.GetActiveUserIdsForTeamAsync(sprint.TeamID.Value, ct);
            foreach (var teamMemberId in teamUserIds)
            {
                if (teamMemberId == userId.Value) continue;
                if (notificationSet.Add(teamMemberId))
                {
                    notifications.Add(new Notification
                    {
                        UserID = teamMemberId,
                        RelatedSprintID = null,
                        NotificationType = "SprintDeleted",
                        Message = $"Sprint '{sprint.SprintName}' has been deleted for your team. {returnedToBacklogCount} work item(s) returned to backlog.",
                        CreatedAt = DateTimeHelper.Now,
                        IsRead = false
                    });
                }
            }
        }

        if (notifications.Count > 0)
            await _notifications.AddNotificationsAsync(notifications, ct);

        // Broadcast deletion to ALL clients (for real-time backlog/sprint list updates)
        await _hub.Clients.All.SendAsync("SprintDeleted", new
        {
            sprintID = id,
            sprintName = sprint.SprintName,
            returnedToBacklogCount,
            deletedAt = DateTimeHelper.Now
        }, ct);

        return Ok(new
        {
            message = "Sprint deleted successfully.",
            sprintID = id,
            returnedToBacklogCount
        });
    }

    private bool CanManageSprint(int userId, int? sprintManagedByUserId)
    {
        if (IsElevatedSprintRole())
            return true;

        return sprintManagedByUserId.HasValue && sprintManagedByUserId.Value == userId;
    }

    private bool IsElevatedSprintRole()
    {
        return User.IsInRole("Administrator") ||
               User.IsInRole("Scrum Master") ||
               User.IsInRole("ScrumMaster");
    }

    private static int? TryGetUserId(ClaimsPrincipal user)
    {
        var raw =
            user.FindFirstValue(ClaimTypes.NameIdentifier) ??
            user.FindFirstValue("UserID");

        return int.TryParse(raw, out var id) ? id : null;
    }

    private static string? FormatManagerDisplayName(User? u)
    {
        if (u is null) return null;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(u.FirstName)) parts.Add(u.FirstName.Trim());
        if (!string.IsNullOrWhiteSpace(u.MiddleName)) parts.Add(u.MiddleName.Trim());
        if (!string.IsNullOrWhiteSpace(u.LastName)) parts.Add(u.LastName.Trim());
        if (!string.IsNullOrWhiteSpace(u.NameExtension)) parts.Add(u.NameExtension.Trim());
        return parts.Count > 0 ? string.Join(' ', parts) : null;
    }
}