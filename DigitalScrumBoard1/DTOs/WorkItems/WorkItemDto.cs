namespace DigitalScrumBoard1.DTOs.WorkItems;

/// <summary>
/// Work item data transfer object for API responses.
/// </summary>
public sealed class WorkItemDto
{
    public int WorkItemID { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Priority { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? AssignedUserID { get; set; }
    public string? AssignedUserName { get; set; }
    public int? ParentWorkItemID { get; set; }
    public int? TeamID { get; set; }
    public int? SprintID { get; set; }
    public string? TypeName { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
