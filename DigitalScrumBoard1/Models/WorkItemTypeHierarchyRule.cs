namespace DigitalScrumBoard1.Models
{
    public class WorkItemTypeHierarchyRule
    {
        public int RuleID { get; set; }

        public int ParentTypeID { get; set; }
        public WorkItemType ParentType { get; set; } = null!;

        public int ChildTypeID { get; set; }
        public WorkItemType ChildType { get; set; } = null!;

        public bool IsAllowed { get; set; }
    }
}
