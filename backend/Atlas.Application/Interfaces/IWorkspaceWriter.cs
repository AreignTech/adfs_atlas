using Atlas.Domain.Entities;
namespace Atlas.Application.Interfaces;
public interface IWorkspaceWriter
{
    Task SaveProjectAsync(Project project, IReadOnlyList<Guid> memberIds, bool create, CancellationToken ct = default);
}
