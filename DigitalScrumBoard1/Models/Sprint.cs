namespace DigitalScrumBoard1.Models
{
    public class Sprint
    {
        public int SprintID { get; set; }

        public string SprintName { get; set; } = null!;
        public string? Goal { get; set; }

        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }

        public string Status { get; set; } = null!; // Planned, Active, Completed

        public int? ManagedBy { get; set; }
        public User? Manager { get; set; }

        public DateTime? CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public int? TeamID { get; set; }
        public Team? Team { get; set; }
    }
}
