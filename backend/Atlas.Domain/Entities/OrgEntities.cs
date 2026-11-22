namespace Atlas.Domain.Entities;

public sealed class LabCode
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SectionCode
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class UserOrgAssignment
{
    public string UserId { get; set; } = string.Empty;
    public Guid? LabId { get; set; }
    public Guid? SectionId { get; set; }
    public string? AssignedBy { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}

public sealed class OrgAudit
{
    public Guid Id { get; set; }
    public string? ActorEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? AffectedUserId { get; set; }
    public Guid? AffectedLabId { get; set; }
    public Guid? AffectedSectionId { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public bool Success { get; set; }
    public string Diagnostic { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
