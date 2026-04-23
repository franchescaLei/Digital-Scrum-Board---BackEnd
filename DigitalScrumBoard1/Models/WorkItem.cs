using System.ComponentModel.DataAnnotations;

namespace DigitalScrumBoard1.Models
{
    public class WorkItem
    {
        public int WorkItemID { get; set; }

        public string Title { get; set; } = null!;
        public string? Description { get; set; }

        public string Status { get; set; } = null!; // To-do, Ongoing, For Checking, Completed
        public string? Priority { get; set; }       // Low, Medium, High, Critical

        public DateOnly? DueDate { get; set; }

        public int? AssignedUserID { get; set; }
        public User? AssignedUser { get; set; }

        public int CreatedByUserID { get; set; }
        public User CreatedByUser { get; set; } = null!;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public int WorkItemTypeID { get; set; }
        public WorkItemType WorkItemType { get; set; } = null!;

        public int? ParentWorkItemID { get; set; }
        public WorkItem? ParentWorkItem { get; set; }

        public int? SprintID { get; set; }
        public Sprint? Sprint { get; set; }

        public int? TeamID { get; set; }
        public Team? Team { get; set; }

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        // =============================
        // NEW FIELDS (SAFE ADDITIONS)
        // =============================

        public int BoardOrder { get; set; } = 0;

        [Timestamp]
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}