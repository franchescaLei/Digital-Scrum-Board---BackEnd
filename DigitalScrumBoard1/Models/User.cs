using System.Data;

namespace DigitalScrumBoard1.Models
{
    public class User
    {
        public int UserID { get; set; }

        public string FirstName { get; set; } = null!;
        public string? MiddleName { get; set; }
        public string? NameExtension { get; set; }
        public string LastName { get; set; } = null!;

        public string EmailAddress { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;

        public int RoleID { get; set; }
        public Role Role { get; set; } = null!;

        public DateTime? LastLogin { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public bool Disabled { get; set; }
        public DateTime? DisabledAt { get; set; }

        public int? TeamID { get; set; }
        public Team? Team { get; set; }

        public bool MustChangePassword { get; set; } = true;

        public bool EmailVerified { get; set; } = false;
    }
}
