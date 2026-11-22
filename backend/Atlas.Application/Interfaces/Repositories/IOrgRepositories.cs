using Atlas.Domain.Entities;

namespace Atlas.Application.Interfaces.Repositories;

public interface ILabRepository
{
    Task<IReadOnlyList<LabCode>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<LabCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsCodeAsync(string code, Guid? excludeId, CancellationToken cancellationToken = default);
    Task CreateAsync(LabCode lab, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(LabCode lab, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface ISectionRepository
{
    Task<IReadOnlyList<SectionCode>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<SectionCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsCodeAsync(string code, Guid? excludeId, CancellationToken cancellationToken = default);
    Task CreateAsync(SectionCode section, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(SectionCode section, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IOrgAssignmentRepository
{
    Task<UserOrgAssignment?> GetForUserAsync(string userId, CancellationToken cancellationToken = default);
    Task UpsertAsync(UserOrgAssignment assignment, CancellationToken cancellationToken = default);
    Task<int> CountUsersWithLabAsync(Guid labId, CancellationToken cancellationToken = default);
    Task<int> CountUsersWithSectionAsync(Guid sectionId, CancellationToken cancellationToken = default);
    Task<bool> DeleteForUserAsync(string userId, CancellationToken cancellationToken = default);
}

public interface IOrgAuditRepository
{
    Task InsertAsync(OrgAudit audit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrgAudit>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);
}
