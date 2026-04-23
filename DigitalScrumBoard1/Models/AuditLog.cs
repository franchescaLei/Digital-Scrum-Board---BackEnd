namespace DigitalScrumBoard1.Models
{
    public class AuditLog
    {
        public int LogID { get; set; }

        public int UserID { get; set; }
        public User User { get; set; } = null!;

        public string Action { get; set; } = null!;
        public string IPAddress { get; set; } = null!;
        public DateTime Timestamp { get; set; }

        public bool Success { get; set; }
        public string? Details { get; set; }

        public string? TargetType { get; set; }
        public int? TargetID { get; set; }
    }
}
