namespace DigitalScrumBoard1.Models
{
    public sealed class PasswordResetToken
    {
        public int PasswordResetTokenID { get; set; }

        public int UserID { get; set; }
        public User User { get; set; } = null!;

        public string TokenHash { get; set; } = string.Empty; // base64(SHA256(rawToken))
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}