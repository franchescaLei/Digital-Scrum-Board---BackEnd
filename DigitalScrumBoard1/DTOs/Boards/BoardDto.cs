namespace DigitalScrumBoard1.DTOs.Boards;

public sealed class BoardDto
{
    public int SprintID { get; set; }

    public string SprintName { get; set; } = "";

    public List<BoardWorkItemDto> Todo { get; set; } = new();

    public List<BoardWorkItemDto> Ongoing { get; set; } = new();

    public List<BoardWorkItemDto> ForChecking { get; set; } = new();

    public List<BoardWorkItemDto> Completed { get; set; } = new();
}