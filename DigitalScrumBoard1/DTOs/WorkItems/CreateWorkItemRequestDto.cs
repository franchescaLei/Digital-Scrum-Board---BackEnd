using System.ComponentModel.DataAnnotations;

namespace DigitalScrumBoard1.DTOs.WorkItems;

public sealed class CreateWorkItemRequestDto
{
    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = ""; // Epic, Story, Task

    [Required]
    [MaxLength(50)]
    public string Title { get; set; } = "";

    [Required]
    [MaxLength(2000)]
    public string Description { get; set; } = "";

    [Required]
    [MaxLength(20)]
    public string Priority { get; set; } = "";

    // Required for Story/Task, must be null for Epic
    public int? ParentWorkItemID { get; set; }

    // Optional (but Epic will be forced to null server-side)
    public int? TeamID { get; set; }

    // Optional
    public int? AssignedUserID { get; set; }

    // Optional due date for the work item
    public DateOnly? DueDate { get; set; }
}