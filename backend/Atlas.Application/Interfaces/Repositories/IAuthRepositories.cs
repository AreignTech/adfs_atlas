using Atlas.Domain.Entities;

namespace Atlas.Application.Interfaces.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    Task CreateAsync(User user, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(User user, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface ISessionRepository
{
    Task CreateAsync(Session session, CancellationToken cancellationToken = default);
    Task<Session?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> DeleteByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> DeleteByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<int> DeleteAllForUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteAllExceptAsync(string userId, string? keepToken, CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task TouchAsync(string id, DateTimeOffset expiresAt, DateTimeOffset updatedAt, CancellationToken cancellationToken = default);
}

public interface IAccountRepository
{
    Task<Account?> GetCredentialAsync(string userId, CancellationToken cancellationToken = default);
    Task<Account?> GetByProviderAccountAsync(string providerId, string accountId, CancellationToken cancellationToken = default);
    Task CreateAsync(Account account, CancellationToken cancellationToken = default);
    Task<bool> UpdatePasswordAsync(string userId, string passwordHash, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface IVerificationRepository
{
    Task<Verification?> GetAsync(string identifier, CancellationToken cancellationToken = default);
    Task CreateAsync(Verification verification, CancellationToken cancellationToken = default);
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
