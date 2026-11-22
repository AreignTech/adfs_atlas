using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Atlas.Application.Common;
using Atlas.Application.Interfaces;
using Atlas.Domain.Entities;

namespace Atlas.Infrastructure.Security;

public sealed class LdapDirectory(ISecretProtector secrets) : ILdapDirectory
{
    public static string EscapeFilter(string value) => value.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");
    public static void Validate(LdapConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Host) || config.Port is < 1 or > 65535 || config.ConnectionTimeout is < 1000 or > 120000) throw new ValidationException("Invalid LDAP connection settings.");
        if (!new[] { "ldap", "ldaps", "starttls" }.Contains(config.Protocol) || (config.RequireSecure && config.Protocol == "ldap")) throw new ValidationException("Unencrypted LDAP is prohibited by the security configuration.");
        foreach (var attribute in new[] { config.UserSearchAttribute, config.DirectoryIdentifierAttribute, config.EmailAttribute, config.DisplayNameAttribute, config.LoginIdentifierAttribute })
            if (!Regex.IsMatch(attribute, "^[A-Za-z][A-Za-z0-9;-]{0,99}$")) throw new ValidationException("Invalid LDAP attribute name.");
    }
    private LdapConnection Open(LdapConfig config)
    {
        Validate(config);
        var connection = new LdapConnection(new LdapDirectoryIdentifier(config.Host, config.Port)) { AuthType = AuthType.Basic, Timeout = TimeSpan.FromMilliseconds(config.ConnectionTimeout) };
        try
        {
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            connection.SessionOptions.SecureSocketLayer = config.Protocol == "ldaps";
            if (!config.ValidateTls) connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
            else if (config.TlsCaCertificate.Length > 0)
            {
                var pem = config.TlsCaCertificate.StartsWith("-----BEGIN", StringComparison.Ordinal) ? config.TlsCaCertificate : secrets.Unprotect(config.TlsCaCertificate);
                var roots = new X509Certificate2Collection(); roots.ImportFromPem(pem);
                connection.SessionOptions.VerifyServerCertificate = (_, certificate) =>
                {
                    using var server = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                    using var chain = new X509Chain(); chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust; chain.ChainPolicy.CustomTrustStore.AddRange(roots); chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    return server.MatchesHostname(config.Host) && chain.Build(server);
                };
            }
            if (config.Protocol == "starttls") connection.SessionOptions.StartTransportLayerSecurity(null);
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    private static async Task<SearchResponse> SearchAsync(LdapConnection connection, SearchRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var pending = connection.BeginSendRequest(request, PartialResultProcessing.NoPartialResultSupport, null, null);
        using var registration = ct.Register(() => { try { connection.Abort(pending); } catch (ObjectDisposedException) { } });
        return (SearchResponse)await Task<DirectoryResponse>.Factory.FromAsync(pending, connection.EndSendRequest);
    }
    private async Task<DirectoryUser> LookupAsync(LdapConnection connection, LdapConfig config, string username, CancellationToken ct)
    {
        var request = new SearchRequest(string.IsNullOrEmpty(config.SearchBase) ? config.BaseDn : config.SearchBase, $"({config.UserSearchAttribute}={EscapeFilter(username)})", SearchScope.Subtree, config.DirectoryIdentifierAttribute, config.EmailAttribute, config.DisplayNameAttribute, "memberOf") { SizeLimit = 2 };
        var response = await SearchAsync(connection, request, ct);
        if (response.Entries.Count != 1) throw new UnauthorizedException("Directory user lookup did not return a unique match.");
        var entry = response.Entries[0];
        string[] Values(string name) => entry.Attributes[name] is not { } attribute ? [] : attribute.Cast<object>().Select(v => v is byte[] bytes ? Encoding.UTF8.GetString(bytes) : v.ToString() ?? "").ToArray();
        return new(Values(config.DirectoryIdentifierAttribute).FirstOrDefault() ?? entry.DistinguishedName, entry.DistinguishedName, Values(config.EmailAttribute).FirstOrDefault() ?? "", Values(config.DisplayNameAttribute).FirstOrDefault() ?? username, Values("memberOf"));
    }
    public Task TestAsync(LdapConfig config, string? username, CancellationToken ct) => Task.Run(async () =>
    {
        using var connection = Open(config); connection.Bind(new NetworkCredential(config.BindDn, secrets.Unprotect(config.BindPasswordEncrypted)));
        if (!string.IsNullOrWhiteSpace(username)) await LookupAsync(connection, config, username, ct);
        else await SearchAsync(connection, new SearchRequest(string.IsNullOrEmpty(config.SearchBase) ? config.BaseDn : config.SearchBase, "(objectClass=*)", SearchScope.Base, "dn"), ct);
    }, ct);
    public Task<DirectoryUser> AuthenticateAsync(LdapConfig config, string username, string password, CancellationToken ct) => Task.Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) throw new UnauthorizedException("Directory username and password are required.");
        using var search = Open(config); search.Bind(new NetworkCredential(config.BindDn, secrets.Unprotect(config.BindPasswordEncrypted)));
        var user = await LookupAsync(search, config, username, ct);
        using var bind = Open(config); bind.Bind(new NetworkCredential(user.Dn, password)); return user;
    }, ct);
}
