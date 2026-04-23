namespace DigitalScrumBoard1.DTOs;

public class SprintMetricsDto
{
    public int TotalItems { get; set; }

    public int CompletedItems { get; set; }

    public int RemainingItems { get; set; }

    public double CompletionRate { get; set; }

    public int TotalStoryPoints { get; set; }

    public int CompletedStoryPoints { get; set; }
}