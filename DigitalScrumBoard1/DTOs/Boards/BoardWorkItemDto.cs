namespace DigitalScrumBoard1.DTOs.Boards;

public sealed class BoardWorkItemDto
{
    public int WorkItemID { get; set; }

    public string Title { get; set; } = "";

    public string Status { get; set; } = "";

    public string Priority { get; set; } = "";

    public int? AssignedUserID { get; set; }

    public int SprintID { get; set; }
}