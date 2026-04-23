using System.ComponentModel.DataAnnotations;

namespace DigitalScrumBoard1.DTOs.Authentication
{
    public sealed class ConfirmEmailRequest
    {
        [Required]
        public string Token { get; set; } = string.Empty;
    }
}
