namespace DigitalScrumBoard1.DTOs.WorkItems;

/// <summary>
/// Represents a work item within an epic hierarchy, including nested children.
/// Used for the View Epic modal to display Epic → Stories → Tasks structure.
/// </summary>
public sealed class WorkItemHierarchyDto
{
    public int WorkItemID { get; set; }
    public string TypeName { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "";
    public string? Priority { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? AssignedUserID { get; set; }
    public string? AssignedUserName { get; set; }
    public int? ParentWorkItemID { get; set; }
    public int? TeamID { get; set; }
    public string? TeamName { get; set; }
    public int? SprintID { get; set; }
    public string? SprintName { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<WorkItemHierarchyDto> Children { get; set; } = new();
}
