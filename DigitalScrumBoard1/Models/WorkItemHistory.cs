namespace DigitalScrumBoard1.Models
{
    public class WorkItemHistory
    {
        public int HistoryID { get; set; }

        public int WorkItemID { get; set; }
        public WorkItem WorkItem { get; set; } = null!;

        public int ChangedBy { get; set; }
        public User ChangedByUser { get; set; } = null!;

        public string FieldChanged { get; set; } = null!;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        public DateTime ChangedAt { get; set; }
    }
}
