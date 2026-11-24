using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Dapper;

namespace Atlas.Infrastructure.Repositories;

public sealed class TaskCommentRepository(IDbConnectionFactory connections) : ITaskCommentRepository
{
    private const string Projection = """
        SELECT c.*, u.name AS AuthorName, u.image AS AuthorImage
        FROM dbo.task_comment c INNER JOIN dbo.[user] u ON u.id=c.user_id
        """;

    public async Task<IReadOnlyList<TaskComment>> ListAsync(Guid taskId, int offset, int limit, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return (await connection.QueryAsync<TaskComment>(new CommandDefinition(Projection + """
             WHERE c.task_id=@TaskId ORDER BY c.created_at DESC, c.id DESC
             OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY
            """, new { TaskId = taskId, Offset = offset, Limit = limit }, cancellationToken: ct))).AsList();
    }

    public async Task<int> CountAsync(Guid taskId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.task_comment WHERE task_id=@TaskId", new { TaskId = taskId }, cancellationToken: ct));
    }

    public async Task<TaskComment?> GetAsync(Guid taskId, Guid id, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<TaskComment>(new CommandDefinition(
            Projection + " WHERE c.task_id=@TaskId AND c.id=@Id", new { TaskId = taskId, Id = id }, cancellationToken: ct));
    }

    public async Task<Guid> CreateAsync(Guid taskId, string userId, string content, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.QuerySingleAsync<Guid>(new CommandDefinition("""
            INSERT INTO dbo.task_comment(task_id,user_id,content) OUTPUT inserted.id
            VALUES(@TaskId,@UserId,@Content)
            """, new { TaskId = taskId, UserId = userId, Content = content }, cancellationToken: ct));
    }

    public async Task<bool> UpdateAsync(Guid taskId, Guid id, string content, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.task_comment SET content=@Content,updated_at=SYSUTCDATETIME()
            WHERE task_id=@TaskId AND id=@Id
            """, new { TaskId = taskId, Id = id, Content = content }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> DeleteAsync(Guid taskId, Guid id, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.task_comment WHERE task_id=@TaskId AND id=@Id", new { TaskId = taskId, Id = id }, cancellationToken: ct)) > 0;
    }
}
