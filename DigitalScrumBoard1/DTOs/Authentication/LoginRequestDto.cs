using System.ComponentModel.DataAnnotations;

namespace DigitalScrumBoard1.DTOs.Authentication
{
    public sealed class LoginRequestDto
    {
        [Required]
        [EmailAddress]
        [StringLength(100)]
        public string EmailAddress { get; set; } = string.Empty;

        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string Password { get; set; } = string.Empty;
    }
}