using System.ComponentModel.DataAnnotations;

namespace DigitalScrumBoard1.DTOs.WorkItems;

public sealed class CreateWorkItemCommentRequestDto
{
    [Required]
    [MaxLength(2000)]
    public string CommentText { get; set; } = string.Empty;
}
