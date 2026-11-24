using Atlas.Application.Interfaces;
using Atlas.Application.Common;
using Atlas.Domain.Entities;
using Dapper;

namespace Atlas.Infrastructure.Repositories;

public sealed class IdentityWriter(IDbConnectionFactory factory) : IIdentityWriter
{
    public async Task<User> AcceptInvitationAsync(string tokenHash, string email, string name, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var c = await factory.OpenAsync(cancellationToken);
        await using var t = await c.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var user = await c.QuerySingleOrDefaultAsync<User>(new CommandDefinition("""
            SELECT u.* FROM dbo.[user] u
            JOIN dbo.member_invitation i WITH(UPDLOCK,HOLDLOCK) ON i.user_id=u.id
            JOIN dbo.member m WITH(UPDLOCK,HOLDLOCK) ON m.user_id=u.id
            WHERE i.token_hash=@TokenHash AND i.expires_at>SYSUTCDATETIME()
              AND u.email=@Email AND m.status='invited'
              AND NOT EXISTS(SELECT 1 FROM dbo.account a WHERE a.user_id=u.id)
            """, new { TokenHash = tokenHash, Email = email }, t, cancellationToken: cancellationToken))
            ?? throw new ValidationException("This invitation is invalid, expired, already accepted, or belongs to another email address.");
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.account(id,account_id,provider_id,user_id,password)
            VALUES(@AccountId,@Id,'credential',@Id,@PasswordHash);
            UPDATE dbo.[user] SET name=@Name,email_verified=1,updated_at=SYSUTCDATETIME() WHERE id=@Id;
            UPDATE dbo.member SET status='active' WHERE user_id=@Id;
            DELETE FROM dbo.member_invitation WHERE user_id=@Id;
            """, new { user.Id, Name = name, PasswordHash = passwordHash, AccountId = Guid.NewGuid().ToString("N") }, t, cancellationToken: cancellationToken));
        await t.CommitAsync(cancellationToken);
        user.Name = name;
        user.EmailVerified = true;
        return user;
    }

    public async Task CreateAsync(User user, Member member, Account? account, string? roleName = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO dbo.[user](id,name,email,email_verified,image) VALUES(@Id,@Name,@Email,@EmailVerified,@Image)", user, transaction, cancellationToken: cancellationToken));
        if (member.Id == Guid.Empty) member.Id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO dbo.member(id,user_id,role,status) VALUES(@Id,@UserId,@Role,@Status)", member, transaction, cancellationToken: cancellationToken));
        if (account is not null)
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO dbo.account(id,account_id,provider_id,user_id,password) VALUES(@Id,@AccountId,@ProviderId,@UserId,@Password)", account, transaction, cancellationToken: cancellationToken));
        if (roleName is not null)
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO dbo.app_user_role(user_id,role_id) SELECT @UserId,id FROM dbo.app_role WHERE name=@RoleName", new { UserId = user.Id, RoleName = roleName }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ChangePasswordAsync(string userId, string passwordHash, string? keepTokenHash, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition("UPDATE dbo.account SET password=@PasswordHash,updated_at=SYSUTCDATETIME() WHERE user_id=@UserId AND provider_id='credential';", new { UserId = userId, PasswordHash = passwordHash }, transaction, cancellationToken: cancellationToken));
        if (affected != 1) throw new InvalidOperationException("Expected one credential account.");
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.session WHERE user_id=@UserId AND (@KeepTokenHash IS NULL OR token<>@KeepTokenHash)", new { UserId = userId, KeepTokenHash = keepTokenHash }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }
}
