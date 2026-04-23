namespace DigitalScrumBoard1.DTOs.SignalR;

/// <summary>
/// Complete work item data for real-time board updates.
/// Allows frontend to update UI without refetching.
/// </summary>
public sealed class WorkItemBroadcastDto
{
    public int WorkItemID { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Priority { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? AssignedUserID { get; set; }
    public string? AssignedUserName { get; set; }
    public int WorkItemTypeID { get; set; }
    public string WorkItemType { get; set; } = string.Empty;
    public int? ParentWorkItemID { get; set; }
    public int? TeamID { get; set; }
    public int? SprintID { get; set; }
    public int BoardOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Minimal work item reference for notifications.
/// </summary>
public sealed class WorkItemReferenceDto
{
    public int WorkItemID { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? SprintID { get; set; }
}
