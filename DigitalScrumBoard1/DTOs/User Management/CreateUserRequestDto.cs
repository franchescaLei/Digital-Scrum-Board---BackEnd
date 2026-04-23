using System.ComponentModel.DataAnnotations;

namespace DigitalScrumBoard1.Dtos
{
    public sealed class CreateUserRequestDto
    {
        [Required, StringLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [StringLength(50)]
        public string? MiddleName { get; set; }

        [StringLength(10)]
        public string? NameExtension { get; set; }

        [Required, StringLength(50)]
        public string LastName { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(100)]
        public string EmailAddress { get; set; } = string.Empty;

        [Required]
        public int RoleID { get; set; }

        [Required]
        public int TeamID { get; set; }
    }
}