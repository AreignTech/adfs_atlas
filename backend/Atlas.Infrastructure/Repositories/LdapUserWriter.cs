using System.Data;
using Atlas.Application.Common;
using Atlas.Application.Interfaces;
using Atlas.Domain.Entities;
using Dapper;
namespace Atlas.Infrastructure.Repositories;

public sealed class LdapUserWriter(IDbConnectionFactory factory) : ILdapUserWriter
{
    public async Task<string> LinkAsync(DirectoryUser directory, bool autoProvision, bool requireApproval, string? mappedRole, CancellationToken ct)
    {
        await using var c = await factory.OpenAsync(ct); await using var t = await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var userId = await c.QuerySingleOrDefaultAsync<string>("SELECT user_id FROM dbo.ldap_identity WITH(UPDLOCK,HOLDLOCK) WHERE directory_identifier=@Identifier", directory, t);
        if (userId is null && directory.Email.Length > 0) userId = await c.QuerySingleOrDefaultAsync<string>("SELECT id FROM dbo.[user] WHERE email=@Email", new { Email = directory.Email.ToLowerInvariant() }, t);
        if (userId is null)
        {
            if (!autoProvision || requireApproval) throw new ForbiddenException("This directory account has not been approved for ATLAS.");
            if (directory.Email.Length == 0) throw new ValidationException("Directory user has no email address.");
            userId = Guid.NewGuid().ToString();
            await c.ExecuteAsync("INSERT INTO dbo.[user](id,name,email,email_verified) VALUES(@Id,@Name,@Email,1); INSERT INTO dbo.member(id,user_id,role,status) VALUES(NEWID(),@Id,@Role,'active'); INSERT INTO dbo.account(id,account_id,provider_id,user_id) VALUES(CONVERT(nvarchar(255),NEWID()),@Identifier,'ldap',@Id)", new { Id = userId, Name = directory.DisplayName, Email = directory.Email.ToLowerInvariant(), Role = mappedRole ?? "Users", directory.Identifier }, t);
        }
        else
        {
            await c.ExecuteAsync("UPDATE dbo.[user] SET name=CASE WHEN @Name='' THEN name ELSE @Name END,email=CASE WHEN @Email='' THEN email ELSE @Email END,email_verified=1,updated_at=SYSUTCDATETIME() WHERE id=@Id", new { Id = userId, Name = directory.DisplayName, Email = directory.Email.ToLowerInvariant() }, t);
            if (mappedRole is not null) await c.ExecuteAsync("UPDATE dbo.member SET role=@Role,status='active' WHERE user_id=@Id", new { Id = userId, Role = mappedRole }, t);
        }
        await c.ExecuteAsync("IF NOT EXISTS(SELECT 1 FROM dbo.ldap_identity WHERE user_id=@Id) INSERT INTO dbo.ldap_identity(id,user_id,directory_identifier,directory_dn) VALUES(NEWID(),@Id,@Identifier,@Dn)", new { Id = userId, directory.Identifier, directory.Dn }, t);
        if (mappedRole is not null)
        {
            var roleName = mappedRole switch { "Member" => "Users", "Administrator" => "Administrators", _ => mappedRole };
            var roleId = await c.QuerySingleOrDefaultAsync<Guid?>("SELECT id FROM dbo.app_role WHERE name=@Name", new { Name = roleName }, t);
            if (roleId.HasValue) await c.ExecuteAsync("DELETE FROM dbo.app_user_role WHERE user_id=@Id; INSERT INTO dbo.app_user_role(user_id,role_id,assigned_by) VALUES(@Id,@RoleId,'LDAP authentication')", new { Id = userId, RoleId = roleId.Value }, t);
        }
        await t.CommitAsync(ct); return userId;
    }
}
