namespace DigitalScrumBoard1.DTOs;

public class BoardResponseDto
{
    public int SprintID { get; set; }

    public string SprintName { get; set; } = "";

    public int? SprintManagerId { get; set; }

    public string? SprintManagerName { get; set; }

    public int? SprintTeamID { get; set; }

    public string? SprintTeamName { get; set; }

    public List<WorkItemBoardDto> Todo { get; set; } = new();

    public List<WorkItemBoardDto> Ongoing { get; set; } = new();

    public List<WorkItemBoardDto> ForChecking { get; set; } = new();

    public List<WorkItemBoardDto> Completed { get; set; } = new();
}

public class WorkItemBoardDto
{
    public int WorkItemID { get; set; }

    public string Title { get; set; } = "";

    public string Status { get; set; } = "";

    public string? TypeName { get; set; }

    public string? Priority { get; set; }

    public int? AssignedUserID { get; set; }

    public string? AssignedUserName { get; set; }

    public int? TeamID { get; set; }

    public int CommentCount { get; set; }

    public DateOnly? DueDate { get; set; }
}