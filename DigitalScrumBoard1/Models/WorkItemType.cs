namespace DigitalScrumBoard1.Models
{
    public class WorkItemType
    {
        public int WorkItemTypeID { get; set; }
        public string TypeName { get; set; } = null!; // Epic, Story, Task
        public string? Description { get; set; }
    }
}
