using System.ComponentModel.DataAnnotations;

namespace DigitalScrumBoard1.DTOs.Sprints;

public sealed class CreateSprintRequestDto
{
    [Required]
    [MaxLength(100)]
    public string SprintName { get; set; } = "";

    [Required]
    [MaxLength(255)]
    public string Goal { get; set; } = "";

    [Required]
    public DateOnly? StartDate { get; set; }

    [Required]
    public DateOnly? EndDate { get; set; }

    [Required]
    public int? ManagedBy { get; set; }

    public int? TeamID { get; set; }
}