namespace DigitalScrumBoard1.DTOs.Authentication
{
    public sealed class AuthUserDto
    {
        public int UserID { get; set; }
        public string EmailAddress { get; set; } = string.Empty;

        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string LastName { get; set; } = string.Empty;
        public string? NameExtension { get; set; }

        /// <summary>Display name built from the name parts (for headers and UI).</summary>
        public string FullName { get; set; } = string.Empty;

        public int RoleID { get; set; }
        public string RoleName { get; set; } = string.Empty;

        public int? TeamID { get; set; }
        public string? TeamName { get; set; }
    }
}