using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Dapper;

namespace Atlas.Infrastructure.Repositories;

public sealed class LabRepository : ILabRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public LabRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private sealed record Timestamps(DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public async Task<IReadOnlyList<LabCode>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<LabCode>(new CommandDefinition(
            """
            SELECT id, code, name, description, is_active, created_by, created_at, updated_at
            FROM dbo.lab_code
            WHERE @IncludeInactive = 1 OR is_active = 1
            ORDER BY code
            """,
            new { IncludeInactive = includeInactive ? 1 : 0 },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<LabCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<LabCode>(new CommandDefinition(
            """
            SELECT id, code, name, description, is_active, created_by, created_at, updated_at
            FROM dbo.lab_code
            WHERE id = @Id
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ExistsCodeAsync(
        string code,
        Guid? excludeId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM dbo.lab_code
            WHERE code = @Code AND (@ExcludeId IS NULL OR id <> @ExcludeId)
            """,
            new { Code = code, ExcludeId = excludeId },
            cancellationToken: cancellationToken));
        return count > 0;
    }

    public async Task CreateAsync(LabCode lab, CancellationToken cancellationToken = default)
    {
        if (lab.Id == Guid.Empty)
        {
            lab.Id = Guid.NewGuid();
        }

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var timestamps = await connection.QuerySingleAsync<Timestamps>(new CommandDefinition(
            """
            INSERT INTO dbo.lab_code (id, code, name, description, is_active, created_by)
            OUTPUT inserted.created_at AS CreatedAt, inserted.updated_at AS UpdatedAt
            VALUES (@Id, @Code, @Name, @Description, @IsActive, @CreatedBy)
            """,
            lab,
            cancellationToken: cancellationToken));

        lab.CreatedAt = timestamps.CreatedAt;
        lab.UpdatedAt = timestamps.UpdatedAt;
    }

    public async Task<bool> UpdateAsync(LabCode lab, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.lab_code
            SET code = @Code,
                name = @Name,
                description = @Description,
                is_active = @IsActive,
                updated_at = SYSUTCDATETIME()
            WHERE id = @Id
            """,
            lab,
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.lab_code WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return affected > 0;
    }
}

public sealed class SectionRepository : ISectionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SectionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private sealed record Timestamps(DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public async Task<IReadOnlyList<SectionCode>> ListAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<SectionCode>(new CommandDefinition(
            """
            SELECT id, code, name, description, is_active, created_by, created_at, updated_at
            FROM dbo.section_code
            WHERE @IncludeInactive = 1 OR is_active = 1
            ORDER BY code
            """,
            new { IncludeInactive = includeInactive ? 1 : 0 },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<SectionCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<SectionCode>(new CommandDefinition(
            """
            SELECT id, code, name, description, is_active, created_by, created_at, updated_at
            FROM dbo.section_code
            WHERE id = @Id
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ExistsCodeAsync(
        string code,
        Guid? excludeId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*)
            FROM dbo.section_code
            WHERE code = @Code AND (@ExcludeId IS NULL OR id <> @ExcludeId)
            """,
            new { Code = code, ExcludeId = excludeId },
            cancellationToken: cancellationToken));
        return count > 0;
    }

    public async Task CreateAsync(SectionCode section, CancellationToken cancellationToken = default)
    {
        if (section.Id == Guid.Empty)
        {
            section.Id = Guid.NewGuid();
        }

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var timestamps = await connection.QuerySingleAsync<Timestamps>(new CommandDefinition(
            """
            INSERT INTO dbo.section_code (id, code, name, description, is_active, created_by)
            OUTPUT inserted.created_at AS CreatedAt, inserted.updated_at AS UpdatedAt
            VALUES (@Id, @Code, @Name, @Description, @IsActive, @CreatedBy)
            """,
            section,
            cancellationToken: cancellationToken));

        section.CreatedAt = timestamps.CreatedAt;
        section.UpdatedAt = timestamps.UpdatedAt;
    }

    public async Task<bool> UpdateAsync(SectionCode section, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.section_code
            SET code = @Code,
                name = @Name,
                description = @Description,
                is_active = @IsActive,
                updated_at = SYSUTCDATETIME()
            WHERE id = @Id
            """,
            section,
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.section_code WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return affected > 0;
    }
}

public sealed class OrgAssignmentRepository : IOrgAssignmentRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public OrgAssignmentRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<UserOrgAssignment?> GetForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<UserOrgAssignment>(new CommandDefinition(
            """
            SELECT user_id, lab_id, section_id, assigned_by, assigned_at
            FROM dbo.user_org_assignment
            WHERE user_id = @UserId
            """,
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task UpsertAsync(UserOrgAssignment assignment, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF EXISTS (SELECT 1 FROM dbo.user_org_assignment WHERE user_id = @UserId)
            BEGIN
                UPDATE dbo.user_org_assignment
                SET lab_id = @LabId,
                    section_id = @SectionId,
                    assigned_by = @AssignedBy,
                    assigned_at = SYSUTCDATETIME()
                WHERE user_id = @UserId;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.user_org_assignment (user_id, lab_id, section_id, assigned_by)
                VALUES (@UserId, @LabId, @SectionId, @AssignedBy);
            END
            """,
            assignment,
            cancellationToken: cancellationToken));
    }

    public async Task<int> CountUsersWithLabAsync(Guid labId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.user_org_assignment WHERE lab_id = @LabId",
            new { LabId = labId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CountUsersWithSectionAsync(Guid sectionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.user_org_assignment WHERE section_id = @SectionId",
            new { SectionId = sectionId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.user_org_assignment WHERE user_id = @UserId",
            new { UserId = userId },
            cancellationToken: cancellationToken));
        return affected > 0;
    }
}

public sealed class OrgAuditRepository : IOrgAuditRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public OrgAuditRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InsertAsync(OrgAudit audit, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.org_audit
                (id, actor_email, action, affected_user_id, affected_lab_id, affected_section_id,
                 previous_value, new_value, success, diagnostic)
            VALUES
                (@Id, @ActorEmail, @Action, @AffectedUserId, @AffectedLabId, @AffectedSectionId,
                 @PreviousValue, @NewValue, @Success, @Diagnostic)
            """,
            new
            {
                Id = audit.Id == Guid.Empty ? Guid.NewGuid() : audit.Id,
                audit.ActorEmail,
                audit.Action,
                audit.AffectedUserId,
                audit.AffectedLabId,
                audit.AffectedSectionId,
                audit.PreviousValue,
                audit.NewValue,
                audit.Success,
                Diagnostic = audit.Diagnostic ?? string.Empty
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<OrgAudit>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<OrgAudit>(new CommandDefinition(
            """
            SELECT TOP (@Limit)
                id, actor_email, action, affected_user_id, affected_lab_id, affected_section_id,
                previous_value, new_value, success, diagnostic, created_at
            FROM dbo.org_audit
            ORDER BY created_at DESC
            """,
            new { Limit = limit },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
