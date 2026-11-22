using Atlas.Application.Interfaces;
using Atlas.Domain.Entities;
using Dapper;
namespace Atlas.Infrastructure.Repositories;

public sealed class WorkspaceWriter(IDbConnectionFactory factory) : IWorkspaceWriter
{
    public async Task SaveProjectAsync(Project project, IReadOnlyList<Guid> memberIds, bool create, CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (create) project.Id = Guid.NewGuid();
        var sql = create
            ? "INSERT INTO dbo.project(id,name,[key],description,status,color,owner_id,start_date,due_date,lab_id,section_id) VALUES(@Id,@Name,@Key,@Description,@Status,@Color,@OwnerId,@StartDate,@DueDate,@LabId,@SectionId)"
            : "UPDATE dbo.project SET name=@Name,[key]=@Key,description=@Description,status=@Status,color=@Color,owner_id=@OwnerId,start_date=@StartDate,due_date=@DueDate,lab_id=@LabId,section_id=@SectionId,updated_at=SYSUTCDATETIME() WHERE id=@Id";
        if (await connection.ExecuteAsync(new CommandDefinition(sql, project, transaction, cancellationToken: ct)) != 1) throw new InvalidOperationException("Project was not found.");
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.project_member WHERE project_id=@Id", project, transaction, cancellationToken: ct));
        foreach (var memberId in memberIds.Distinct())
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO dbo.project_member(project_id,member_id) VALUES(@ProjectId,@MemberId)", new { ProjectId = project.Id, MemberId = memberId }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
    }
}
