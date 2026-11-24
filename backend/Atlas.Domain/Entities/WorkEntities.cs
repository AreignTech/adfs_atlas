namespace Atlas.Domain.Entities;

public sealed class Member
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = "Member";
    public string Status { get; set; } = "invited";
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LastActiveAt { get; set; }
}

/// <summary>Workspace projection: member joined to identity and organization.</summary>
public sealed class MemberWithUser
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LastActiveAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid? LabId { get; set; }
    public Guid? SectionId { get; set; }
    public string? LabCode { get; set; }
    public string? LabName { get; set; }
    public string? SectionCode { get; set; }
    public string? SectionName { get; set; }
}

public sealed class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "planning";
    public string Color { get; set; } = "var(--chart-1)";
    public Guid OwnerId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime DueDate { get; set; }
    public Guid? LabId { get; set; }
    public Guid? SectionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TaskItem
{
    public int CommentCount { get; set; }
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "todo";
    public string Priority { get; set; } = "medium";
    public Guid? AssigneeId { get; set; }
    public DateTime DueDate { get; set; }
    public Guid? LabId { get; set; }
    public Guid? SectionId { get; set; }
    public int Position { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ProjectMember
{
    public Guid ProjectId { get; set; }
    public Guid MemberId { get; set; }
}
