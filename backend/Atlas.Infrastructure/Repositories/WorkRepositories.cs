using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Dapper;

namespace Atlas.Infrastructure.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProjectRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private sealed record Timestamps(DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    public async Task<IReadOnlyList<Project>> ListAsync(
        OrgScopeFilter? scope,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Project>(new CommandDefinition(
            $"""
            SELECT p.*
            FROM dbo.project p
            WHERE {WorkspaceAccessSql.ForProject("p.id")}
            ORDER BY p.due_date
            """,
            new
            {
                Unscoped = scope is null || scope.IsAdministrator ? 1 : 0,
                scope?.LabId,
                scope?.SectionId,
                scope?.UserId
            },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Project>(new CommandDefinition(
            "SELECT p.* FROM dbo.project p WHERE p.id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.project WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return count > 0;
    }

    public async Task CreateAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (project.Id == Guid.Empty)
        {
            project.Id = Guid.NewGuid();
        }

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var timestamps = await connection.QuerySingleAsync<Timestamps>(new CommandDefinition(
            """
            INSERT INTO dbo.project
                (id, name, [key], description, status, color, owner_id, start_date, due_date, lab_id, section_id)
            OUTPUT inserted.created_at AS CreatedAt, inserted.updated_at AS UpdatedAt
            VALUES
                (@Id, @Name, @Key, @Description, @Status, @Color, @OwnerId, @StartDate, @DueDate, @LabId, @SectionId)
            """,
            project,
            cancellationToken: cancellationToken));

        project.CreatedAt = timestamps.CreatedAt;
        project.UpdatedAt = timestamps.UpdatedAt;
    }

    public async Task<bool> UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.project
            SET name = @Name,
                [key] = @Key,
                description = @Description,
                status = @Status,
                color = @Color,
                owner_id = @OwnerId,
                start_date = @StartDate,
                due_date = @DueDate,
                lab_id = @LabId,
                section_id = @SectionId,
                updated_at = SYSUTCDATETIME()
            WHERE id = @Id
            """,
            project,
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> SetStatusAsync(Guid id, string status, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.project SET status = @Status, updated_at = SYSUTCDATETIME() WHERE id = @Id",
            new { Id = id, Status = status },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.project WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<int> CountOwnedAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.project WHERE owner_id = @MemberId",
            new { MemberId = memberId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> TransferOwnershipAsync(
        Guid fromMemberId,
        Guid toMemberId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.project
            SET owner_id = @ToMemberId, updated_at = SYSUTCDATETIME()
            WHERE owner_id = @FromMemberId
            """,
            new { FromMemberId = fromMemberId, ToMemberId = toMemberId },
            cancellationToken: cancellationToken));
    }
}

public sealed class TaskRepository : ITaskRepository
{
    private sealed record ParentScope(Guid? LabId, Guid? SectionId);
    private readonly IDbConnectionFactory _connectionFactory;

