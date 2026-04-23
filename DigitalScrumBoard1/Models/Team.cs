namespace DigitalScrumBoard1.Models
{
    public class Team
    {
        public int TeamID { get; set; }
        public string TeamName { get; set; } = null!;
        public string? Description { get; set; }

        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
