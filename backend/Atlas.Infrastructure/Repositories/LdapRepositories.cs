using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Dapper;

namespace Atlas.Infrastructure.Repositories;

public sealed class LdapConfigRepository : ILdapConfigRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public LdapConfigRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string SelectSql = """
        SELECT id, enabled, host, port, protocol, base_dn, bind_dn, bind_password_encrypted,
               search_base, user_search_attribute, login_identifier_attribute, email_attribute,
               display_name_attribute, directory_identifier_attribute, require_secure, validate_tls,
               tls_ca_certificate, connection_timeout, auto_provision, require_approval, status,
               last_test_at, last_successful_test_at, last_config_update_at, updated_by,
               created_at, updated_at
        FROM dbo.ldap_config
        WHERE id = 1
        """;

    public async Task<LdapConfig?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<LdapConfig>(new CommandDefinition(
            SelectSql,
            cancellationToken: cancellationToken));
    }

    public async Task UpsertAsync(LdapConfig config, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            IF EXISTS (SELECT 1 FROM dbo.ldap_config WITH(UPDLOCK,HOLDLOCK) WHERE id = 1)
            BEGIN
                UPDATE dbo.ldap_config
                SET enabled = @Enabled,
                    host = @Host,
                    port = @Port,
                    protocol = @Protocol,
                    base_dn = @BaseDn,
                    bind_dn = @BindDn,
                    bind_password_encrypted = @BindPasswordEncrypted,
                    search_base = @SearchBase,
                    user_search_attribute = @UserSearchAttribute,
                    login_identifier_attribute = @LoginIdentifierAttribute,
                    email_attribute = @EmailAttribute,
                    display_name_attribute = @DisplayNameAttribute,
                    directory_identifier_attribute = @DirectoryIdentifierAttribute,
                    require_secure = @RequireSecure,
                    validate_tls = @ValidateTls,
                    tls_ca_certificate = @TlsCaCertificate,
                    connection_timeout = @ConnectionTimeout,
                    auto_provision = @AutoProvision,
                    require_approval = @RequireApproval,
                    status = @Status,
                    last_config_update_at = SYSUTCDATETIME(),
                    updated_by = @UpdatedBy,
                    updated_at = SYSUTCDATETIME()
                WHERE id = 1;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.ldap_config
                    (id, enabled, host, port, protocol, base_dn, bind_dn, bind_password_encrypted,
                     search_base, user_search_attribute, login_identifier_attribute, email_attribute,
                     display_name_attribute, directory_identifier_attribute, require_secure, validate_tls,
                     tls_ca_certificate, connection_timeout, auto_provision, require_approval, status,
                     last_config_update_at, updated_by)
                VALUES
                    (1, @Enabled, @Host, @Port, @Protocol, @BaseDn, @BindDn, @BindPasswordEncrypted,
                     @SearchBase, @UserSearchAttribute, @LoginIdentifierAttribute, @EmailAttribute,
                     @DisplayNameAttribute, @DirectoryIdentifierAttribute, @RequireSecure, @ValidateTls,
                     @TlsCaCertificate, @ConnectionTimeout, @AutoProvision, @RequireApproval, @Status,
                     SYSUTCDATETIME(), @UpdatedBy);
            END
            """,
            new
            {
                config.Enabled,
                config.Host,
                config.Port,
                config.Protocol,
                config.BaseDn,
                config.BindDn,
                config.BindPasswordEncrypted,
                config.SearchBase,
                config.UserSearchAttribute,
                config.LoginIdentifierAttribute,
                config.EmailAttribute,
                config.DisplayNameAttribute,
                config.DirectoryIdentifierAttribute,
                config.RequireSecure,
                config.ValidateTls,
                config.TlsCaCertificate,
                config.ConnectionTimeout,
                config.AutoProvision,
                config.RequireApproval,
                Status = string.IsNullOrWhiteSpace(config.Status) ? "not-tested" : config.Status,
                config.UpdatedBy
            },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO dbo.ldap_audit(event_type,administrator,success,diagnostic) VALUES('ldap.configuration.changed',@Administrator,1,'LDAP configuration updated.')", new { Administrator = config.UpdatedBy }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateTestStatusAsync(
        bool success,
        DateTimeOffset testedAt,
        CancellationToken cancellationToken = default,
        string? administrator = null,
        string? username = null)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.ldap_config
            SET status = @Status,
                last_test_at = @TestedAt,
                last_successful_test_at = CASE WHEN @Success = 1 THEN @TestedAt ELSE last_successful_test_at END,
                updated_at = SYSUTCDATETIME()
            WHERE id = 1
            """,
            new
            {
                Status = success ? "successful" : "failed",
                TestedAt = testedAt,
                Success = success ? 1 : 0
            },
            transaction,
            cancellationToken: cancellationToken));
        if (administrator is not null) await connection.ExecuteAsync(new CommandDefinition("INSERT INTO dbo.ldap_audit(event_type,username,administrator,success,diagnostic) VALUES('ldap.connection.test',@Username,@Administrator,@Success,@Diagnostic)", new { Username = username, Administrator = administrator, Success = success, Diagnostic = success ? "LDAP directory operation succeeded." : "LDAP directory operation failed. Verify directory settings and certificates." }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class LdapIdentityRepository : ILdapIdentityRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public LdapIdentityRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<LdapIdentity?> GetByDirectoryIdentifierAsync(
        string directoryIdentifier,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<LdapIdentity>(new CommandDefinition(
            """
            SELECT id, user_id, directory_identifier, directory_dn, linked_at
            FROM dbo.ldap_identity
            WHERE directory_identifier = @DirectoryIdentifier
            """,
            new { DirectoryIdentifier = directoryIdentifier },
            cancellationToken: cancellationToken));
    }

    public async Task<LdapIdentity?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<LdapIdentity>(new CommandDefinition(
            """
            SELECT id, user_id, directory_identifier, directory_dn, linked_at
            FROM dbo.ldap_identity
            WHERE user_id = @UserId
            """,
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task CreateAsync(LdapIdentity identity, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.ldap_identity (id, user_id, directory_identifier, directory_dn, linked_at)
            VALUES (@Id, @UserId, @DirectoryIdentifier, @DirectoryDn,
                    COALESCE(@LinkedAt, SYSUTCDATETIME()))
            """,
            new
            {
                Id = identity.Id == Guid.Empty ? Guid.NewGuid() : identity.Id,
                identity.UserId,
                identity.DirectoryIdentifier,
                identity.DirectoryDn,
                LinkedAt = identity.LinkedAt == default ? (DateTimeOffset?)null : identity.LinkedAt
            },
            cancellationToken: cancellationToken));
    }
}

public sealed class LdapGroupMappingRepository : ILdapGroupMappingRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public LdapGroupMappingRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<LdapGroupMapping>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<LdapGroupMapping>(new CommandDefinition(
            """
            SELECT id, ldap_group, atlas_role, enabled, updated_at
            FROM dbo.ldap_group_mapping
            ORDER BY ldap_group
            """,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task ReplaceAllAsync(
        IEnumerable<LdapGroupMapping> mappings,
        CancellationToken cancellationToken = default,
        string? administrator = null)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.ldap_group_mapping",
                transaction: transaction,
                cancellationToken: cancellationToken));

            foreach (var mapping in mappings)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.ldap_group_mapping (id, ldap_group, atlas_role, enabled)
                    VALUES (@Id, @LdapGroup, @AtlasRole, @Enabled)
                    """,
                    new
                    {
                        Id = mapping.Id == Guid.Empty ? Guid.NewGuid() : mapping.Id,
                        mapping.LdapGroup,
                        mapping.AtlasRole,
                        mapping.Enabled
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            if (administrator is not null) await connection.ExecuteAsync(new CommandDefinition("INSERT INTO dbo.ldap_audit(event_type,administrator,success,diagnostic) VALUES('ldap.group_mapping.changed',@Administrator,1,'LDAP group mappings updated.')", new { Administrator = administrator }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

public sealed class LdapAuditRepository : ILdapAuditRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public LdapAuditRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InsertAsync(LdapAudit audit, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.ldap_audit (id, event_type, username, administrator, success, diagnostic)
            VALUES (@Id, @EventType, @Username, @Administrator, @Success, @Diagnostic)
            """,
            new
            {
                Id = audit.Id == Guid.Empty ? Guid.NewGuid() : audit.Id,
                audit.EventType,
                audit.Username,
                audit.Administrator,
                audit.Success,
                Diagnostic = audit.Diagnostic ?? string.Empty
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<LdapAudit>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<LdapAudit>(new CommandDefinition(
            """
            SELECT TOP (@Limit)
                id, event_type, username, administrator, success, diagnostic, created_at
            FROM dbo.ldap_audit
            ORDER BY created_at
            """,
            new { Limit = limit },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
