namespace DigitalScrumBoard1.Models
{
    public class WorkItemComment
    {
        public int CommentID { get; set; }

        public int WorkItemID { get; set; }
        public WorkItem WorkItem { get; set; } = null!;

        public int CommentedBy { get; set; }
        public User CommentedByUser { get; set; } = null!;

        public string CommentText { get; set; } = null!;

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public bool IsDeleted { get; set; }
    }
}
