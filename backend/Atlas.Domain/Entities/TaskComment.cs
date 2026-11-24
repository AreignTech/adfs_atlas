namespace Atlas.Domain.Entities;

public sealed class TaskComment
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorImage { get; set; }
}
