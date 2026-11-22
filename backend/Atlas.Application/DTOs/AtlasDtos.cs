namespace Atlas.Application.DTOs;

/* ------------------------------------------------------------------------- */
/* Identity and session                                                       */
/* ------------------------------------------------------------------------- */

public sealed record UserDto(string Id, string Name, string Email, bool EmailVerified, string? Image);

public sealed record OrgScopeDto(Guid? LabId, Guid? SectionId, bool IsAssigned, bool IsAdministrator);

public sealed record SessionContextDto(
    UserDto User,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<string> Permissions,
    bool IsAdministrator,
    OrgScopeDto Scope);

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class DeleteAccountRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
}

public sealed class LdapLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/* ------------------------------------------------------------------------- */
/* Workspace                                                                  */
/* ------------------------------------------------------------------------- */

public sealed class MemberDto
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string JoinedDate { get; set; } = string.Empty;
    public string LastActive { get; set; } = string.Empty;
    public Guid? LabId { get; set; }
    public Guid? SectionId { get; set; }
    public string? LabCode { get; set; }
    public string? LabName { get; set; }
    public string? SectionCode { get; set; }
    public string? SectionName { get; set; }
}

public sealed class ProjectDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;
    public List<string> MemberIds { get; set; } = [];
    public Guid? LabId { get; set; }
    public Guid? SectionId { get; set; }
}

public sealed class TaskDto
{
    public string Id { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string AssigneeId { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;
    public int Position { get; set; }
    public Guid? LabId { get; set; }
    public Guid? SectionId { get; set; }
}

public sealed class WorkspaceDto
{
    public List<ProjectDto> Projects { get; set; } = [];
    public List<TaskDto> Tasks { get; set; } = [];
    public List<MemberDto> Members { get; set; } = [];
}

/* ------------------------------------------------------------------------- */
/* Inputs                                                                     */
/* ------------------------------------------------------------------------- */

public sealed class ProjectInput
{
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "planning";
    public string DueDate { get; set; } = string.Empty;
    public string? StartDate { get; set; }
    public string Color { get; set; } = "var(--chart-1)";
    public List<string> MemberIds { get; set; } = [];
    public Guid? LabId { get; set; }
    public Guid? SectionId { get; set; }
}

public sealed class TaskInput
{
    public Guid? ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "todo";
    public string Priority { get; set; } = "medium";
    public string? AssigneeId { get; set; }
    public string DueDate { get; set; } = string.Empty;
}

public sealed class TaskReorderItem
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Position { get; set; }
}

public sealed class TaskReorderInput
{
    public Guid ProjectId { get; set; }
    public List<TaskReorderItem> Items { get; set; } = [];
}

public sealed class InviteInput
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Role { get; set; }
    public Guid RoleId { get; set; }
    public Guid LabId { get; set; }
    public Guid SectionId { get; set; }
}

public sealed class StatusUpdateRequest
{
    public string Status { get; set; } = string.Empty;
}

public sealed class AssignRoleRequest
{
    public Guid RoleId { get; set; }
}

/* ------------------------------------------------------------------------- */
/* RBAC administration                                                        */
/* ------------------------------------------------------------------------- */

public sealed class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int AssignedUsers { get; set; }
    public List<string> AssignedUserNames { get; set; } = [];
    public List<string> PermissionKeys { get; set; } = [];
}

public sealed class RoleInput
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> PermissionKeys { get; set; } = [];
}

public sealed record PermissionDto(Guid Id, string PermissionKey, string Description, DateTimeOffset CreatedAt);

public sealed record RbacAuditDto(
    Guid Id,
    string? ActorUserId,
    string? ActorEmail,
    string Action,
    string? AffectedUserId,
    Guid? AffectedRoleId,
    string? PreviousValue,
    string? NewValue,
    bool Success,
    string Diagnostic,
    DateTimeOffset CreatedAt);

/* ------------------------------------------------------------------------- */
/* Organization administration                                                */
/* ------------------------------------------------------------------------- */

public sealed class OrgCodeInput
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AssignOrgRequest
{
    public Guid LabId { get; set; }
    public Guid SectionId { get; set; }
}

public sealed record OrgAuditDto(
    Guid Id,
    string? ActorEmail,
    string Action,
    string? AffectedUserId,
    Guid? AffectedLabId,
    Guid? AffectedSectionId,
    string? PreviousValue,
    string? NewValue,
    bool Success,
    string Diagnostic,
    DateTimeOffset CreatedAt);

/* ------------------------------------------------------------------------- */
/* LDAP administration                                                        */
/* ------------------------------------------------------------------------- */

public sealed class LdapConfigInput
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public string Protocol { get; set; } = "ldap";
    public string BaseDn { get; set; } = string.Empty;
    public string BindDn { get; set; } = string.Empty;
    public string BindPassword { get; set; } = string.Empty;
    public string SearchBase { get; set; } = string.Empty;
    public string UserSearchAttribute { get; set; } = "sAMAccountName";
    public string LoginIdentifierAttribute { get; set; } = "sAMAccountName";
    public string EmailAttribute { get; set; } = "mail";
    public string DisplayNameAttribute { get; set; } = "displayName";
    public string DirectoryIdentifierAttribute { get; set; } = "objectGUID";
    public bool RequireSecure { get; set; } = true;
    public bool ValidateTls { get; set; } = true;
    public string TlsCaCertificate { get; set; } = string.Empty;
    public int ConnectionTimeout { get; set; } = 10000;
    public bool AutoProvision { get; set; } = true;
    public bool RequireApproval { get; set; }
}

public sealed class LdapAdminConfigDto
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public string Protocol { get; set; } = "ldap";
    public string BaseDn { get; set; } = string.Empty;
    public string BindDn { get; set; } = string.Empty;
    public bool BindPasswordSet { get; set; }
    public string BindPassword { get; set; } = string.Empty;
    public string SearchBase { get; set; } = string.Empty;
    public string UserSearchAttribute { get; set; } = "sAMAccountName";
    public string LoginIdentifierAttribute { get; set; } = "sAMAccountName";
    public string EmailAttribute { get; set; } = "mail";
    public string DisplayNameAttribute { get; set; } = "displayName";
    public string DirectoryIdentifierAttribute { get; set; } = "objectGUID";
    public bool RequireSecure { get; set; } = true;
    public bool ValidateTls { get; set; } = true;
    public string TlsCaCertificate { get; set; } = string.Empty;
    public int ConnectionTimeout { get; set; } = 10000;
    public bool AutoProvision { get; set; } = true;
    public bool RequireApproval { get; set; }
    public string Status { get; set; } = "not-tested";
    public DateTimeOffset? LastTestAt { get; set; }
    public DateTimeOffset? LastSuccessfulTestAt { get; set; }
    public DateTimeOffset? LastConfigUpdateAt { get; set; }
}

public sealed class LdapGroupMappingDto
{
    public Guid Id { get; set; }
    public string LdapGroup { get; set; } = string.Empty;
    public string AtlasRole { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class LdapMappingInput
{
    public string LdapGroup { get; set; } = string.Empty;
    public string AtlasRole { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class LdapTestRequest
{
    public string? Username { get; set; }
}

public sealed record LdapAuditDto(
    Guid Id,
    string EventType,
    string? Username,
    string? Administrator,
    bool Success,
    string Diagnostic,
    DateTimeOffset CreatedAt);
