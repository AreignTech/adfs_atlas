namespace Atlas.Domain.Entities;

public sealed class LdapConfig
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 389;
    public string Protocol { get; set; } = "ldap";
    public string BaseDn { get; set; } = string.Empty;
    public string BindDn { get; set; } = string.Empty;
    public string BindPasswordEncrypted { get; set; } = string.Empty;
    public string SearchBase { get; set; } = string.Empty;
    public string UserSearchAttribute { get; set; } = "sAMAccountName";
    public string LoginIdentifierAttribute { get; set; } = "sAMAccountName";
    public string EmailAttribute { get; set; } = "mail";
    public string DisplayNameAttribute { get; set; } = "displayName";
    public string DirectoryIdentifierAttribute { get; set; } = "objectGUID";
    public bool RequireSecure { get; set; } = true;
    public bool ValidateTls { get; set; } = true;
    public string TlsCaCertificate { get; set; } = string.Empty;
    public int ConnectionTimeout { get; set; } = 10000;
    public bool AutoProvision { get; set; } = true;
    public bool RequireApproval { get; set; }
    public string Status { get; set; } = "not-tested";
    public DateTimeOffset? LastTestAt { get; set; }
    public DateTimeOffset? LastSuccessfulTestAt { get; set; }
    public DateTimeOffset? LastConfigUpdateAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class LdapIdentity
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string DirectoryIdentifier { get; set; } = string.Empty;
    public string DirectoryDn { get; set; } = string.Empty;
    public DateTimeOffset LinkedAt { get; set; }
}

public sealed class LdapGroupMapping
{
    public Guid Id { get; set; }
    public string LdapGroup { get; set; } = string.Empty;
    public string AtlasRole { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class LdapAudit
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Administrator { get; set; }
    public bool Success { get; set; }
    public string Diagnostic { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
