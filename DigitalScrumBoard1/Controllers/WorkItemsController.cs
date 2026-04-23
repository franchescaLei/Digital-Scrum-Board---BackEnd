using DigitalScrumBoard1.Utilities;
using DigitalScrumBoard1.DTOs.WorkItems;
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
public sealed class WorkItemsController : ControllerBase
{
    private readonly IWorkItemRepository _repo;
    private readonly IAuditService _audit;
    private readonly IHubContext<BoardHub> _hub;
    private readonly INotificationService _notifications;

    public WorkItemsController(
        IWorkItemRepository repo,
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
    public async Task<ActionResult<WorkItemCreatedResponseDto>> Create(
        [FromBody] CreateWorkItemRequestDto req,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        var type = NormalizeType(req.Type);
        if (type is null)
            return BadRequest(new { message = "Invalid Type. Allowed: Epic, Story, Task." });

        var isElevated = IsElevatedWorkItemRole();

        // Only Administrators and Scrum Masters can create work items
        if (!isElevated)
            return StatusCode(403, new { message = "Only Administrators and Scrum Masters can create work items." });

        var epicTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Epic", ct);
        var storyTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Story", ct);
        var taskTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Task", ct);

        if (epicTypeId is null || storyTypeId is null || taskTypeId is null)
            return Problem("WorkItemTypes table is missing Epic/Story/Task entries.");

        int workItemTypeId = type switch
        {
            "Epic" => epicTypeId.Value,
            "Story" => storyTypeId.Value,
            "Task" => taskTypeId.Value,
            _ => throw new InvalidOperationException()
        };

        // ── Validation: parent type constraints ──
        if (type == "Story" || type == "Task")
        {
            if (req.ParentWorkItemID is null)
                return BadRequest(new { message = $"{type} requires ParentWorkItemID." });

            var parentInfo = await _repo.GetWorkItemTypeInfoByIdAsync(req.ParentWorkItemID.Value, ct);
            if (parentInfo is null)
                return BadRequest(new { message = "Parent work item not found." });
            if (parentInfo.Value.IsDeleted)
                return BadRequest(new { message = "Cannot create under a deleted parent work item." });

            // Validate parent type constraints
            if (type == "Story" && parentInfo.Value.WorkItemTypeID != epicTypeId.Value)
                return BadRequest(new { message = "Story parent must be an Epic." });

            if (type == "Task")
            {
                var parentType = parentInfo.Value.WorkItemTypeID;
                if (parentType != epicTypeId.Value && parentType != storyTypeId.Value)
                    return BadRequest(new { message = "Task parent must be an Epic or Story." });

                if (req.DueDate.HasValue && parentInfo.Value.WorkItemTypeID == storyTypeId.Value)
                {
                    var storyDueDate = parentInfo.Value.DueDate;
                    if (storyDueDate.HasValue && req.DueDate.Value > storyDueDate.Value)
                        return BadRequest(new { message = $"Task due date cannot be later than its parent story's due date ({storyDueDate.Value})." });
                }
            }
        }

        var title = (req.Title ?? "").Trim();
        var desc = (req.Description ?? "").Trim();
        var priority = NormalizePriority(req.Priority);
        if (title.Length == 0) return BadRequest(new { message = "Title is required." });
        if (desc.Length == 0) return BadRequest(new { message = "Description is required." });
        if (priority is null) return BadRequest(new { message = "Invalid Priority. Allowed: Low, Medium, High, Critical." });

        if (req.TeamID.HasValue)
        {
            var teamExists = await _repo.TeamExistsAsync(req.TeamID.Value, ct);
            if (!teamExists)
                return BadRequest(new { message = "Team not found." });
        }

        if (req.AssignedUserID.HasValue)
        {
            var assigneeExists = await _repo.UserExistsAsync(req.AssignedUserID.Value, ct);
            if (!assigneeExists)
                return BadRequest(new { message = "Assigned user not found." });
        }

        // Epic-specific validations
        if (type == "Epic")
        {
            if (req.DueDate.HasValue && req.DueDate.Value < DateTimeHelper.Today)
                return BadRequest(new { message = "Epic due date cannot be in the past." });
        }

        // Story and Task due date past validation
        if (type is "Story" or "Task")
        {
            if (req.DueDate.HasValue && req.DueDate.Value < DateTimeHelper.Today)
                return BadRequest(new { message = "Due date cannot be in the past." });
        }

        var now = DateTimeHelper.Now;

        var item = new WorkItem
        {
            Title = title,
            Description = desc,
            Priority = priority,
            Status = "To-do",
            WorkItemTypeID = workItemTypeId,
            ParentWorkItemID = type == "Epic" ? null : req.ParentWorkItemID,
            TeamID = type == "Epic" ? null : req.TeamID,
            DueDate = req.DueDate,
            AssignedUserID = req.AssignedUserID,
            SprintID = null,
            CreatedByUserID = userId.Value,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var audit = new AuditLog
        {
            UserID = userId.Value,
            Action = "WorkItem.Create",
            IPAddress = ip,
            Timestamp = now,
            Success = true,
            TargetType = "WorkItem",
            TargetID = null,
            Details = $"Type={type}; Title={title}; ParentWorkItemID={item.ParentWorkItemID}; TeamID={item.TeamID}; AssignedUserID={item.AssignedUserID}"
        };

        await _repo.AddWithAuditAsync(item, audit, ct);

        var creationNotifications = new List<Notification>();

        if (item.AssignedUserID.HasValue && item.AssignedUserID.Value != userId.Value)
        {
            creationNotifications.Add(new Notification
            {
                UserID = item.AssignedUserID.Value,
                NotificationType = "WorkItemAssigned",
                Message = $"You were assigned to work item '{item.Title}'.",
                RelatedWorkItemID = item.WorkItemID,
                CreatedAt = now,
                IsRead = false
            });
        }

        // ADDITIVE: Notify team members of new work item (excluding actor and assignee already notified)
        // TeamID=1 (Default Team) is not a real team — no team-based notifications
        if (item.TeamID.HasValue && item.TeamID.Value != Utilities.SystemDefaults.DefaultTeamId)
        {
            var teamMembers = await _repo.GetUsersByTeamIdAsync(item.TeamID.Value, ct);
            foreach (var tm in teamMembers)
            {
                if (tm == userId.Value) continue;
                if (item.AssignedUserID.HasValue && tm == item.AssignedUserID.Value) continue;
                if (creationNotifications.Any(n => n.UserID == tm)) continue;

                creationNotifications.Add(new Notification
                {
                    UserID = tm,
                    NotificationType = "WorkItemUpdated",
                    Message = $"A new work item '{item.Title}' was created for your team.",
                    RelatedWorkItemID = item.WorkItemID,
                    CreatedAt = now,
                    IsRead = false
                });
            }
        }

        if (creationNotifications.Count > 0)
            await _notifications.AddNotificationsAsync(creationNotifications, ct);

        var resp = new WorkItemCreatedResponseDto
        {
            WorkItemID = item.WorkItemID,
            Type = type,
            Title = item.Title,
            Description = item.Description ?? "",
            Priority = item.Priority ?? "",
            Status = item.Status,
            ParentWorkItemID = item.ParentWorkItemID,
            TeamID = item.TeamID,
            AssignedUserID = item.AssignedUserID
        };

        // Broadcast work item creation to all clients (for backlog/epic views)
        await _hub.Clients.All.SendAsync("WorkItemCreated", new
        {
            workItem = resp,
            createdAt = item.CreatedAt
        }, ct);

        return CreatedAtAction(nameof(GetById), new { id = item.WorkItemID }, resp);
    }

    [HttpGet("{id:int}")]
    [Authorize]
    public async Task<ActionResult<object>> GetById([FromRoute] int id, CancellationToken ct)
    {
        var wi = await _repo.GetByIdAsync(id, ct);
        return wi is null ? NotFound() : Ok(wi);
    }

    [HttpGet("{id:int}/details")]
    [Authorize]
    public async Task<ActionResult<WorkItemDetailsResponseDto>> GetDetails([FromRoute] int id, CancellationToken ct)
    {
        var details = await _repo.GetWorkItemDetailsAsync(id, ct);
        return details is null ? NotFound(new { message = "Work item not found." }) : Ok(details);
    }

    [HttpGet("{id:int}/comments")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<ActionResult<List<WorkItemCommentDto>>> GetComments([FromRoute] int id, CancellationToken ct)
    {
        var itemInfo = await _repo.GetWorkItemTypeInfoByIdAsync(id, ct);
        if (itemInfo is null)
            return NotFound(new { message = "Work item not found." });

        if (itemInfo.Value.IsDeleted)
            return BadRequest(new { message = "Cannot access comments of a deleted work item." });

        var comments = await _repo.GetCommentsAsync(id, ct);
        return Ok(comments);
    }

    [HttpPost("{id:int}/comments")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<ActionResult<WorkItemCommentDto>> AddComment(
        [FromRoute] int id,
        [FromBody] CreateWorkItemCommentRequestDto req,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (req is null || string.IsNullOrWhiteSpace(req.CommentText))
            return BadRequest(new { message = "CommentText is required." });

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        var itemInfo = await _repo.GetWorkItemTypeInfoByIdAsync(id, ct);
        if (itemInfo is null)
            return NotFound(new { message = "Work item not found." });

        if (itemInfo.Value.IsDeleted)
            return BadRequest(new { message = "Cannot comment on a deleted work item." });

        var workItem = await _repo.GetTrackedByIdAsync(id, ct);
        if (workItem is null)
            return NotFound(new { message = "Work item not found." });

        // Authorization: Admin/SM, assignee, team member, Sprint Manager, or sprint team member
        var canComment = CanManageWorkItemWithTeam(userId.Value, workItem.AssignedUserID, workItem.TeamID);
        if (!canComment && workItem.SprintID.HasValue)
        {
            var sprint = await _repo.GetSprintByIdAsync(workItem.SprintID.Value, ct);
            if (sprint is not null)
            {
                canComment = CanManageSprint(userId.Value, sprint.ManagedBy);
                // ADDITIVE: Sprint team members can comment on work items in their sprint
                // TeamID=1 (Default Team) is not a real team — does not grant comment permission
                if (!canComment && sprint.TeamID.HasValue && sprint.TeamID.Value != Utilities.SystemDefaults.DefaultTeamId)
                {
                    var userTeamId = GetUserTeamId();
                    canComment = userTeamId.HasValue && userTeamId.Value != Utilities.SystemDefaults.DefaultTeamId && userTeamId.Value == sprint.TeamID.Value;
                }
            }
        }
        if (!canComment)
            return StatusCode(403, new { message = "Only administrators, scrum masters, the sprint manager, the work item's assignee, team members, or sprint team members can comment." });

        var text = req.CommentText.Trim();
        if (text.Length == 0)
            return BadRequest(new { message = "CommentText cannot be empty." });

        var now = DateTimeHelper.Now;

        var comment = new WorkItemComment
        {
            WorkItemID = workItem.WorkItemID,
            CommentedBy = userId.Value,
            CommentText = text,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false
        };

        await _repo.AddCommentAsync(comment, ct);

        // Notify assignee, team members, sprint team members, and sprint manager
        var notifyUserIds = new HashSet<int>();
        if (workItem.AssignedUserID.HasValue)
            notifyUserIds.Add(workItem.AssignedUserID.Value);
        // TeamID=1 (Default Team) is not a real team — no team-based notifications
        if (workItem.TeamID.HasValue && workItem.TeamID.Value != Utilities.SystemDefaults.DefaultTeamId)
        {
            var teamMembers = await _repo.GetUsersByTeamIdAsync(workItem.TeamID.Value, ct);
            foreach (var tm in teamMembers)
                notifyUserIds.Add(tm);
        }
        // ADDITIVE: Notify sprint team members if work item is in a sprint with a team
        // TeamID=1 (Default Team) is not a real team — no team-based notifications
        if (workItem.SprintID.HasValue)
        {
            var sprint = await _repo.GetSprintByIdAsync(workItem.SprintID.Value, ct);
            if (sprint?.TeamID.HasValue == true && sprint.TeamID.Value != Utilities.SystemDefaults.DefaultTeamId)
            {
                var sprintTeamMembers = await _repo.GetUsersByTeamIdAsync(sprint.TeamID.Value, ct);
                foreach (var stm in sprintTeamMembers)
                    notifyUserIds.Add(stm);
            }
            // Notify sprint manager (deduplicated via HashSet)
            if (sprint?.ManagedBy.HasValue == true)
                notifyUserIds.Add(sprint.ManagedBy.Value);
        }
        notifyUserIds.Remove(userId.Value);

        var commentNotifications = notifyUserIds
            .Select(targetUserId => new Notification
            {
                UserID = targetUserId,
                Message = $"A new comment was added to work item '{workItem.Title}'.",
                NotificationType = "WorkItemCommentAdded",
                RelatedWorkItemID = workItem.WorkItemID,
                RelatedSprintID = workItem.SprintID,
                CreatedAt = now,
                IsRead = false
            })
            .ToList();

        if (commentNotifications.Count > 0)
            await _notifications.AddNotificationsAsync(commentNotifications, ct);
        else
            await _repo.SaveChangesAsync(ct);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        await _audit.LogAsync(
            userId.Value,
            "WorkItem.Comment",
            "WorkItem",
            workItem.WorkItemID,
            true,
            $"Added comment to WorkItemID={workItem.WorkItemID}; CommentID={comment.CommentID}",
            ip,
            ct
        );

        var comments = await _repo.GetCommentsAsync(workItem.WorkItemID, ct);
        var created = comments.FirstOrDefault(c => c.CommentID == comment.CommentID);

        if (created is null)
        {
            created = new WorkItemCommentDto
            {
                CommentID = comment.CommentID,
                WorkItemID = comment.WorkItemID,
                CommentedBy = comment.CommentedBy,
                CommentedByName = string.Empty,
                CommentText = comment.CommentText,
                CreatedAt = comment.CreatedAt,
                UpdatedAt = comment.UpdatedAt
            };
        }

        await _hub.Clients.All.SendAsync("WorkItemCommentAdded", new
        {
            sprintID = workItem.SprintID,
            workItemID = workItem.WorkItemID,
            comment = created
        }, ct);

        return Ok(created);
    }

    [HttpPatch("{id:int}/comments/{commentId:int}")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<IActionResult> EditComment(
        [FromRoute] int id,
        [FromRoute] int commentId,
        [FromBody] CreateWorkItemCommentRequestDto req,
        CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.CommentText))
            return BadRequest(new { message = "CommentText is required." });

        var userId = TryGetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Missing/invalid user identity." });

        var comment = await _repo.GetCommentByIdAsync(commentId, ct);
        if (comment is null) return NotFound(new { message = "Comment not found." });
        // Only the comment's creator can edit
        if (comment.CommentedBy != userId.Value)
            return StatusCode(403, new { message = "Only the comment's author can edit this comment." });

        comment.CommentText = req.CommentText.Trim();
        comment.UpdatedAt = DateTimeHelper.Now;
        await _repo.SaveChangesAsync(ct);

        var ipEdit = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _audit.LogAsync(
            userId.Value,
            "WorkItem.Comment.Edit",
            "WorkItem",
            comment.WorkItemID,
            true,
            $"Edited comment on WorkItemID={comment.WorkItemID}; CommentID={commentId}",
            ipEdit,
            ct);

        // Broadcast edit to all connected clients
        await _hub.Clients.All.SendAsync("WorkItemCommentEdited", new
        {
            workItemID = comment.WorkItemID,
            commentID = comment.CommentID,
            commentText = comment.CommentText
        }, ct);

        return Ok(new { message = "Comment updated." });
    }

    [HttpDelete("{id:int}/comments/{commentId:int}")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<IActionResult> DeleteComment(
        [FromRoute] int id,
        [FromRoute] int commentId,
        CancellationToken ct)
    {
        var userId = TryGetUserId(User);
        if (userId is null) return Unauthorized(new { message = "Missing/invalid user identity." });

        var comment = await _repo.GetCommentByIdAsync(commentId, ct);
        if (comment is null) return NotFound(new { message = "Comment not found." });
        // Creator or elevated role can delete
        if (comment.CommentedBy != userId.Value && !IsElevatedWorkItemRole())
            return StatusCode(403, new { message = "Only the comment's author or an administrator can delete this comment." });

        comment.IsDeleted = true;
        comment.UpdatedAt = DateTimeHelper.Now;
        await _repo.SaveChangesAsync(ct);

        var ipDel = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _audit.LogAsync(
            userId.Value,
            "WorkItem.Comment.Delete",
            "WorkItem",
            comment.WorkItemID,
            true,
            $"Deleted comment on WorkItemID={comment.WorkItemID}; CommentID={commentId}",
            ipDel,
            ct);

        // Broadcast delete to all connected clients
        await _hub.Clients.All.SendAsync("WorkItemCommentDeleted", new
        {
            workItemID = comment.WorkItemID,
            commentID = comment.CommentID
        }, ct);

        return Ok(new { message = "Comment deleted." });
    }

    [HttpGet("parents")]
    [Authorize]
    public async Task<ActionResult<List<object>>> GetParents([FromQuery] string forType, CancellationToken ct)
    {
        var t = NormalizeType(forType);
        if (t is null) return BadRequest(new { message = "Invalid forType." });

        var epicTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Epic", ct);
        var storyTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Story", ct);
        if (epicTypeId is null || storyTypeId is null)
            return Problem("WorkItemTypes missing Epic/Story.");

        var allowed = t == "Story"
            ? new[] { epicTypeId.Value }
            : new[] { epicTypeId.Value, storyTypeId.Value };

        var parents = await _repo.ListParentsAsync(allowed, ct);
        var resp = parents.Select(p => new { p.WorkItemID, p.Title, Type = p.TypeName, p.DueDate }).ToList();
        return Ok(resp);
    }

    [HttpGet("epics")]
    [Authorize]
    public async Task<ActionResult<List<EpicTileDto>>> GetEpicTiles(
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken ct = default)
    {
        var result = await _repo.GetEpicTilesFilteredAsync(search, sortBy, sortDirection, ct);
        return Ok(result);
    }

    [HttpGet("stories/by-epic")]
    [Authorize]
    public async Task<ActionResult<List<WorkItemDto>>> GetStoriesByEpicId(
        [FromQuery] int epicId,
        CancellationToken ct = default)
    {
        if (epicId <= 0)
            return BadRequest(new { message = "EpicID is required." });

        var result = await _repo.GetWorkItemsByParentIdAsync(epicId, "Story", ct);
        return Ok(result);
    }

    [HttpGet("tasks/by-parent")]
    [Authorize]
    public async Task<ActionResult<List<WorkItemDto>>> GetTasksByParentId(
        [FromQuery] int parentId,
        CancellationToken ct = default)
    {
        if (parentId <= 0)
            return BadRequest(new { message = "ParentID is required." });

        var result = await _repo.GetWorkItemsByParentIdAsync(parentId, "Task", ct);
        return Ok(result);
    }

    [HttpGet("epic/{epicId:int}/hierarchy")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<ActionResult<WorkItemHierarchyDto>> GetEpicHierarchy(
        [FromRoute] int epicId,
        CancellationToken ct = default)
    {
        if (epicId <= 0)
            return BadRequest(new { message = "EpicID must be greater than 0." });

        var result = await _repo.GetEpicHierarchyAsync(epicId, ct);
        if (result is null)
            return NotFound(new { message = "Epic not found." });

        return Ok(result);
    }

    [HttpGet("backlog")]
    [Authorize]
    public async Task<ActionResult<List<AgendaWorkItemDto>>> GetBacklogItems(
        CancellationToken ct = default)
    {
        var result = await _repo.GetBacklogItemsAsync(ct);
        return Ok(result);
    }

    [HttpGet("agendas")]
    [Authorize]
    public async Task<ActionResult<AgendasResponseDto>> GetAgendas(
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] string? workItemType,
        [FromQuery] int? teamId,
        [FromQuery] int? assigneeId,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDirection,
        CancellationToken ct = default)
    {
        var result = await _repo.GetAgendasFilteredAsync(
            status,
            priority,
            workItemType,
            teamId,
            assigneeId,
            sortBy,
            sortDirection,
            ct);
        return Ok(result);
    }

    /// <summary>
    /// Gets all work items assigned to a specific sprint (excluding Completed items).
    /// For sprint planning in Backlogs page.
    /// </summary>
    [HttpGet("sprint/{sprintId:int}")]
    [Authorize]
    public async Task<ActionResult<List<WorkItemDto>>> GetSprintWorkItems(
        [FromRoute] int sprintId,
        CancellationToken ct)
    {
        if (sprintId <= 0)
            return BadRequest(new { message = "SprintID must be greater than 0." });

        var workItems = await _repo.GetWorkItemsBySprintIdAsync(sprintId, ct);

        var filtered = workItems
            .Select(w => new WorkItemDto
            {
                WorkItemID = w.WorkItemID,
                Title = w.Title,
                Description = w.Description,
                Status = w.Status,
                Priority = w.Priority,
                DueDate = w.DueDate,
                AssignedUserID = w.AssignedUserID,
                AssignedUserName = w.AssignedUser != null ? $"{w.AssignedUser.FirstName} {w.AssignedUser.LastName}".Trim() : null,
                ParentWorkItemID = w.ParentWorkItemID,
                TeamID = w.TeamID,
                SprintID = w.SprintID,
                TypeName = w.WorkItemType != null ? w.WorkItemType.TypeName : null,
                CreatedAt = w.CreatedAt,
                UpdatedAt = w.UpdatedAt
            })
            .ToList();

        return Ok(filtered);
    }

    /// <summary>
    /// Assign a work item to a sprint. If the item is a Story, all child Tasks are also batch-assigned.
    /// </summary>
    [HttpPut("{id:int}/assign-sprint")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<IActionResult> AssignToSprint(
        [FromRoute] int id,
        [FromBody] AssignWorkItemToSprintRequestDto req,
        CancellationToken ct)
    {
        if (req.SprintID <= 0)
            return BadRequest(new { message = "SprintID is required." });

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        var itemInfo = await _repo.GetWorkItemTypeInfoByIdAsync(id, ct);
        if (itemInfo is null)
            return NotFound(new { message = "Work item not found." });

        if (itemInfo.Value.IsDeleted)
            return BadRequest(new { message = "Cannot assign a deleted work item." });

        var storyTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Story", ct);
        var taskTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Task", ct);

        if (storyTypeId is null || taskTypeId is null)
            return Problem("WorkItemTypes table is missing Story/Task entries.");

        // Stories cannot be assigned to sprints - only Tasks can be assigned
        if (itemInfo.Value.WorkItemTypeID == storyTypeId.Value)
        {
            return BadRequest(new { message = "Stories cannot be assigned to sprints. Only Tasks can be assigned to sprints." });
        }

        if (itemInfo.Value.WorkItemTypeID != taskTypeId.Value)
        {
            return BadRequest(new { message = "Only Task work items can be assigned to a sprint." });
        }

        var workItem = await _repo.GetTrackedByIdAsync(id, ct);
        if (workItem is null)
            return NotFound(new { message = "Work item not found." });

        if (workItem.Status == "Completed")
            return BadRequest(new { message = "Completed work items cannot be assigned to a sprint." });

        var sprint = await _repo.GetSprintByIdAsync(req.SprintID, ct);
        if (sprint is null)
            return BadRequest(new { message = "Sprint not found." });

        if (sprint.Status == "Completed")
            return BadRequest(new { message = "Cannot assign a work item to a completed sprint." });

        if (sprint.Status == "Active")
            return BadRequest(new { message = "Cannot add work items to an active sprint. Stop the sprint first to add items." });

        if (!CanManageSprint(userId.Value, sprint.ManagedBy))
            return Forbid();

        // Validate work item due date falls within sprint duration.
        if (workItem.DueDate.HasValue)
        {
            if (workItem.DueDate.Value < sprint.StartDate || workItem.DueDate.Value > sprint.EndDate)
            {
                return BadRequest(new
                {
                    message = $"Cannot assign work item to this sprint. The work item's due date ({workItem.DueDate.Value:yyyy-MM-dd}) must be within the sprint duration ({sprint.StartDate:yyyy-MM-dd} to {sprint.EndDate:yyyy-MM-dd})."
                });
            }
        }

        // ── Assign the Task to the sprint ──
        await _repo.AssignToSprintAsync(workItem, req.SprintID, ct);

        var notifications = new List<Notification>();

        // Notify assignee of the primary item
        if (workItem.AssignedUserID.HasValue && workItem.AssignedUserID.Value != userId.Value)
        {
            notifications.Add(new Notification
            {
                UserID = workItem.AssignedUserID.Value,
                RelatedWorkItemID = workItem.WorkItemID,
                RelatedSprintID = req.SprintID,
                NotificationType = "WorkItemAssignedToSprint",
                Message = $"Work item '{workItem.Title}' was added to sprint '{sprint.SprintName}'.",
                CreatedAt = DateTimeHelper.Now,
                IsRead = false
            });
        }

        // ── If this is a Story, batch-assign all child Tasks ──
        var childTaskIds = new List<int>();
        if (itemInfo.Value.WorkItemTypeID == storyTypeId.Value)
        {
            var childTasks = await _repo.GetChildTasksByParentIdAsync(workItem.WorkItemID, ct);

            foreach (var task in childTasks)
            {
                if (task.Status == "Completed")
                    continue; // Skip completed tasks

                // Track which tasks we're assigning for notifications
                childTaskIds.Add(task.WorkItemID);

                // We need a tracked entity to update
                var trackedTask = await _repo.GetTrackedByIdAsync(task.WorkItemID, ct);
                if (trackedTask is null) continue;

                await _repo.AssignToSprintAsync(trackedTask, req.SprintID, ct);

                // Notify task assignee
                if (trackedTask.AssignedUserID.HasValue && trackedTask.AssignedUserID.Value != userId.Value)
                {
                    notifications.Add(new Notification
                    {
                        UserID = trackedTask.AssignedUserID.Value,
                        RelatedWorkItemID = trackedTask.WorkItemID,
                        RelatedSprintID = req.SprintID,
                        NotificationType = "WorkItemAssignedToSprint",
                        Message = $"Task '{trackedTask.Title}' was added to sprint '{sprint.SprintName}' (parent story '{workItem.Title}' was assigned).",
                        CreatedAt = DateTimeHelper.Now,
                        IsRead = false
                    });
                }
            }
        }

        // ADDITIVE: Notify work item team members (excluding already notified users)
        if (workItem.TeamID.HasValue)
        {
            var teamMembers = await _repo.GetUsersByTeamIdAsync(workItem.TeamID.Value, ct);
            foreach (var tm in teamMembers)
            {
                if (tm == userId.Value) continue;
                if (notifications.Any(n => n.UserID == tm)) continue;

                notifications.Add(new Notification
                {
                    UserID = tm,
                    RelatedWorkItemID = workItem.WorkItemID,
                    RelatedSprintID = req.SprintID,
                    NotificationType = "WorkItemAssignedToSprint",
                    Message = $"Work item '{workItem.Title}' was added to sprint '{sprint.SprintName}'.",
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }
        }

        // ADDITIVE: Notify sprint team members when items are added to their sprint
        if (sprint.TeamID.HasValue)
        {
            var sprintTeamMembers = await _repo.GetUsersByTeamIdAsync(sprint.TeamID.Value, ct);
            foreach (var stm in sprintTeamMembers)
            {
                if (stm == userId.Value) continue;
                if (notifications.Any(n => n.UserID == stm)) continue;

                notifications.Add(new Notification
                {
                    UserID = stm,
                    RelatedWorkItemID = workItem.WorkItemID,
                    RelatedSprintID = req.SprintID,
                    NotificationType = "WorkItemAssignedToSprint",
                    Message = $"Work item '{workItem.Title}' was added to your sprint '{sprint.SprintName}'.",
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }
        }

        // ── Send all notifications ──
        if (notifications.Count > 0)
        {
            await _notifications.AddNotificationsAsync(notifications, ct);
        }

        // ── Broadcast to all clients for real-time updates ──
        await _hub.Clients.All.SendAsync("WorkItemAssignedToSprint", new
        {
            workItemID = workItem.WorkItemID,
            title = workItem.Title,
            status = workItem.Status,
            assignedUserID = workItem.AssignedUserID,
            sprintID = req.SprintID,
            sprintName = sprint.SprintName,
            childTaskIDs = childTaskIds,
            changedAt = DateTimeHelper.Now
        }, ct);

        return Ok(new
        {
            message = "Work item assigned to sprint successfully.",
            workItemID = workItem.WorkItemID,
            sprintID = req.SprintID,
            childTasksAssigned = childTaskIds.Count
        });
    }

    /// <summary>
    /// Remove a work item from a sprint. If the item is a Story, all child Tasks are also batch-removed.
    /// </summary>
    [HttpPut("{id:int}/remove-sprint")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<IActionResult> RemoveFromSprint([FromRoute] int id, CancellationToken ct)
    {
        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        var itemInfo = await _repo.GetWorkItemTypeInfoByIdAsync(id, ct);
        if (itemInfo is null)
            return NotFound(new { message = "Work item not found." });

        if (itemInfo.Value.IsDeleted)
            return BadRequest(new { message = "Cannot modify a deleted work item." });

        var storyTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Story", ct);
        var taskTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Task", ct);

        if (storyTypeId is null || taskTypeId is null)
            return Problem("WorkItemTypes table is missing Story/Task entries.");

        if (itemInfo.Value.WorkItemTypeID != storyTypeId.Value &&
            itemInfo.Value.WorkItemTypeID != taskTypeId.Value)
        {
            return BadRequest(new { message = "Only Story or Task work items can be removed from a sprint." });
        }

        var workItem = await _repo.GetTrackedByIdAsync(id, ct);
        if (workItem is null)
            return NotFound(new { message = "Work item not found." });

        if (!workItem.SprintID.HasValue)
            return BadRequest(new { message = "Work item is not currently assigned to a sprint." });

        var sprint = await _repo.GetSprintByIdAsync(workItem.SprintID.Value, ct);
        if (sprint is null)
            return BadRequest(new { message = "Sprint not found." });

        if (sprint.Status == "Active")
            return BadRequest(new { message = "Cannot remove work items from an active sprint. Stop the sprint first to remove items." });

        if (!CanManageSprint(userId.Value, sprint.ManagedBy))
            return Forbid();

        var oldSprintId = workItem.SprintID;
        await _repo.RemoveFromSprintAsync(workItem, ct);
        // Reset status to To-do when removed from sprint
        workItem.Status = "To-do";
        workItem.UpdatedAt = DateTimeHelper.Now;

        var notifications = new List<Notification>();

        // Notify assignee of the primary item
        if (workItem.AssignedUserID.HasValue && workItem.AssignedUserID.Value != userId.Value)
        {
            notifications.Add(new Notification
            {
                UserID = workItem.AssignedUserID.Value,
                RelatedWorkItemID = workItem.WorkItemID,
                RelatedSprintID = oldSprintId.Value,
                NotificationType = "WorkItemRemovedFromSprint",
                Message = $"Work item '{workItem.Title}' was removed from sprint '{sprint.SprintName}'.",
                CreatedAt = DateTimeHelper.Now,
                IsRead = false
            });
        }

        // ── If this is a Story, batch-remove all child Tasks ──
        var childTaskIds = new List<int>();
        if (itemInfo.Value.WorkItemTypeID == storyTypeId.Value)
        {
            var childTasks = await _repo.GetChildTasksByParentIdAsync(workItem.WorkItemID, ct);

            foreach (var task in childTasks)
            {
                if (task.SprintID != oldSprintId.Value)
                    continue; // Task is not in this sprint

                childTaskIds.Add(task.WorkItemID);

                var trackedTask = await _repo.GetTrackedByIdAsync(task.WorkItemID, ct);
                if (trackedTask is null) continue;

                await _repo.RemoveFromSprintAsync(trackedTask, ct);
                // Reset status to To-do when removed from sprint
                trackedTask.Status = "To-do";
                trackedTask.UpdatedAt = DateTimeHelper.Now;

                // Notify task assignee
                if (trackedTask.AssignedUserID.HasValue && trackedTask.AssignedUserID.Value != userId.Value)
                {
                    notifications.Add(new Notification
                    {
                        UserID = trackedTask.AssignedUserID.Value,
                        RelatedWorkItemID = trackedTask.WorkItemID,
                        RelatedSprintID = oldSprintId.Value,
                        NotificationType = "WorkItemRemovedFromSprint",
                        Message = $"Task '{trackedTask.Title}' was removed from sprint '{sprint.SprintName}' (parent story '{workItem.Title}' was removed).",
                        CreatedAt = DateTimeHelper.Now,
                        IsRead = false
                    });
                }
            }
        }

        // ADDITIVE: Notify work item team members (excluding already notified users)
        if (workItem.TeamID.HasValue)
        {
            var teamMembers = await _repo.GetUsersByTeamIdAsync(workItem.TeamID.Value, ct);
            foreach (var tm in teamMembers)
            {
                if (tm == userId.Value) continue;
                if (notifications.Any(n => n.UserID == tm)) continue;

                notifications.Add(new Notification
                {
                    UserID = tm,
                    RelatedWorkItemID = workItem.WorkItemID,
                    RelatedSprintID = oldSprintId.Value,
                    NotificationType = "WorkItemRemovedFromSprint",
                    Message = $"Work item '{workItem.Title}' was removed from sprint '{sprint.SprintName}'.",
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }
        }

        // ADDITIVE: Notify sprint team members when items are removed from their sprint
        if (sprint.TeamID.HasValue)
        {
            var sprintTeamMembers = await _repo.GetUsersByTeamIdAsync(sprint.TeamID.Value, ct);
            foreach (var stm in sprintTeamMembers)
            {
                if (stm == userId.Value) continue;
                if (notifications.Any(n => n.UserID == stm)) continue;

                notifications.Add(new Notification
                {
                    UserID = stm,
                    RelatedWorkItemID = workItem.WorkItemID,
                    RelatedSprintID = oldSprintId.Value,
                    NotificationType = "WorkItemRemovedFromSprint",
                    Message = $"Work item '{workItem.Title}' was removed from your sprint '{sprint.SprintName}'.",
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }
        }

        // ── Audit log ──
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await _audit.LogAsync(
            userId.Value,
            "WorkItem.RemoveFromSprint",
            "WorkItem",
            workItem.WorkItemID,
            true,
            $"Removed WorkItemID={workItem.WorkItemID} from SprintID={oldSprintId}. Child tasks removed: {childTaskIds.Count}",
            ip,
            ct
        );

        // ── Send all notifications ──
        if (notifications.Count > 0)
        {
            await _notifications.AddNotificationsAsync(notifications, ct);
        }

        // ── Broadcast to all clients ──
        await _hub.Clients.All.SendAsync("WorkItemRemovedFromSprint", new
        {
            workItemID = workItem.WorkItemID,
            title = workItem.Title,
            oldSprintID = oldSprintId.Value,
            oldSprintName = sprint.SprintName,
            childTaskIDs = childTaskIds,
            changedAt = DateTimeHelper.Now
        }, ct);

        return Ok(new
        {
            message = "Work item removed from sprint successfully.",
            workItemID = workItem.WorkItemID,
            childTasksRemoved = childTaskIds.Count
        });
    }

    [HttpPut("{id:int}/status")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<IActionResult> UpdateStatus(
        [FromRoute] int id,
        [FromBody] UpdateWorkItemStatusRequestDto req,
        CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Status))
            return BadRequest(new { message = "Status is required." });

        var newStatus = NormalizeStatus(req.Status);
        if (newStatus is null)
            return BadRequest(new { message = "Invalid Status. Allowed: To-do, Ongoing, For Checking, Completed." });

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        var itemInfo = await _repo.GetWorkItemTypeInfoByIdAsync(id, ct);
        if (itemInfo is null)
            return NotFound(new { message = "Work item not found." });

        if (itemInfo.Value.IsDeleted)
            return BadRequest(new { message = "Cannot modify a deleted work item." });

        var workItem = await _repo.GetTrackedByIdAsync(id, ct);
        if (workItem is null)
            return NotFound(new { message = "Work item not found." });

        if (!CanManageWorkItemWithTeam(userId.Value, workItem.AssignedUserID, workItem.TeamID))
        {
            if (!workItem.SprintID.HasValue)
                return Forbid();

            var sprint = await _repo.GetSprintByIdAsync(workItem.SprintID.Value, ct);
            if (sprint is null || !CanManageSprint(userId.Value, sprint.ManagedBy))
                return Forbid();
        }

        var oldStatus = workItem.Status;
        if (string.Equals(oldStatus, newStatus, StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                message = "Work item status is already set to that value.",
                workItemID = workItem.WorkItemID,
                status = workItem.Status
            });
        }

        workItem.Status = newStatus;
        workItem.UpdatedAt = DateTimeHelper.Now;

        await _repo.SaveChangesAsync(ct);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        await _audit.LogAsync(
            userId.Value,
            "WorkItem.StatusChange",
            "WorkItem",
            workItem.WorkItemID,
            true,
            $"Changed WorkItemID={workItem.WorkItemID} status from {oldStatus} to {newStatus}",
            ip,
            ct
        );

        // ADDITIVE: Notify team members of status change (excluding actor and assignee)
        var statusChangeNotifications = new List<Notification>();
        if (workItem.AssignedUserID.HasValue && workItem.AssignedUserID.Value != userId.Value)
        {
            statusChangeNotifications.Add(new Notification
            {
                UserID = workItem.AssignedUserID.Value,
                NotificationType = "WorkItemStatusChanged",
                Message = $"Work item '{workItem.Title}' status changed from '{oldStatus}' to '{newStatus}'.",
                RelatedWorkItemID = workItem.WorkItemID,
                RelatedSprintID = workItem.SprintID,
                CreatedAt = DateTimeHelper.Now,
                IsRead = false
            });
        }
        if (workItem.TeamID.HasValue)
        {
            var teamMembers = await _repo.GetUsersByTeamIdAsync(workItem.TeamID.Value, ct);
            foreach (var tm in teamMembers)
            {
                if (tm == userId.Value) continue;
                if (workItem.AssignedUserID.HasValue && tm == workItem.AssignedUserID.Value) continue;
                if (statusChangeNotifications.Any(n => n.UserID == tm)) continue;

                statusChangeNotifications.Add(new Notification
                {
                    UserID = tm,
                    NotificationType = "WorkItemStatusChanged",
                    Message = $"Work item '{workItem.Title}' status changed from '{oldStatus}' to '{newStatus}'.",
                    RelatedWorkItemID = workItem.WorkItemID,
                    RelatedSprintID = workItem.SprintID,
                    CreatedAt = DateTimeHelper.Now,
                    IsRead = false
                });
            }
        }
        if (statusChangeNotifications.Count > 0)
            await _notifications.AddNotificationsAsync(statusChangeNotifications, ct);

        // Broadcast status change to sprint group for real-time board update
        if (workItem.SprintID.HasValue)
        {
            await _hub.Clients.Group($"sprint-{workItem.SprintID.Value}").SendAsync("WorkItemStatusChanged", new
            {
                workItemID = workItem.WorkItemID,
                title = workItem.Title,
                oldStatus,
                newStatus = workItem.Status,
                sprintID = workItem.SprintID.Value,
                changedAt = workItem.UpdatedAt
            }, ct);
        }

        return Ok(new
        {
            message = "Work item status updated successfully.",
            workItemID = workItem.WorkItemID,
            oldStatus,
            status = workItem.Status
        });
    }

    [HttpPatch("{id:int}")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth")]
    public async Task<IActionResult> Patch(
        [FromRoute] int id,
        [FromBody] UpdateWorkItemRequestDto req,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (id <= 0)
            return BadRequest(new { message = "WorkItemID must be greater than 0." });

        if (req is null)
            return BadRequest(new { message = "Request body is required." });

        var hasAnyPatchField =
            req.Title is not null ||
            req.Description is not null ||
            req.Priority is not null ||
            req.DueDate is not null ||
            req.ParentWorkItemID.HasValue ||
            req.TeamID.HasValue ||
            req.AssignedUserID.HasValue ||
            req.ClearAssignee == true;

        if (!hasAnyPatchField)
            return BadRequest(new { message = "At least one field must be provided." });

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        var itemInfo = await _repo.GetWorkItemTypeInfoByIdAsync(id, ct);
        if (itemInfo is null)
            return NotFound(new { message = "Work item not found." });

        if (itemInfo.Value.IsDeleted)
            return BadRequest(new { message = "Cannot modify a deleted work item." });

        var workItem = await _repo.GetTrackedByIdAsync(id, ct);
        if (workItem is null)
            return NotFound(new { message = "Work item not found." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var isElevated = IsElevatedWorkItemRole();

        // Determine if user is the Sprint Owner of the sprint this item belongs to
        var isSprintOwner = false;
        if (workItem.SprintID.HasValue)
        {
            var sprint = await _repo.GetSprintByIdAsync(workItem.SprintID.Value, ct);
            if (sprint is not null && sprint.ManagedBy.HasValue && sprint.ManagedBy.Value == userId.Value)
                isSprintOwner = true;
        }

        // ── Restricted fields (assignee/team): Admin/Scrum Master ONLY, but Sprint Owner can change assignee ──
        if (req.TeamID.HasValue && !isElevated)
        {
            await _audit.LogAsync(
                userId.Value,
                "WorkItem.Update",
                "WorkItem",
                workItem.WorkItemID,
                false,
                $"Unauthorized: non-elevated user attempted to change team for WorkItemID={workItem.WorkItemID}",
                ip,
                ct);

            return StatusCode(403, new { message = "Only administrators and scrum masters can change team." });
        }

        // Assignee changes: Admin/SM always allowed; Sprint Owner allowed only for items in their sprint
        // Only reject if the assignee is actually being CHANGED, not if the same value is sent back
        var isAssigneeChanging = req.ClearAssignee == true
            ? workItem.AssignedUserID.HasValue  // clearing when there was an assignee
            : req.AssignedUserID.HasValue && workItem.AssignedUserID != req.AssignedUserID;  // changing to different user

        if (isAssigneeChanging && !isElevated && !isSprintOwner)
        {
            await _audit.LogAsync(
                userId.Value,
                "WorkItem.Update",
                "WorkItem",
                workItem.WorkItemID,
                false,
                $"Unauthorized: non-elevated user attempted to change assignee for WorkItemID={workItem.WorkItemID}",
                ip,
                ct);

            return StatusCode(403, new { message = "Only administrators, scrum masters, or the sprint owner can change assignee." });
        }

        // ── Priority changes: Admin/Scrum Master ONLY ──
        // Only reject if the priority is actually being CHANGED, not if the same value is sent back
        var normalizedPriority = req.Priority is not null ? NormalizePriority(req.Priority) : null;
        if (req.Priority is not null && !isElevated && !string.Equals(workItem.Priority, normalizedPriority, StringComparison.OrdinalIgnoreCase))
        {
            await _audit.LogAsync(
                userId.Value,
                "WorkItem.Update",
                "WorkItem",
                workItem.WorkItemID,
                false,
                $"Unauthorized: non-elevated user attempted to change priority for WorkItemID={workItem.WorkItemID}",
                ip,
                ct);

            return StatusCode(403, new { message = "Only administrators and scrum masters can change priority." });
        }

        // ── Authorization: who can update? ──
        // 1. Admin/Scrum Master: full access (all fields)
        // 2. Sprint Manager: can change assignee only for items in their sprint
        // 3. Team member: can change assignee only for items in their team (ADDITIVE)
        var isOwner = workItem.AssignedUserID.HasValue && workItem.AssignedUserID.Value == userId.Value;
        var isTeamMember = workItem.TeamID.HasValue && GetUserTeamId() == workItem.TeamID.Value;
        if (!isElevated && !isSprintOwner && !isTeamMember)
        {
            await _audit.LogAsync(
                userId.Value,
                "WorkItem.Update",
                "WorkItem",
                workItem.WorkItemID,
                false,
                $"Unauthorized update attempt for WorkItemID={workItem.WorkItemID}",
                ip,
                ct);

            return Forbid();
        }

        var epicTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Epic", ct);
        var storyTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Story", ct);
        var taskTypeId = await _repo.GetWorkItemTypeIdByNameAsync("Task", ct);

        if (epicTypeId is null || storyTypeId is null || taskTypeId is null)
            return Problem("WorkItemTypes table is missing Epic/Story/Task entries.");

        // ── Field-level restrictions for Sprint Manager ──
        // Sprint Manager can ONLY change assignee — not title, description, priority, team, parent, or dueDate
        if (isSprintOwner && !isElevated)
        {
            var hasNonAssigneeChange =
                req.Title is not null ||
                req.Description is not null ||
                req.Priority is not null ||
                req.TeamID.HasValue ||
                req.ParentWorkItemID.HasValue ||
                req.DueDate is not null;

            if (hasNonAssigneeChange)
            {
                await _audit.LogAsync(
                    userId.Value,
                    "WorkItem.Update",
                    "WorkItem",
                    workItem.WorkItemID,
                    false,
                    $"Unauthorized: sprint manager attempted to edit non-assignee fields for WorkItemID={workItem.WorkItemID}",
                    ip,
                    ct);

                return StatusCode(403, new { message = "Sprint managers can only change the assignee of work items. Administrators and scrum masters are required to edit other fields." });
            }
        }

        // ── Field-level restrictions for Team Members ──
        // Team members can ONLY change assignee — same restrictions as sprint managers
        if (isTeamMember && !isElevated && !isSprintOwner)
        {
            var hasNonAssigneeChange =
                req.Title is not null ||
                req.Description is not null ||
                req.Priority is not null ||
                req.TeamID.HasValue ||
                req.ParentWorkItemID.HasValue ||
                req.DueDate is not null;

            if (hasNonAssigneeChange)
            {
                await _audit.LogAsync(
                    userId.Value,
                    "WorkItem.Update",
                    "WorkItem",
                    workItem.WorkItemID,
                    false,
                    $"Unauthorized: team member attempted to edit non-assignee fields for WorkItemID={workItem.WorkItemID}",
                    ip,
                    ct);

                return StatusCode(403, new { message = "Team members can only change the assignee of work items. Administrators and scrum masters are required to edit other fields." });
            }
        }

        // ── Active Sprint Restrictions ──
        // When a work item belongs to an active sprint, only the due date can be edited
        // All other fields are locked (title, description, priority, assignee, team, parent)
        // This applies to ALL users including Admin/Scrum Master
        if (workItem.SprintID.HasValue)
        {
            var sprint = await _repo.GetSprintByIdAsync(workItem.SprintID.Value, ct);
            if (sprint != null && sprint.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                var isAssigneeChangingInActiveSprint = req.ClearAssignee == true
                    ? workItem.AssignedUserID.HasValue
                    : req.AssignedUserID.HasValue && workItem.AssignedUserID != req.AssignedUserID;

                // Active sprint: only Admin/Scrum Master/Sprint Manager can change assignee.
                if (isAssigneeChangingInActiveSprint && !isElevated && !isSprintOwner)
                {
                    await _audit.LogAsync(
                        userId.Value,
                        "WorkItem.Update",
                        "WorkItem",
                        workItem.WorkItemID,
                        false,
                        $"Unauthorized: attempted assignee change for WorkItemID={workItem.WorkItemID} in active sprint",
                        ip,
                        ct);

                    return StatusCode(403, new { message = "Only administrators, scrum masters, or the sprint manager can change assignee while the sprint is active." });
                }

                // Active sprint: if sprint has a real team, assignee must belong to that sprint team.
                if (isAssigneeChangingInActiveSprint &&
                    req.AssignedUserID.HasValue &&
                    sprint.TeamID.HasValue &&
                    sprint.TeamID.Value != Utilities.SystemDefaults.DefaultTeamId)
                {
                    var sprintTeamUserIds = await _repo.GetUsersByTeamIdAsync(sprint.TeamID.Value, ct);
                    if (!sprintTeamUserIds.Contains(req.AssignedUserID.Value))
                    {
                        return BadRequest(new
                        {
                            message = "Assigned user must belong to the sprint team while the sprint is active."
                        });
                    }
                }

                // Check if user is trying to modify locked fields (everything except DueDate)
                var hasLockedFieldChange =
                    req.Title is not null ||
                    req.Description is not null ||
                    req.Priority is not null ||
                    req.TeamID.HasValue ||
                    req.ParentWorkItemID.HasValue;

                if (hasLockedFieldChange)
                {
                    await _audit.LogAsync(
                        userId.Value,
                        "WorkItem.Update",
                        "WorkItem",
                        workItem.WorkItemID,
                        false,
                        $"Unauthorized: attempted to edit locked fields for WorkItemID={workItem.WorkItemID} in active sprint",
                        ip,
                        ct);

                    return StatusCode(403, new { message = "Work items in an active sprint can only have their due date modified. All other fields are locked." });
                }

                // Validate due date is not later than sprint end date
                if (req.DueDate.HasValue)
                {
                    if (req.DueDate.Value > sprint.EndDate)
                    {
                        return BadRequest(new { message = $"Due date cannot be later than the sprint end date ({sprint.EndDate:yyyy-MM-dd})." });
                    }
                }
            }
        }

        var histories = new List<WorkItemHistory>();
        var changedFields = new List<string>();
        var oldAssignedUserId = workItem.AssignedUserID;
        var now = DateTimeHelper.Now;

        if (req.Title is not null)
        {
            var newTitle = req.Title.Trim();
            if (newTitle.Length == 0)
                return BadRequest(new { message = "Title cannot be empty." });

            if (!string.Equals(workItem.Title, newTitle, StringComparison.Ordinal))
            {
                histories.Add(BuildHistory(workItem.WorkItemID, "Title", workItem.Title, newTitle, userId.Value, now));
                changedFields.Add($"Title:{workItem.Title}->{newTitle}");
                workItem.Title = newTitle;
            }
        }

        if (req.Description is not null)
        {
            var newDescription = req.Description.Trim();
            if (newDescription.Length == 0)
                return BadRequest(new { message = "Description cannot be empty." });

            if (!string.Equals(workItem.Description ?? "", newDescription, StringComparison.Ordinal))
            {
                histories.Add(BuildHistory(workItem.WorkItemID, "Description", workItem.Description, newDescription, userId.Value, now));
                changedFields.Add("Description:updated");
                workItem.Description = newDescription;
            }
        }

        if (req.Priority is not null)
        {
            var newPriority = NormalizePriority(req.Priority);
            if (newPriority is null)
                return BadRequest(new { message = "Invalid Priority. Allowed: Low, Medium, High, Critical." });

            if (!string.Equals(workItem.Priority, newPriority, StringComparison.Ordinal))
            {
                histories.Add(BuildHistory(workItem.WorkItemID, "Priority", workItem.Priority, newPriority, userId.Value, now));
                changedFields.Add($"Priority:{workItem.Priority}->{newPriority}");
                workItem.Priority = newPriority;
            }
        }

        if (req.TeamID.HasValue)
        {
            if (workItem.WorkItemTypeID == epicTypeId.Value)
                return BadRequest(new { message = "Epic cannot be assigned to a team." });

            var teamExists = await _repo.TeamExistsAsync(req.TeamID.Value, ct);
            if (!teamExists)
                return BadRequest(new { message = "Team not found." });

            if (workItem.TeamID != req.TeamID.Value)
            {
                histories.Add(BuildHistory(workItem.WorkItemID, "TeamID", workItem.TeamID?.ToString(), req.TeamID.Value.ToString(), userId.Value, now));
                changedFields.Add($"TeamID:{workItem.TeamID}->{req.TeamID.Value}");
                workItem.TeamID = req.TeamID.Value;
            }
        }

        if (req.AssignedUserID.HasValue)
        {
            var userExists = await _repo.UserExistsAsync(req.AssignedUserID.Value, ct);
            if (!userExists)
                return BadRequest(new { message = "Assigned user not found." });

            if (workItem.AssignedUserID != req.AssignedUserID.Value)
            {
                histories.Add(BuildHistory(workItem.WorkItemID, "AssignedUserID", workItem.AssignedUserID?.ToString(), req.AssignedUserID.Value.ToString(), userId.Value, now));
                changedFields.Add($"AssignedUserID:{workItem.AssignedUserID}->{req.AssignedUserID.Value}");
                workItem.AssignedUserID = req.AssignedUserID.Value;
            }
        }
        else if (req.ClearAssignee == true)
        {
            // Explicitly clear the assignee
            if (workItem.AssignedUserID.HasValue)
            {
                histories.Add(BuildHistory(workItem.WorkItemID, "AssignedUserID", workItem.AssignedUserID.Value.ToString(), null, userId.Value, now));
                changedFields.Add($"AssignedUserID:{workItem.AssignedUserID}->null");
                workItem.AssignedUserID = null;
            }
        }

        if (req.ParentWorkItemID.HasValue)
        {
            if (req.ParentWorkItemID.Value == workItem.WorkItemID)
                return BadRequest(new { message = "A work item cannot be its own parent." });

            if (workItem.WorkItemTypeID == epicTypeId.Value)
                return BadRequest(new { message = "Epic cannot have a parent." });

            var parentInfo = await _repo.GetWorkItemTypeInfoByIdAsync(req.ParentWorkItemID.Value, ct);
            if (parentInfo is null)
                return BadRequest(new { message = "Parent work item not found." });

            if (parentInfo.Value.IsDeleted)
                return BadRequest(new { message = "Cannot assign a deleted parent work item." });

            if (workItem.WorkItemTypeID == storyTypeId.Value &&
                parentInfo.Value.WorkItemTypeID != epicTypeId.Value)
            {
                return BadRequest(new { message = "Story parent must be an Epic." });
            }

            if (workItem.WorkItemTypeID == taskTypeId.Value &&
                parentInfo.Value.WorkItemTypeID != epicTypeId.Value &&
                parentInfo.Value.WorkItemTypeID != storyTypeId.Value)
            {
                return BadRequest(new { message = "Task parent must be an Epic or Story." });
            }

            if (workItem.ParentWorkItemID != req.ParentWorkItemID.Value)
            {
                histories.Add(BuildHistory(workItem.WorkItemID, "ParentWorkItemID", workItem.ParentWorkItemID?.ToString(), req.ParentWorkItemID.Value.ToString(), userId.Value, now));
                changedFields.Add($"ParentWorkItemID:{workItem.ParentWorkItemID}->{req.ParentWorkItemID.Value}");
                workItem.ParentWorkItemID = req.ParentWorkItemID.Value;
            }
        }

        if (req.DueDate.HasValue || (workItem.DueDate.HasValue && req.DueDate is not null))
        {
            if (workItem.DueDate != req.DueDate)
            {
                histories.Add(BuildHistory(workItem.WorkItemID, "DueDate", workItem.DueDate?.ToString(), req.DueDate?.ToString(), userId.Value, now));
                changedFields.Add($"DueDate:{workItem.DueDate}->{req.DueDate}");
                workItem.DueDate = req.DueDate;
            }
        }

        if (changedFields.Count == 0)
        {
            return Ok(new
            {
                message = "No changes were applied.",
                workItemID = workItem.WorkItemID
            });
        }

        workItem.UpdatedAt = now;

        foreach (var history in histories)
            await _repo.AddHistoryAsync(history, ct);

        var notifications = new List<Notification>();

        // If assignee changed, notify the new assignee AND the old assignee
        if (oldAssignedUserId != workItem.AssignedUserID && workItem.AssignedUserID.HasValue && workItem.AssignedUserID.Value != userId.Value)
        {
            notifications.Add(new Notification
            {
                UserID = workItem.AssignedUserID.Value,
                Message = $"You were assigned to work item '{workItem.Title}'.",
                NotificationType = "WorkItemAssigned",
                RelatedWorkItemID = workItem.WorkItemID,
                RelatedSprintID = workItem.SprintID,
                CreatedAt = now,
                IsRead = false
            });
        }

        // Notify old assignee that they were removed
        if (oldAssignedUserId.HasValue && oldAssignedUserId.Value != userId.Value && oldAssignedUserId != workItem.AssignedUserID)
        {
            notifications.Add(new Notification
            {
                UserID = oldAssignedUserId.Value,
                Message = $"You were removed from work item '{workItem.Title}'.",
                NotificationType = "WorkItemUnassigned",
                RelatedWorkItemID = workItem.WorkItemID,
                RelatedSprintID = workItem.SprintID,
                CreatedAt = now,
                IsRead = false
            });
        }

        // Notify the work item's assignee that details changed (if not already notified above)
        if (workItem.AssignedUserID.HasValue && workItem.AssignedUserID.Value != userId.Value)
        {
            var alreadyNotified = notifications.Any(n => n.UserID == workItem.AssignedUserID.Value);
            if (!alreadyNotified)
            {
                notifications.Add(new Notification
                {
                    UserID = workItem.AssignedUserID.Value,
                    Message = $"Work item '{workItem.Title}' was updated.",
                    NotificationType = "WorkItemUpdated",
                    RelatedWorkItemID = workItem.WorkItemID,
                    RelatedSprintID = workItem.SprintID,
                    CreatedAt = now,
                    IsRead = false
                });
            }
        }

        // If team is set on the work item, notify all team members (ADDITIVE — always notify on any patch)
        if (workItem.TeamID.HasValue)
        {
            var teamMembers = await _repo.GetUsersByTeamIdAsync(workItem.TeamID.Value, ct);
            foreach (var tm in teamMembers)
            {
                if (tm != userId.Value && !notifications.Any(n => n.UserID == tm))
                {
                    notifications.Add(new Notification
                    {
                        UserID = tm,
                        Message = $"Work item '{workItem.Title}' was updated.",
                        NotificationType = "WorkItemUpdated",
                        RelatedWorkItemID = workItem.WorkItemID,
                        RelatedSprintID = workItem.SprintID,
                        CreatedAt = now,
                        IsRead = false
                    });
                }
            }
        }

        var patchNotifications = notifications
            .GroupBy(x => new { x.UserID, x.NotificationType, x.Message, x.RelatedWorkItemID, x.RelatedSprintID })
            .Select(g => g.First())
            .ToList();

        if (patchNotifications.Count > 0)
            await _notifications.AddNotificationsAsync(patchNotifications, ct);
        else
            await _repo.SaveChangesAsync(ct);

        await _audit.LogAsync(
            userId.Value,
            "WorkItem.Update",
            "WorkItem",
            workItem.WorkItemID,
            true,
            $"Updated WorkItemID={workItem.WorkItemID}; Changes={string.Join("; ", changedFields)}",
            ip,
            ct);

        // Broadcast update to sprint group for real-time board update
        if (workItem.SprintID.HasValue)
        {
            await _hub.Clients.Group($"sprint-{workItem.SprintID.Value}").SendAsync("WorkItemUpdated", new
            {
                workItemID = workItem.WorkItemID,
                title = workItem.Title,
                status = workItem.Status,
                assignedUserID = workItem.AssignedUserID,
                priority = workItem.Priority,
                sprintID = workItem.SprintID.Value,
                changedFields,
                changedAt = workItem.UpdatedAt
            }, ct);
        }
        else
        {
            // For backlog items, broadcast to all (for backlog view updates)
            await _hub.Clients.All.SendAsync("WorkItemUpdated", new
            {
                workItemID = workItem.WorkItemID,
                title = workItem.Title,
                status = workItem.Status,
                assignedUserID = workItem.AssignedUserID,
                priority = workItem.Priority,
                sprintID = (int?)null,
                changedFields,
                changedAt = workItem.UpdatedAt
            }, ct);
        }

        return Ok(new
        {
            message = "Work item updated successfully.",
            workItemID = workItem.WorkItemID
        });
    }

    [HttpDelete("{id:int}")]
    [Authorize(AuthenticationSchemes = "MyCookieAuth", Roles = "Administrator,Scrum Master,ScrumMaster")]
    public async Task<IActionResult> SoftDelete([FromRoute] int id, CancellationToken ct)
    {
        if (id <= 0)
            return BadRequest(new { message = "WorkItemID must be greater than 0." });

        var userId = TryGetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Missing/invalid user identity." });

        var itemInfo = await _repo.GetWorkItemTypeInfoByIdAsync(id, ct);
        if (itemInfo is null)
            return NotFound(new { message = "Work item not found." });

        if (itemInfo.Value.IsDeleted)
            return BadRequest(new { message = "Work item is already deleted." });

        var workItem = await _repo.GetTrackedByIdAsync(id, ct);
        if (workItem is null)
            return NotFound(new { message = "Work item not found." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var canDelete = CanManageWorkItemWithTeam(userId.Value, workItem.AssignedUserID, workItem.TeamID);
        if (!canDelete && workItem.SprintID.HasValue)
        {
            var sprint = await _repo.GetSprintByIdAsync(workItem.SprintID.Value, ct);
            canDelete = sprint is not null && CanManageSprint(userId.Value, sprint.ManagedBy);
        }

        if (!canDelete)
        {
            await _audit.LogAsync(
                userId.Value,
                "WorkItem.Delete",
                "WorkItem",
                workItem.WorkItemID,
                false,
                $"Unauthorized delete attempt for WorkItemID={workItem.WorkItemID}",
                ip,
                ct);

            return Forbid();
        }

        var now = DateTimeHelper.Now;
        var descendantIds = await _repo.GetActiveDescendantIdsAsync(workItem.WorkItemID, ct);
        var allIdsToDelete = new List<int> { workItem.WorkItemID };
        allIdsToDelete.AddRange(descendantIds);

        var itemsToDelete = new List<WorkItem>();
        foreach (var wid in allIdsToDelete.Distinct())
        {
            var tracked = await _repo.GetTrackedByIdAsync(wid, ct);
            if (tracked is not null && !tracked.IsDeleted)
                itemsToDelete.Add(tracked);
        }

        var archiveNotifications = new List<Notification>();
        var deletedEventPayloads = new List<(int WorkItemID, string Title, int? SprintID)>();

        foreach (var itemToDelete in itemsToDelete)
        {
            var previousSprintId = itemToDelete.SprintID;
            var previousAssignedUserId = itemToDelete.AssignedUserID;

            int? itemSprintManagerId = null;
            if (previousSprintId.HasValue)
                itemSprintManagerId = await _repo.GetSprintManagerUserIdAsync(previousSprintId.Value, ct);

            if (previousSprintId.HasValue)
            {
                itemToDelete.SprintID = null;
                await _repo.AddHistoryAsync(
                    BuildHistory(itemToDelete.WorkItemID, "SprintID", previousSprintId.Value.ToString(), null, userId.Value, now),
                    ct);
            }

            if (previousAssignedUserId.HasValue)
            {
                itemToDelete.AssignedUserID = null;
                await _repo.AddHistoryAsync(
                    BuildHistory(itemToDelete.WorkItemID, "AssignedUserID", previousAssignedUserId.Value.ToString(), null, userId.Value, now),
                    ct);
            }

            itemToDelete.IsDeleted = true;
            itemToDelete.DeletedAt = now;
            itemToDelete.UpdatedAt = now;

            await _repo.AddHistoryAsync(
                BuildHistory(itemToDelete.WorkItemID, "IsDeleted", "false", "true", userId.Value, now),
                ct);

            var relatedUserIds = BuildRelatedUserIds(
                itemToDelete,
                itemSprintManagerId,
                userId.Value,
                null);

            archiveNotifications.AddRange(
                relatedUserIds.Select(targetUserId => new Notification
                {
                    UserID = targetUserId,
                    Message = $"Work item '{itemToDelete.Title}' was archived.",
                    NotificationType = "WorkItemArchived",
                    RelatedWorkItemID = itemToDelete.WorkItemID,
                    RelatedSprintID = previousSprintId,
                    CreatedAt = now,
                    IsRead = false
                }));

            deletedEventPayloads.Add((itemToDelete.WorkItemID, itemToDelete.Title ?? "", previousSprintId));
        }

        if (archiveNotifications.Count > 0)
            await _notifications.AddNotificationsAsync(archiveNotifications, ct);
        else
            await _repo.SaveChangesAsync(ct);

        await _audit.LogAsync(
            userId.Value,
            "WorkItem.Delete",
            "WorkItem",
            workItem.WorkItemID,
            true,
            $"Soft deleted WorkItemID={workItem.WorkItemID}; Title={workItem.Title}",
            ip,
            ct);

        // Broadcast delete to all connected clients for real-time UI update
        foreach (var payload in deletedEventPayloads)
        {
            await _hub.Clients.All.SendAsync("WorkItemDeleted", new
            {
                workItemID = payload.WorkItemID,
                title = payload.Title,
                sprintID = payload.SprintID,
                deletedAt = now
            }, ct);
        }

        return Ok(new
        {
            message = "Work item archived successfully.",
            workItemID = workItem.WorkItemID,
            deletedCount = itemsToDelete.Count
        });
    }

    private bool CanManageSprint(int userId, int? sprintManagedByUserId)
    {
        if (User.IsInRole("Administrator") || User.IsInRole("Scrum Master") || User.IsInRole("ScrumMaster"))
            return true;

        return sprintManagedByUserId.HasValue && sprintManagedByUserId.Value == userId;
    }

    private bool CanManageWorkItem(int userId, int? assignedUserId)
    {
        if (User.IsInRole("Administrator") || User.IsInRole("Scrum Master") || User.IsInRole("ScrumMaster"))
            return true;

        return assignedUserId.HasValue && assignedUserId.Value == userId;
    }

    /// <summary>
    /// Extended check: returns true if user can manage the work item based on
    /// existing rules (role / assignee) OR if they belong to the same team.
    /// This is an ADDITIVE check — existing rules are not changed.
    /// </summary>
    private bool CanManageWorkItemWithTeam(int userId, int? assignedUserId, int? workItemTeamId)
    {
        // Existing rules first
        if (CanManageWorkItem(userId, assignedUserId))
            return true;

        // ADDITIVE: team members can manage work items in their team
        return workItemTeamId.HasValue && GetUserTeamId() == workItemTeamId.Value;
    }

    /// <summary>
    /// Returns the TeamID of the currently authenticated user from their JWT claims.
    /// Returns null if the claim is missing or invalid (user may not be on a team).
    /// </summary>
    private int? GetUserTeamId()
    {
        var teamClaim = User.FindFirst("TeamID");
        if (teamClaim is null) return null;
        return int.TryParse(teamClaim.Value, out var tid) ? tid : null;
    }

    private bool IsElevatedWorkItemRole()
    {
        return User.IsInRole("Administrator") ||
               User.IsInRole("Scrum Master") ||
               User.IsInRole("ScrumMaster");
    }

    private static WorkItemHistory BuildHistory(
        int workItemId,
        string fieldChanged,
        string? oldValue,
        string? newValue,
        int changedBy,
        DateTime changedAt)
    {
        return new WorkItemHistory
        {
            WorkItemID = workItemId,
            FieldChanged = fieldChanged,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = changedAt,
            ChangedBy = changedBy
        };
    }

    private static List<int> BuildRelatedUserIds(
        WorkItem workItem,
        int? sprintManagerId,
        int actorUserId,
        int? oldAssignedUserId)
    {
        var ids = new HashSet<int>();

        if (workItem.AssignedUserID.HasValue)
            ids.Add(workItem.AssignedUserID.Value);

        if (oldAssignedUserId.HasValue)
            ids.Add(oldAssignedUserId.Value);

        if (workItem.CreatedByUserID > 0)
            ids.Add(workItem.CreatedByUserID);

        if (sprintManagerId.HasValue)
            ids.Add(sprintManagerId.Value);

        ids.Remove(actorUserId);

        return ids.ToList();
    }

    private static int? TryGetUserId(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var parsed) ? parsed : null;
    }

    private static string? NormalizeType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var t = raw.Trim().ToLowerInvariant();
        return t switch
        {
            "epic" => "Epic",
            "story" => "Story",
            "task" => "Task",
            _ => null
        };
    }

    private static string? NormalizeStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "to-do" => "To-do",
            "todo" => "To-do",
            "ongoing" => "Ongoing",
            "for checking" => "For Checking",
            "for-checking" => "For Checking",
            "forchecking" => "For Checking",
            "completed" => "Completed",
            _ => null
        };
    }

    private static string? NormalizePriority(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var p = raw.Trim().ToLowerInvariant();
        return p switch
        {
            "low" => "Low",
            "medium" => "Medium",
            "high" => "High",
            "critical" => "Critical",
            _ => null
        };
    }
}