    public TaskRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private sealed record CreatedTask(string Reference, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

    private sealed record PositionBounds(int MinAll, int MaxStatus);

    public async Task<IReadOnlyList<TaskItem>> ListAsync(
        OrgScopeFilter? scope,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<TaskItem>(new CommandDefinition(
            $"""
            SELECT t.*, (SELECT COUNT(*) FROM dbo.task_comment c WHERE c.task_id=t.id) AS CommentCount
            FROM dbo.task t
            INNER JOIN dbo.project p ON p.id = t.project_id
            WHERE {WorkspaceAccessSql.ForProject("p.id")}
            ORDER BY t.project_id, t.status, t.position
            """,
            new
            {
                Unscoped = scope is null || scope.IsAdministrator ? 1 : 0,
                scope?.LabId,
                scope?.SectionId,
                scope?.UserId
            },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<TaskItem>(new CommandDefinition(
            "SELECT t.*, (SELECT COUNT(*) FROM dbo.task_comment c WHERE c.task_id=t.id) AS CommentCount FROM dbo.task t WHERE t.id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
    }

    public async Task CreateAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        if (task.Id == Guid.Empty)
        {
            task.Id = Guid.NewGuid();
        }

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var parent = await connection.QuerySingleOrDefaultAsync<ParentScope>(new CommandDefinition(
            "SELECT lab_id AS LabId,section_id AS SectionId FROM dbo.project WITH(UPDLOCK,HOLDLOCK) WHERE id=@ProjectId", new { task.ProjectId }, transaction, cancellationToken: cancellationToken))
            ?? throw new Atlas.Application.Common.NotFoundException("Project was not found.");
        if (parent.LabId != task.LabId || parent.SectionId != task.SectionId) throw new Atlas.Application.Common.ConflictException("Project organization changed. Reload and try again.");
        var bounds = await connection.QuerySingleAsync<PositionBounds>(new CommandDefinition(
            "SELECT COALESCE(MIN(position),-1) AS MinAll,COALESCE(MAX(CASE WHEN status=@Status THEN position END),0) AS MaxStatus FROM dbo.task WHERE project_id=@ProjectId", new { task.ProjectId, task.Status }, transaction, cancellationToken: cancellationToken));
        task.Position = checked(Math.Min(bounds.MinAll, bounds.MaxStatus) - 1);
        var created = await connection.QuerySingleAsync<CreatedTask>(new CommandDefinition(
            """
            INSERT INTO dbo.task
                (id, project_id, title, description, status, priority, assignee_id, due_date,
                 lab_id, section_id, position, completed_at)
            OUTPUT inserted.reference AS Reference,
                   inserted.created_at AS CreatedAt,
                   inserted.updated_at AS UpdatedAt
            VALUES
                (@Id, @ProjectId, @Title, @Description, @Status, @Priority, @AssigneeId, @DueDate,
                 @LabId, @SectionId, @Position, @CompletedAt)
            """,
            task,
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        task.Reference = created.Reference;
        task.CreatedAt = created.CreatedAt;
        task.UpdatedAt = created.UpdatedAt;
    }

    public async Task<bool> UpdateAsync(TaskItem task, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.task
            SET project_id = @ProjectId,
                title = @Title,
                description = @Description,
                status = @Status,
                priority = @Priority,
                assignee_id = @AssigneeId,
                due_date = @DueDate,
                lab_id = @LabId,
                section_id = @SectionId,
                position = @Position,
                completed_at = @CompletedAt,
                updated_at = SYSUTCDATETIME()
            WHERE id = @Id
            """,
            task,
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.task WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    /// <summary>
    /// Mirrors the PostgreSQL implementation: new tasks land above every task in
    /// the project, and above the current column maximum.
    /// </summary>
    public async Task<int> NextPositionAsync(
        Guid projectId,
        string status,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<PositionBounds>(new CommandDefinition(
            """
            SELECT
                COALESCE(MIN(position), -1) AS MinAll,
                COALESCE(MAX(CASE WHEN status = @Status THEN position END), 0) AS MaxStatus
            FROM dbo.task
            WHERE project_id = @ProjectId
            """,
            new { ProjectId = projectId, Status = status },
            cancellationToken: cancellationToken));
        return Math.Min(row.MinAll, row.MaxStatus) - 1;
    }

    public async Task ReorderAsync(
        Guid projectId,
        IReadOnlyList<TaskReorderItem> items,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in items)
            {
                if (!Guid.TryParse(item.TaskId, out var taskId))
                {
                    continue;
                }

                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE dbo.task
                    SET status = @Status,
                        position = @Position,
                        completed_at = CASE
                            WHEN @Status = N'completed' THEN COALESCE(completed_at, SYSUTCDATETIME())
                            ELSE NULL
                        END,
                        updated_at = SYSUTCDATETIME()
                    WHERE id = @TaskId AND project_id = @ProjectId
                    """,
                    new { TaskId = taskId, ProjectId = projectId, item.Status, item.Position },
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

    public async Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.task WHERE project_id = @ProjectId",
            new { ProjectId = projectId },
            cancellationToken: cancellationToken));
    }
}

public sealed class MemberRepository : IMemberRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public MemberRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<MemberWithUser>> ListWithUserAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<MemberWithUser>(new CommandDefinition(
            """
            SELECT m.id, m.user_id, m.role, m.status, m.joined_at, m.last_active_at,
                   u.name, u.email,
                   a.lab_id, a.section_id,
                   l.code AS lab_code, l.name AS lab_name,
                   s.code AS section_code, s.name AS section_name
            FROM dbo.member m
            INNER JOIN dbo.[user] u ON u.id = m.user_id
            LEFT JOIN dbo.user_org_assignment a ON a.user_id = m.user_id
            LEFT JOIN dbo.lab_code l ON l.id = a.lab_id
            LEFT JOIN dbo.section_code s ON s.id = a.section_id
            ORDER BY m.joined_at
            """,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<Member?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Member>(new CommandDefinition(
            """
            SELECT id, user_id, role, status, joined_at, last_active_at
            FROM dbo.member
            WHERE id = @Id
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
    }

    public async Task<Member?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Member>(new CommandDefinition(
            """
            SELECT id, user_id, role, status, joined_at, last_active_at
            FROM dbo.member
            WHERE user_id = @UserId
            """,
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<Guid?> GetFirstMemberIdAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var id = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT TOP 1 id FROM dbo.member ORDER BY joined_at, id",
            cancellationToken: cancellationToken));
        return id;
    }

    public async Task CreateAsync(Member member, CancellationToken cancellationToken = default)
    {
        if (member.Id == Guid.Empty)
        {
            member.Id = Guid.NewGuid();
        }

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.member (id, user_id, role, status, joined_at, last_active_at)
            VALUES (@Id, @UserId, @Role, @Status,
                    COALESCE(@JoinedAt, SYSUTCDATETIME()), @LastActiveAt)
            """,
            new
            {
                member.Id,
                member.UserId,
                member.Role,
                member.Status,
                JoinedAt = member.JoinedAt == default ? (DateTimeOffset?)null : member.JoinedAt,
                member.LastActiveAt
            },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateStatusAsync(Guid id, string status, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.member SET status = @Status WHERE id = @Id",
            new { Id = id, Status = status },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> UpdateRoleAsync(Guid id, string role, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.member SET role = @Role WHERE id = @Id",
            new { Id = id, Role = role },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.member WHERE user_id = @UserId",
            new { UserId = userId },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.member",
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

public sealed class ProjectMemberRepository : IProjectMemberRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProjectMemberRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ProjectMember>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ProjectMember>(new CommandDefinition(
            "SELECT project_id, member_id FROM dbo.project_member",
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Guid>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT member_id FROM dbo.project_member WHERE project_id = @ProjectId",
            new { ProjectId = projectId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task ReplaceForProjectAsync(
        Guid projectId,
        IEnumerable<Guid> memberIds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.project_member WHERE project_id = @ProjectId",
                new { ProjectId = projectId },
                transaction,
                cancellationToken: cancellationToken));

            foreach (var memberId in memberIds.Distinct())
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO dbo.project_member (project_id, member_id) VALUES (@ProjectId, @MemberId)",
                    new { ProjectId = projectId, MemberId = memberId },
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
}
