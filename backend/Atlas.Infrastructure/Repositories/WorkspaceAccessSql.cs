namespace Atlas.Infrastructure.Repositories;

internal static class WorkspaceAccessSql
{
    // Expressions are repository-owned SQL, never request input. Keep list and search access identical.
    public static string ForProject(string projectId) => $"""
        (@Unscoped = 1 OR EXISTS (
            SELECT 1 FROM dbo.project_member access_pm
            INNER JOIN dbo.member access_m ON access_m.id = access_pm.member_id
            WHERE access_pm.project_id = {projectId} AND access_m.user_id = @UserId
        ) OR EXISTS (
            SELECT 1 FROM dbo.project access_p
            INNER JOIN dbo.member access_owner ON access_owner.id = access_p.owner_id
            WHERE access_p.id = {projectId} AND access_owner.user_id = @UserId
        ))
        """;
}
