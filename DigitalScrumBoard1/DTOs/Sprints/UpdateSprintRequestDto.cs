using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DigitalScrumBoard1.DTOs.Sprints;

public sealed class UpdateSprintRequestDto
{
    [MaxLength(100)]
    public string? SprintName { get; set; }

    [MaxLength(255)]
    public string? Goal { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    [JsonPropertyName("teamID")]
    public int? TeamID { get; set; }

    [JsonPropertyName("managedBy")]
    public int? ManagedBy { get; set; }
}