namespace Atlas.Application.DTOs;

public sealed record TaskCommentInput(string Content);
public sealed record TaskCommentDto(Guid Id, Guid TaskId, string UserId, string AuthorName,
    string? AuthorImage, string Content, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    bool CanEdit, bool CanDelete);
public sealed record TaskCommentPage(IReadOnlyList<TaskCommentDto> Items, int Total, int Offset, int Limit, bool CanComment);
public sealed record TaskCommentCount(int Count);
