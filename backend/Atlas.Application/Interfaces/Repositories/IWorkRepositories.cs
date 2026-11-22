using Atlas.Application.DTOs;
using Atlas.Domain.Entities;

namespace Atlas.Application.Interfaces.Repositories;

public sealed record OrgScopeFilter(bool IsAdministrator, Guid? LabId, Guid? SectionId);

public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> ListAsync(OrgScopeFilter? scope, CancellationToken cancellationToken = default);
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task CreateAsync(Project project, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Project project, CancellationToken cancellationToken = default);
    Task<bool> SetStatusAsync(Guid id, string status, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> CountOwnedAsync(Guid memberId, CancellationToken cancellationToken = default);
    Task<int> TransferOwnershipAsync(Guid fromMemberId, Guid toMemberId, CancellationToken cancellationToken = default);
}

public interface ITaskRepository
{
    Task<IReadOnlyList<TaskItem>> ListAsync(OrgScopeFilter? scope, CancellationToken cancellationToken = default);
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task CreateAsync(TaskItem task, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(TaskItem task, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> NextPositionAsync(Guid projectId, string status, CancellationToken cancellationToken = default);
    Task ReorderAsync(Guid projectId, IReadOnlyList<TaskReorderItem> items, CancellationToken cancellationToken = default);
    Task<int> CountByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public interface IMemberRepository
{
    Task<IReadOnlyList<MemberWithUser>> ListWithUserAsync(CancellationToken cancellationToken = default);
    Task<Member?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Member?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<Guid?> GetFirstMemberIdAsync(CancellationToken cancellationToken = default);
    Task CreateAsync(Member member, CancellationToken cancellationToken = default);
    Task<bool> UpdateStatusAsync(Guid id, string status, CancellationToken cancellationToken = default);
    Task<bool> UpdateRoleAsync(Guid id, string role, CancellationToken cancellationToken = default);
    Task<bool> DeleteByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken = default);
}

public interface IProjectMemberRepository
{
    Task<IReadOnlyList<ProjectMember>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task ReplaceForProjectAsync(Guid projectId, IEnumerable<Guid> memberIds, CancellationToken cancellationToken = default);
}
