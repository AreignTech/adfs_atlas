using Atlas.Domain.Entities;

namespace Atlas.Application.Interfaces.Repositories;

public interface ILdapConfigRepository
{
    Task<LdapConfig?> GetAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(LdapConfig config, CancellationToken cancellationToken = default);
    Task UpdateTestStatusAsync(bool success, DateTimeOffset testedAt, CancellationToken cancellationToken = default, string? administrator = null, string? username = null);
}

public interface ILdapIdentityRepository
{
    Task<LdapIdentity?> GetByDirectoryIdentifierAsync(string directoryIdentifier, CancellationToken cancellationToken = default);
    Task<LdapIdentity?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task CreateAsync(LdapIdentity identity, CancellationToken cancellationToken = default);
}

public interface ILdapGroupMappingRepository
{
    Task<IReadOnlyList<LdapGroupMapping>> ListAsync(CancellationToken cancellationToken = default);
    Task ReplaceAllAsync(IEnumerable<LdapGroupMapping> mappings, CancellationToken cancellationToken = default, string? administrator = null);
}

public interface ILdapAuditRepository
{
    Task InsertAsync(LdapAudit audit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LdapAudit>> ListRecentAsync(int limit, CancellationToken cancellationToken = default);
}
