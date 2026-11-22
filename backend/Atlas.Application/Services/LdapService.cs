using System.Text.Json;
using System.Text.RegularExpressions;
using Atlas.Application.Common;
using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Security;
using Atlas.Domain.Entities;

namespace Atlas.Application.Services;

public sealed class LdapService(ILdapConfigRepository configs, ILdapDirectory directory, ISecretProtector secrets, ILdapGroupMappingRepository mappings, ILdapAuditRepository audits, ILdapUserWriter users, IOrgAssignmentRepository assignments, IAuthService auth, AccessGuard guard)
{
    private Task Audit(string action, bool success, string diagnostic, string? username = null, string? administrator = null, CancellationToken ct = default) => audits.InsertAsync(new LdapAudit { EventType = action, Success = success, Diagnostic = diagnostic, Username = username, Administrator = administrator }, ct);
    public async Task<LdapAdminConfigDto> ConfigAsync(SessionContextDto actor, CancellationToken ct)
    {
        await guard.PermissionAsync(actor, "admin.authentication", ct);
        var config = await configs.GetAsync(ct) ?? new LdapConfig();
        var dto = JsonSerializer.SerializeToElement(config).Deserialize<LdapAdminConfigDto>()!;
        dto.BindPassword = ""; dto.BindPasswordSet = config.BindPasswordEncrypted.Length > 0; dto.TlsCaCertificate = config.TlsCaCertificate.Length > 0 ? "<configured>" : ""; return dto;
    }
    public async Task SaveAsync(SessionContextDto actor, LdapConfigInput input, CancellationToken ct)
    {
        await guard.PermissionAsync(actor, "admin.authentication", ct);
        foreach (var (value, max) in new[] { (input.Host, 255), (input.BaseDn, 1000), (input.BindDn, 1000), (input.SearchBase, 1000), (input.BindPassword, 4000), (input.TlsCaCertificate, 100000) }) WorkspaceService.Text(value, 0, max, "LDAP configuration value");
        if (!new[] { "ldap", "ldaps", "starttls" }.Contains(input.Protocol) || (input.RequireSecure && input.Protocol == "ldap") || input.Port is < 1 or > 65535 || input.ConnectionTimeout is < 1000 or > 120000) throw new ValidationException("Invalid or insecure LDAP connection settings.");
        foreach (var attribute in new[] { input.UserSearchAttribute, input.LoginIdentifierAttribute, input.DirectoryIdentifierAttribute, input.EmailAttribute, input.DisplayNameAttribute }) if (!Regex.IsMatch(attribute, "^[A-Za-z][A-Za-z0-9;-]{0,99}$")) throw new ValidationException("Invalid LDAP attribute name.");
        var previous = await configs.GetAsync(ct); var config = JsonSerializer.SerializeToElement(input).Deserialize<LdapConfig>()!;
        config.BindPasswordEncrypted = input.BindPassword.Length > 0 ? secrets.Protect(input.BindPassword) : previous?.BindPasswordEncrypted ?? "";
        config.TlsCaCertificate = input.TlsCaCertificate == "<configured>" ? previous?.TlsCaCertificate ?? "" : secrets.Protect(input.TlsCaCertificate);
        config.Status = "not-tested"; config.UpdatedBy = actor.User.Email;
        if (config.Enabled && (config.Host.Length == 0 || config.BindDn.Length == 0 || config.BindPasswordEncrypted.Length == 0)) throw new ValidationException("Host, bind DN and bind password are required.");
        await configs.UpsertAsync(config, ct);
    }
    public async Task<object> TestAsync(SessionContextDto actor, string? username, CancellationToken ct)
    {
        await guard.PermissionAsync(actor, "admin.authentication", ct); var config = await configs.GetAsync(ct) ?? throw new ValidationException("LDAP has not been configured.");
        try
        {
            await directory.TestAsync(config, username, ct); await configs.UpdateTestStatusAsync(true, DateTimeOffset.UtcNow, ct, actor.User.Email, username);
            return new { success = true, message = "LDAP directory operation succeeded." };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await configs.UpdateTestStatusAsync(false, DateTimeOffset.UtcNow, ct, actor.User.Email, username);
            throw new ValidationException("LDAP directory operation failed. Verify directory settings and certificates.");
        }
    }
    public async Task<AuthSession> LoginAsync(LdapLoginRequest input, string? ip, string? agent, CancellationToken ct)
    {
        WorkspaceService.Text(input.Username, 1, 256, "directory username"); WorkspaceService.Text(input.Password, 1, 4000, "directory password");
        try
        {
            var config = await configs.GetAsync(ct); if (config is not { Enabled: true }) throw new ForbiddenException("LDAP authentication is not enabled.");
            var identity = await directory.AuthenticateAsync(config, input.Username, input.Password, ct);
            var mappedRole = (await mappings.ListAsync(ct)).FirstOrDefault(m => m.Enabled && identity.Groups.Contains(m.LdapGroup, StringComparer.OrdinalIgnoreCase))?.AtlasRole;
            var userId = await users.LinkAsync(identity, config.AutoProvision, config.RequireApproval, mappedRole, ct);
            var assignment = await assignments.GetForUserAsync(userId, ct);
            if (assignment?.LabId is null || assignment.SectionId is null) throw new ForbiddenException("An administrator must assign your Lab and Section before sign-in.");
            var session = await auth.CreateDirectorySessionAsync(userId, ip, agent, ct); await Audit("ldap.authentication.success", true, "LDAP credentials validated and an ATLAS session was created.", input.Username, ct: ct); return session;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Audit("ldap.authentication.failed", false, "Directory authentication, approval or organizational assignment failed.", input.Username, ct: ct);
            if (ex is ForbiddenException) throw;
            throw new UnauthorizedException("Directory authentication failed.");
        }
    }
    public async Task<object> MappingsAsync(SessionContextDto actor, CancellationToken ct) { await guard.PermissionAsync(actor, "admin.authentication", ct); return await mappings.ListAsync(ct); }
    public async Task SaveMappingsAsync(SessionContextDto actor, List<LdapMappingInput> input, CancellationToken ct)
    {
        await guard.PermissionAsync(actor, "admin.authentication", ct); if (input.Count > 100) throw new ValidationException("Too many mappings.");
        foreach (var mapping in input) { WorkspaceService.Text(mapping.LdapGroup, 1, 1000, "LDAP group"); WorkspaceService.Text(mapping.AtlasRole, 1, 100, "ATLAS role"); }
        await mappings.ReplaceAllAsync(input.Select(m => new LdapGroupMapping { LdapGroup = m.LdapGroup, AtlasRole = m.AtlasRole, Enabled = m.Enabled }), ct, actor.User.Email);
    }
    public async Task<object> AuditsAsync(SessionContextDto actor, CancellationToken ct) { await guard.PermissionAsync(actor, "admin.authentication", ct); return await audits.ListRecentAsync(100, ct); }
    public async Task<bool> EnabledAsync(CancellationToken ct) => (await configs.GetAsync(ct))?.Enabled == true;
}
