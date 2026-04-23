namespace DigitalScrumBoard1.DTOs.SignalR;

/// <summary>
/// Complete sprint data for real-time updates.
/// Allows frontend to update UI without refetching.
/// </summary>
public sealed class SprintBroadcastDto
{
    public int SprintID { get; set; }
    public string SprintName { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ManagedBy { get; set; }
    public string? ManagedByName { get; set; }
    public int? TeamID { get; set; }
    public string? TeamName { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Sprint lifecycle event data.
/// </summary>
public sealed class SprintLifecycleBroadcastDto
{
    public int SprintID { get; set; }
    public string SprintName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Goal { get; set; }
    public int? ManagedBy { get; set; }
    public int? TeamID { get; set; }
    
    // For stop/complete events
    public int? UnfinishedCount { get; set; }
    public int? CompletedCount { get; set; }
    public int? ReturnedToBacklogCount { get; set; }
    
    public DateTime ChangedAt { get; set; }
}
