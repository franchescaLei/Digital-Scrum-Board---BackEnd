namespace DigitalScrumBoard1.DTOs.WorkItems;

public sealed class WorkItemCreatedResponseDto
{
    public int WorkItemID { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "To-do";

    public int? ParentWorkItemID { get; set; }
    public int? TeamID { get; set; }
    public int? AssignedUserID { get; set; }
}