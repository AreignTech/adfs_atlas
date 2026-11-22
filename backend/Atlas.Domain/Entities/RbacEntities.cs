namespace Atlas.Domain.Entities;

public sealed class AppRole
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AppPermission
{
    public Guid Id { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AppRolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

public sealed class AppUserRole
{
    public string UserId { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public string? AssignedBy { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}

/// <summary>A user's effective role assignment with the role's own details.</summary>
public sealed class UserRoleAssignment
{
    public string UserId { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string RoleDescription { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; }
    public string? AssignedBy { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}

public sealed class RbacAudit
{
    public Guid Id { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? AffectedUserId { get; set; }
    public Guid? AffectedRoleId { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public bool Success { get; set; }
    public string Diagnostic { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
