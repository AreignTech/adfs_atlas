namespace Atlas.Application.Security;

/// <summary>
/// The 21 RBAC permission keys and the legacy member-role fallback sets.
/// These strings are the contract with the existing frontend and database, so
/// they must not be renamed.
/// </summary>
public static class AtlasPermissions
{
    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";
    public const string UsersAssignRoles = "users.assign_roles";
    public const string RolesView = "roles.view";
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesDelete = "roles.delete";
    public const string AdminAccess = "admin.access";
    public const string AdminSettings = "admin.settings";
    public const string AdminAuthentication = "admin.authentication";
    public const string AdminAuditLogs = "admin.audit_logs";
    public const string TasksView = "tasks.view";
    public const string TasksCreate = "tasks.create";
    public const string TasksEdit = "tasks.edit";
    public const string TasksDelete = "tasks.delete";
    public const string TasksAssign = "tasks.assign";
    public const string TasksManageAll = "tasks.manage_all";
    public const string ReportsView = "reports.view";
    public const string ReportsManage = "reports.manage";

    public static readonly IReadOnlyList<string> All =
    [
        UsersView, UsersCreate, UsersEdit, UsersDelete, UsersAssignRoles,
        RolesView, RolesCreate, RolesEdit, RolesDelete,
        AdminAccess, AdminSettings, AdminAuthentication, AdminAuditLogs,
        TasksView, TasksCreate, TasksEdit, TasksDelete, TasksAssign, TasksManageAll,
        ReportsView, ReportsManage
    ];

    public static IReadOnlyList<string> ForLegacyRole(string? memberRole)
    {
        var role = memberRole ?? "Users";

        if (role.Contains("admin", StringComparison.OrdinalIgnoreCase))
        {
            return All;
        }

        if (role.Contains("supervisor", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                UsersView, TasksView, TasksCreate, TasksEdit, TasksAssign,
                TasksManageAll, ReportsView
            ];
        }

        return [TasksView, TasksCreate, TasksEdit, TasksAssign];
    }
}
