using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Dapper;

namespace Atlas.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public UserRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            """
            SELECT id, name, email, email_verified, image, created_at, updated_at
            FROM dbo.[user]
            WHERE id = @Id
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
            """
            SELECT id, name, email, email_verified, image, created_at, updated_at
            FROM dbo.[user]
            WHERE email = @Email
            """,
            new { Email = email.Trim().ToLowerInvariant() },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<User>(new CommandDefinition(
            """
            SELECT id, name, email, email_verified, image, created_at, updated_at
            FROM dbo.[user]
            ORDER BY name
            """,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.[user] WHERE email = @Email",
            new { Email = email.Trim().ToLowerInvariant() },
            cancellationToken: cancellationToken));
        return count > 0;
    }

    public async Task CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.[user] (id, name, email, email_verified, image, created_at, updated_at)
            VALUES (@Id, @Name, @Email, @EmailVerified, @Image,
                    COALESCE(@CreatedAt, SYSUTCDATETIME()),
                    COALESCE(@UpdatedAt, SYSUTCDATETIME()))
            """,
            new
            {
                user.Id,
                user.Name,
                Email = user.Email.Trim().ToLowerInvariant(),
                user.EmailVerified,
                user.Image,
                CreatedAt = user.CreatedAt == default ? (DateTimeOffset?)null : user.CreatedAt,
                UpdatedAt = user.UpdatedAt == default ? (DateTimeOffset?)null : user.UpdatedAt
            },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.[user]
            SET name = @Name,
                email = @Email,
                email_verified = @EmailVerified,
                image = @Image,
                updated_at = SYSUTCDATETIME()
            WHERE id = @Id
            """,
            new
            {
                user.Id,
                user.Name,
                Email = user.Email.Trim().ToLowerInvariant(),
                user.EmailVerified,
                user.Image
            },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.[user] WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return affected > 0;
    }
}

public sealed class SessionRepository : ISessionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SessionRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(Session session, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.session (id, expires_at, token, created_at, updated_at, ip_address, user_agent, user_id)
            VALUES (@Id, @ExpiresAt, @Token,
                    COALESCE(@CreatedAt, SYSUTCDATETIME()),
                    COALESCE(@UpdatedAt, SYSUTCDATETIME()),
                    @IpAddress, @UserAgent, @UserId);
            UPDATE dbo.member SET last_active_at=SYSUTCDATETIME(),
                status=CASE WHEN status='invited' THEN 'active' ELSE status END
            WHERE user_id=@UserId;
            DELETE FROM dbo.member_invitation WHERE user_id=@UserId;
            """,
            new
            {
                session.Id,
                session.ExpiresAt,
                session.Token,
                CreatedAt = session.CreatedAt == default ? (DateTimeOffset?)null : session.CreatedAt,
                UpdatedAt = session.UpdatedAt == default ? (DateTimeOffset?)null : session.UpdatedAt,
                session.IpAddress,
                session.UserAgent,
                session.UserId
            },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Session?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Session>(new CommandDefinition(
            """
            SELECT id, expires_at, token, created_at, updated_at, ip_address, user_agent, user_id
            FROM dbo.session
            WHERE token = @Token
            """,
            new { Token = token },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.session WHERE token = @Token",
            new { Token = token },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.session WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<int> DeleteAllForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.session WHERE user_id = @UserId",
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> DeleteAllExceptAsync(
        string userId,
        string? keepToken,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.session
            WHERE user_id = @UserId
              AND (@KeepToken IS NULL OR token <> @KeepToken)
            """,
            new { UserId = userId, KeepToken = keepToken },
            cancellationToken: cancellationToken));
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.session WHERE expires_at <= @Now",
            new { Now = now },
            cancellationToken: cancellationToken));
    }

    public async Task TouchAsync(
        string id,
        DateTimeOffset expiresAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.session SET expires_at = @ExpiresAt, updated_at = @UpdatedAt WHERE id = @Id",
            new { Id = id, ExpiresAt = expiresAt, UpdatedAt = updatedAt },
            cancellationToken: cancellationToken));
    }
}

public sealed class AccountRepository : IAccountRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AccountRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Account?> GetCredentialAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Account>(new CommandDefinition(
            """
            SELECT id, account_id, provider_id, user_id, access_token, refresh_token, id_token,
                   access_token_expires_at, refresh_token_expires_at, scope, password, created_at, updated_at
            FROM dbo.account
            WHERE user_id = @UserId AND provider_id = N'credential'
            """,
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<Account?> GetByProviderAccountAsync(
        string providerId,
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Account>(new CommandDefinition(
            """
            SELECT id, account_id, provider_id, user_id, access_token, refresh_token, id_token,
                   access_token_expires_at, refresh_token_expires_at, scope, password, created_at, updated_at
            FROM dbo.account
            WHERE provider_id = @ProviderId AND account_id = @AccountId
            """,
            new { ProviderId = providerId, AccountId = accountId },
            cancellationToken: cancellationToken));
    }

    public async Task CreateAsync(Account account, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.account
                (id, account_id, provider_id, user_id, access_token, refresh_token, id_token,
                 access_token_expires_at, refresh_token_expires_at, scope, password, created_at, updated_at)
            VALUES
                (@Id, @AccountId, @ProviderId, @UserId, @AccessToken, @RefreshToken, @IdToken,
                 @AccessTokenExpiresAt, @RefreshTokenExpiresAt, @Scope, @Password,
                 COALESCE(@CreatedAt, SYSUTCDATETIME()),
                 COALESCE(@UpdatedAt, SYSUTCDATETIME()))
            """,
            new
            {
                account.Id,
                account.AccountId,
                account.ProviderId,
                account.UserId,
                account.AccessToken,
                account.RefreshToken,
                account.IdToken,
                account.AccessTokenExpiresAt,
                account.RefreshTokenExpiresAt,
                account.Scope,
                account.Password,
                CreatedAt = account.CreatedAt == default ? (DateTimeOffset?)null : account.CreatedAt,
                UpdatedAt = account.UpdatedAt == default ? (DateTimeOffset?)null : account.UpdatedAt
            },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdatePasswordAsync(
        string userId,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.account
            SET password = @Password, updated_at = SYSUTCDATETIME()
            WHERE user_id = @UserId AND provider_id = N'credential'
            """,
            new { UserId = userId, Password = passwordHash },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.account WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return affected > 0;
    }
}

public sealed class VerificationRepository : IVerificationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public VerificationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Verification?> GetAsync(string identifier, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Verification>(new CommandDefinition(
            """
            SELECT id, identifier, value, expires_at, created_at, updated_at
            FROM dbo.verification
            WHERE identifier = @Identifier
            """,
            new { Identifier = identifier },
            cancellationToken: cancellationToken));
    }

    public async Task CreateAsync(Verification verification, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.verification (id, identifier, value, expires_at, created_at, updated_at)
            VALUES (@Id, @Identifier, @Value, @ExpiresAt,
                    COALESCE(@CreatedAt, SYSUTCDATETIME()),
                    COALESCE(@UpdatedAt, SYSUTCDATETIME()))
            """,
            new
            {
                verification.Id,
                verification.Identifier,
                verification.Value,
                verification.ExpiresAt,
                CreatedAt = verification.CreatedAt == default ? (DateTimeOffset?)null : verification.CreatedAt,
                UpdatedAt = verification.UpdatedAt == default ? (DateTimeOffset?)null : verification.UpdatedAt
            },
            cancellationToken: cancellationToken));
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.verification WHERE expires_at <= @Now",
            new { Now = now },
            cancellationToken: cancellationToken));
    }
}
