using Atlas.Application.Interfaces;
using Atlas.Domain.Entities;
using Dapper;

namespace Atlas.Infrastructure.Repositories;

public sealed class IdentityWriter(IDbConnectionFactory factory) : IIdentityWriter
{
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
