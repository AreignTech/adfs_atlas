using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Dapper;

namespace Atlas.Infrastructure.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RoleRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private sealed record Timestamps(DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public async Task<IReadOnlyList<AppRole>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AppRole>(new CommandDefinition(
            """
            SELECT id, name, description, is_system_role, is_active, created_at, updated_at
            FROM dbo.app_role
            ORDER BY name
            """,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<AppRole?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AppRole>(new CommandDefinition(
            """
            SELECT id, name, description, is_system_role, is_active, created_at, updated_at
            FROM dbo.app_role
            WHERE id = @Id
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
    }

    public async Task<AppRole?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AppRole>(new CommandDefinition(
            """
            SELECT id, name, description, is_system_role, is_active, created_at, updated_at
            FROM dbo.app_role
            WHERE name = @Name
            """,
            new { Name = name },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ExistsNameAsync(
        string name,
        Guid? excludeId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM dbo.app_role
            WHERE name = @Name AND (@ExcludeId IS NULL OR id <> @ExcludeId)
            """,
            new { Name = name, ExcludeId = excludeId },
            cancellationToken: cancellationToken));
        return count > 0;
    }

    public async Task CreateAsync(AppRole role, CancellationToken cancellationToken = default)
    {
        if (role.Id == Guid.Empty)
        {
            role.Id = Guid.NewGuid();
        }

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var timestamps = await connection.QuerySingleAsync<Timestamps>(new CommandDefinition(
            """
            INSERT INTO dbo.app_role (id, name, description, is_system_role, is_active)
            OUTPUT inserted.created_at AS CreatedAt, inserted.updated_at AS UpdatedAt
            VALUES (@Id, @Name, @Description, @IsSystemRole, @IsActive)
            """,
            role,
            cancellationToken: cancellationToken));

        role.CreatedAt = timestamps.CreatedAt;
        role.UpdatedAt = timestamps.UpdatedAt;
    }

    public async Task<bool> UpdateAsync(AppRole role, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.app_role
            SET name = @Name,
                description = @Description,
                is_active = @IsActive,
                updated_at = SYSUTCDATETIME()
            WHERE id = @Id
            """,
            role,
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.app_role WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<int> CountAssignedUsersAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.app_user_role WHERE role_id = @RoleId",
            new { RoleId = roleId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<string>> ListAssignedUserNamesAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT u.name
            FROM dbo.app_user_role ur
            INNER JOIN dbo.member m ON m.user_id = ur.user_id
            INNER JOIN dbo.[user] u ON u.id = m.user_id
            WHERE ur.role_id = @RoleId
            ORDER BY u.name
            """,
            new { RoleId = roleId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }
}

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PermissionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AppPermission>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AppPermission>(new CommandDefinition(
            """
            SELECT id, permission_key, description, created_at
            FROM dbo.app_permission
            ORDER BY permission_key
            """,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT permission_key FROM dbo.app_permission ORDER BY permission_key",
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<AppPermission>> GetByKeysAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var list = keys.Distinct().ToArray();
        if (list.Length == 0)
        {
            return [];
        }

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AppPermission>(new CommandDefinition(
            """
            SELECT id, permission_key, description, created_at
            FROM dbo.app_permission
            WHERE permission_key IN @Keys
            """,
            new { Keys = list },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<AppPermission>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToArray();
        if (list.Length == 0)
        {
            return [];
        }

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AppPermission>(new CommandDefinition(
            """
            SELECT id, permission_key, description, created_at
            FROM dbo.app_permission
            WHERE id IN @Ids
            """,
            new { Ids = list },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }
}

public sealed class UserRoleRepository : IUserRoleRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UserRoleRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string SelectSql = """
        SELECT ur.user_id, ur.role_id, r.name AS role_name, r.description AS role_description,
               r.is_system_role, r.is_active, ur.assigned_by, ur.assigned_at
        FROM dbo.app_user_role ur
        INNER JOIN dbo.app_role r ON r.id = ur.role_id
        """;

    public async Task<UserRoleAssignment?> GetForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<UserRoleAssignment>(new CommandDefinition(
            $"{SelectSql} WHERE ur.user_id = @UserId ORDER BY r.name",
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<UserRoleAssignment>> ListForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserRoleAssignment>(new CommandDefinition(
            $"{SelectSql} WHERE ur.user_id = @UserId ORDER BY r.name",
            new { UserId = userId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task ReplaceForUserAsync(
        string userId,
        Guid roleId,
        string? assignedBy,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.app_user_role WHERE user_id = @UserId",
                new { UserId = userId },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO dbo.app_user_role (user_id, role_id, assigned_by)
                VALUES (@UserId, @RoleId, @AssignedBy)
                """,
                new { UserId = userId, RoleId = roleId, AssignedBy = assignedBy },
                transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.app_user_role WHERE user_id = @UserId",
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM dbo.app_user_role ur
            INNER JOIN dbo.app_role r ON r.id = ur.role_id
            INNER JOIN dbo.member m ON m.user_id = ur.user_id
            WHERE r.name = N'Administrators' AND m.status = N'active'
            """,
            cancellationToken: cancellationToken));
    }
}

public sealed class RolePermissionRepository : IRolePermissionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RolePermissionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<string>> ListKeysForRolesAsync(
        IEnumerable<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        var list = roleIds.Distinct().ToArray();
        if (list.Length == 0)
        {
            return [];
        }

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT DISTINCT p.permission_key
            FROM dbo.app_role_permission rp
            INNER JOIN dbo.app_permission p ON p.id = rp.permission_id
            WHERE rp.role_id IN @RoleIds
            ORDER BY p.permission_key
            """,
            new { RoleIds = list },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task ReplaceForRoleAsync(
        Guid roleId,
        IEnumerable<Guid> permissionIds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.app_role_permission WHERE role_id = @RoleId",
                new { RoleId = roleId },
                transaction,
                cancellationToken: cancellationToken));

            foreach (var permissionId in permissionIds.Distinct())
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.app_role_permission (role_id, permission_id)
                    VALUES (@RoleId, @PermissionId)
                    """,
                    new { RoleId = roleId, PermissionId = permissionId },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteForRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.app_role_permission WHERE role_id = @RoleId",
            new { RoleId = roleId },
            cancellationToken: cancellationToken));
    }
}

public sealed class RbacAuditRepository : IRbacAuditRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RbacAuditRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InsertAsync(RbacAudit audit, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.rbac_audit
                (id, actor_user_id, actor_email, action, affected_user_id, affected_role_id,
                 previous_value, new_value, success, diagnostic)
            VALUES
                (@Id, @ActorUserId, @ActorEmail, @Action, @AffectedUserId, @AffectedRoleId,
                 @PreviousValue, @NewValue, @Success, @Diagnostic)
            """,
            new
            {
                Id = audit.Id == Guid.Empty ? Guid.NewGuid() : audit.Id,
                audit.ActorUserId,
                audit.ActorEmail,
                audit.Action,
                audit.AffectedUserId,
                audit.AffectedRoleId,
                audit.PreviousValue,
                audit.NewValue,
                audit.Success,
                Diagnostic = audit.Diagnostic ?? string.Empty
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<RbacAudit>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<RbacAudit>(new CommandDefinition(
            """
            SELECT TOP (@Limit)
                id, actor_user_id, actor_email, action, affected_user_id, affected_role_id,
                previous_value, new_value, success, diagnostic, created_at
            FROM dbo.rbac_audit
            ORDER BY created_at DESC
            """,
            new { Limit = limit },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
