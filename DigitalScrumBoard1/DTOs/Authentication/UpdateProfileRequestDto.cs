using System.ComponentModel.DataAnnotations;

namespace DigitalScrumBoard1.DTOs.Authentication
{
    public sealed class UpdateProfileRequestDto
    {
        [Required, StringLength(120, MinimumLength = 1)]
        public string FirstName { get; set; } = string.Empty;

        [StringLength(120)]
        public string? MiddleName { get; set; }

        [Required, StringLength(120, MinimumLength = 1)]
        public string LastName { get; set; } = string.Empty;

        [StringLength(32)]
        public string? NameExtension { get; set; }
    }
}
