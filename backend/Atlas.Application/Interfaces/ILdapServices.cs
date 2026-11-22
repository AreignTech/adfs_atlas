using Atlas.Domain.Entities;
namespace Atlas.Application.Interfaces;

public sealed record DirectoryUser(string Identifier, string Dn, string Email, string DisplayName, IReadOnlyList<string> Groups);
public interface ISecretProtector { string Protect(string value); string Unprotect(string value); }
public interface ILdapDirectory
{
    Task TestAsync(LdapConfig config, string? username, CancellationToken ct);
    Task<DirectoryUser> AuthenticateAsync(LdapConfig config, string username, string password, CancellationToken ct);
}
public interface ILdapUserWriter
{
    Task<string> LinkAsync(DirectoryUser user, bool autoProvision, bool requireApproval, string? mappedRole, CancellationToken ct);
}
