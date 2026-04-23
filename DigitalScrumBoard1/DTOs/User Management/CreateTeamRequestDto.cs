using System.ComponentModel.DataAnnotations;

namespace DigitalScrumBoard1.Dtos
{
    public sealed class CreateTeamRequestDto
    {
        [Required, StringLength(50)]
        public string TeamName { get; set; } = string.Empty;

        [StringLength(255)]
        public string? Description { get; set; }
    }
}