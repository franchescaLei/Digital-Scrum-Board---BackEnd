using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DigitalScrumBoard1.DTOs.WorkItems;

public sealed class UpdateWorkItemRequestDto
{
    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(20)]
    public string? Priority { get; set; }

    public int? ParentWorkItemID { get; set; }

    public int? TeamID { get; set; }

    public int? AssignedUserID { get; set; }

    /// <summary>
    /// When true, explicitly clears the assignee (sets to null).
    /// Use this instead of sending AssignedUserID=null which is indistinguishable from "not provided".
    /// </summary>
    [JsonPropertyName("clearAssignee")]
    public bool? ClearAssignee { get; set; }

    public DateOnly? DueDate { get; set; }
}