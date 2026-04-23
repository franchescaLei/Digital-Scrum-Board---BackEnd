namespace DigitalScrumBoard1.DTOs.WorkItems;

public sealed class AgendasResponseDto
{
    public List<AgendaSprintDto> Sprints { get; set; } = new();
    public List<AgendaWorkItemDto> WorkItems { get; set; } = new();
}

public sealed class AgendaSprintDto
{
    public int SprintID { get; set; }
    public string SprintName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public List<AgendaWorkItemDto> WorkItems { get; set; } = new();
}

public sealed class AgendaWorkItemDto
{
    public int WorkItemID { get; set; }
    public string Title { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Priority { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? ParentWorkItemID { get; set; }
    public int? SprintID { get; set; }
    public int? TeamID { get; set; }
    public int? AssignedUserID { get; set; }
    public int? AssignedUserTeamId { get; set; }
}