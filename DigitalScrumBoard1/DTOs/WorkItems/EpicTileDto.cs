namespace DigitalScrumBoard1.DTOs.WorkItems;

public sealed class EpicTileDto
{
    public int EpicID { get; set; }
    public string EpicTitle { get; set; } = "";

    public int CompletedStories { get; set; }
    public int TotalStories { get; set; }

    public int CompletedTasks { get; set; }
    public int TotalTasks { get; set; }
}