using Atlas.Domain.Entities;

namespace Atlas.Application.Interfaces.Repositories;

public interface IRoleRepository
{
    Task<IReadOnlyList<AppRole>> ListAsync(CancellationToken cancellationToken = default);
    Task<AppRole?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AppRole?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<bool> ExistsNameAsync(string name, Guid? excludeId, CancellationToken cancellationToken = default);
    Task CreateAsync(AppRole role, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(AppRole role, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> CountAssignedUsersAsync(Guid roleId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAssignedUserNamesAsync(Guid roleId, CancellationToken cancellationToken = default);
}

public interface IPermissionRepository
{
    Task<IReadOnlyList<AppPermission>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppPermission>> GetByKeysAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppPermission>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}

public interface IUserRoleRepository
{
    Task<UserRoleAssignment?> GetForUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserRoleAssignment>> ListForUserAsync(string userId, CancellationToken cancellationToken = default);
    Task ReplaceForUserAsync(string userId, Guid roleId, string? assignedBy, CancellationToken cancellationToken = default);
    Task DeleteForUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken = default);
}

public interface IRolePermissionRepository
{
    Task<IReadOnlyList<string>> ListKeysForRolesAsync(IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
    Task ReplaceForRoleAsync(Guid roleId, IEnumerable<Guid> permissionIds, CancellationToken cancellationToken = default);
    Task DeleteForRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
}

public interface IRbacAuditRepository
{
    Task InsertAsync(RbacAudit audit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RbacAudit>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);
}
