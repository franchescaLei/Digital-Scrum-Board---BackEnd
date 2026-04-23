namespace DigitalScrumBoard1.DTOs.WorkItems;

public sealed class WorkItemCommentDto
{
    public int CommentID { get; set; }
    public int WorkItemID { get; set; }
    public int CommentedBy { get; set; }
    public string CommentedByName { get; set; } = string.Empty;
    public string CommentText { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
