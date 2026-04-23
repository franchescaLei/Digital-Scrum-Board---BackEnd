using System.ComponentModel.DataAnnotations;

namespace DigitalScrumBoard1.DTOs.WorkItems;

public sealed class AssignTeamRequestDto
{
    // nullable to support "unassign team" if you want that behavior
    public int? TeamID { get; set; }
}