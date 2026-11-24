using Atlas.Domain.Entities;

namespace Atlas.Application.Interfaces.Repositories;

public interface ITaskCommentRepository
{
    Task<IReadOnlyList<TaskComment>> ListAsync(Guid taskId, int offset, int limit, CancellationToken ct);
    Task<int> CountAsync(Guid taskId, CancellationToken ct);
    Task<TaskComment?> GetAsync(Guid taskId, Guid id, CancellationToken ct);
    Task<Guid> CreateAsync(Guid taskId, string userId, string content, CancellationToken ct);
    Task<bool> UpdateAsync(Guid taskId, Guid id, string content, CancellationToken ct);
    Task<bool> DeleteAsync(Guid taskId, Guid id, CancellationToken ct);
}
