using System.Security.Cryptography;
using Atlas.Application.Common;
using Atlas.Application.Interfaces;
using Atlas.Infrastructure.Security;
using Atlas.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Atlas.Tests;

public sealed class LdapTests
{
    private static LdapSecretProtector Protector(string key = "compatibility-key", string previous = "") => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ldap:EncryptionKey"] = key, ["Ldap:PreviousEncryptionKeys"] = previous }).Build());
    [Fact]
    public void NodeAesGcmEnvelopeIsCompatibleAndTamperingRejected()
    {
        var protector = Protector(); const string envelope = "AAECAwQFBgcICQoL.62EkWABbG6CqacBX0jRWDQ==.AQa6ldQnWQHwgZkV8MRdEw==";
        Assert.Equal("LDAP test secret", protector.Unprotect(envelope));
        var parts = envelope.Split('.'); var tampered = Convert.FromBase64String(parts[2]); tampered[0] ^= 1; parts[2] = Convert.ToBase64String(tampered);
        Assert.Throws<CryptographicException>(() => protector.Unprotect(string.Join('.', parts)));
        Assert.Equal("CA certificate", protector.Unprotect(protector.Protect("CA certificate")));
        Assert.Throws<CryptographicException>(() => Protector("wrong-key").Unprotect(envelope));
        Assert.Equal("LDAP test secret", Protector("new-key", "compatibility-key").Unprotect(envelope));
        Assert.Equal("value", Protector("new-key").Unprotect(Protector("new-key", "compatibility-key").Protect("value")));
    }
    [Fact]
    public void InsecureLdapAndFilterInjectionAreBlocked()
    {
        Assert.Throws<ValidationException>(() => LdapDirectory.Validate(new LdapConfig { Host = "localhost", Protocol = "ldap", RequireSecure = true }));
        Assert.Throws<ValidationException>(() => LdapDirectory.Validate(new LdapConfig { Host = "localhost", Protocol = "ldaps", UserSearchAttribute = "uid)(objectClass=*" }));
        Assert.Equal("a\\2a\\28\\29\\5c\\00", LdapDirectory.EscapeFilter("a*()\\\0"));
    }
    [Theory, InlineData("ldaps", 1636), InlineData("starttls", 1389)]
    [Trait("Category", "LdapIntegration")]
    public async Task LiveTlsBindLookupAndAuthentication(string protocol, int port)
    {
        var protector = Protector(); var directory = new LdapDirectory(protector);
        var ca = Environment.GetEnvironmentVariable("ATLAS_LDAP_TEST_CA") ?? throw new InvalidOperationException("Run scripts/test-backend.ps1 to configure the disposable LDAP fixture.");
        var config = new LdapConfig { Host = "localhost", Protocol = protocol, Port = port, BaseDn = "dc=atlas,dc=test", BindDn = "cn=admin,dc=atlas,dc=test", BindPasswordEncrypted = protector.Protect("DirectoryTest123!"), UserSearchAttribute = "uid", DirectoryIdentifierAttribute = "uid", DisplayNameAttribute = "cn", TlsCaCertificate = protector.Protect(ca), ValidateTls = true, RequireSecure = true };
        await directory.TestAsync(config, "atlas-test", default);
        var user = await directory.AuthenticateAsync(config, "atlas-test", "UserDirectory123!", default);
        Assert.Equal("atlas-test", user.Identifier); Assert.Equal("ldap-user@atlas.test", user.Email);
        await Assert.ThrowsAnyAsync<Exception>(() => directory.AuthenticateAsync(config, "atlas-test", "incorrect", default));
        config.TlsCaCertificate = "";
        await Assert.ThrowsAnyAsync<Exception>(() => directory.TestAsync(config, "atlas-test", default));
    }
}
