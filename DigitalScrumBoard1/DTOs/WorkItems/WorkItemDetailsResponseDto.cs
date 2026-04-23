namespace DigitalScrumBoard1.DTOs.WorkItems;

public sealed class WorkItemDetailsResponseDto
{
    public int WorkItemID { get; set; }
    public string TypeName { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public string Status { get; set; } = "";
    public string? Priority { get; set; }
    public DateOnly? DueDate { get; set; }

    public int? ParentWorkItemID { get; set; }
    public string? ParentTitle { get; set; }

    public int? TeamID { get; set; }
    public string? TeamName { get; set; }

    public int? AssignedUserID { get; set; }
    public string? AssignedUserName { get; set; }

    public int? SprintID { get; set; }
    public string? SprintName { get; set; }

    public List<WorkItemCommentDto> Comments { get; set; } = new();
    public List<WorkItemChildDto> Stories { get; set; } = new();
    public List<WorkItemChildDto> Tasks { get; set; } = new();
}

public sealed class WorkItemChildDto
{
    public int WorkItemID { get; set; }
    public string TypeName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Priority { get; set; }
}
