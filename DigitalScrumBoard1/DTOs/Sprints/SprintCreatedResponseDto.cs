namespace DigitalScrumBoard1.DTOs.Sprints;

public sealed class SprintCreatedResponseDto
{
    public int SprintID { get; set; }
    public string SprintName { get; set; } = "";
    public string Goal { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = "";
    public int? ManagedBy { get; set; }
    public string? ManagedByName { get; set; }
    public int? TeamID { get; set; }
    public string? TeamName { get; set; }
}