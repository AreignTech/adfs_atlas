using Atlas.Application.Common;
using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Services;
using Atlas.Domain.Entities;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Atlas.Tests;

public sealed class LdapWorkflowTests
{
    private sealed class DirectoryFixture(string email) : ILdapDirectory
    {
        public Task TestAsync(LdapConfig config, string? username, CancellationToken ct) => Task.CompletedTask;
        public Task<DirectoryUser> AuthenticateAsync(LdapConfig config, string username, string password, CancellationToken ct) => Task.FromResult(new DirectoryUser(email, $"uid={email}", email, "Directory User", ["cn=atlas-admins"]));
    }
    private sealed class Factory(string email) : AtlasFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) { base.ConfigureWebHost(builder); builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Scoped<ILdapDirectory>(_ => new DirectoryFixture(email)))); }
    }
    [Fact]
    public async Task MappingProvisioningOrganizationApprovalRedactionAndAudits()
    {
        var email = $"{Guid.NewGuid():N}@directory.test"; await using var factory = new Factory(email); using var scope = factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        var configs = sp.GetRequiredService<ILdapConfigRepository>(); var mappings = sp.GetRequiredService<ILdapGroupMappingRepository>(); var originalConfig = await configs.GetAsync(); var originalMappings = await mappings.ListAsync();
        var lab = new LabCode { Code = Guid.NewGuid().ToString("N"), Name = "LDAP lab" }; var section = new SectionCode { Code = Guid.NewGuid().ToString("N"), Name = "LDAP section" };
        var actor = new SessionContextDto(new("test", "Test", "test@atlas.test", false, null), ["Administrators"], ["admin.authentication"], true, new(null, null, false, true));
        string? userId = null;
        try
        {
            await sp.GetRequiredService<ILabRepository>().CreateAsync(lab); await sp.GetRequiredService<ISectionRepository>().CreateAsync(section);
            var service = sp.GetRequiredService<LdapService>();
            await service.SaveAsync(actor, new LdapConfigInput { Enabled = true, Host = "localhost", Protocol = "ldaps", BindDn = "cn=admin", BindPassword = "DoNotExpose123!", TlsCaCertificate = "Private CA contents" }, default);
            var stored = (await configs.GetAsync())!; Assert.DoesNotContain("DoNotExpose", stored.BindPasswordEncrypted); Assert.DoesNotContain("Private CA", stored.TlsCaCertificate);
            var dto = await service.ConfigAsync(actor, default); Assert.Equal("", dto.BindPassword); Assert.True(dto.BindPasswordSet); Assert.Equal("<configured>", dto.TlsCaCertificate);
            await service.SaveMappingsAsync(actor, [new LdapMappingInput { LdapGroup = "cn=atlas-admins", AtlasRole = "Administrator" }], default);
            await Assert.ThrowsAsync<ForbiddenException>(() => service.LoginAsync(new() { Username = "test", Password = "password" }, null, null, default));
            var user = (await sp.GetRequiredService<IUserRepository>().GetByEmailAsync(email))!; userId = user.Id;
            Assert.Equal("Administrators", (await sp.GetRequiredService<IUserRoleRepository>().GetForUserAsync(user.Id))!.RoleName);
            await sp.GetRequiredService<IOrgAssignmentRepository>().UpsertAsync(new() { UserId = user.Id, LabId = lab.Id, SectionId = section.Id });
            var session = await service.LoginAsync(new() { Username = "test", Password = "password" }, null, null, default); Assert.True(session.Context.IsAdministrator); Assert.Equal(21, session.Context.Permissions.Count);
            Assert.Contains(await sp.GetRequiredService<ILdapAuditRepository>().ListRecentAsync(100), a => a.EventType == "ldap.authentication.success" && a.Success);
            Assert.Contains(await sp.GetRequiredService<ILdapAuditRepository>().ListRecentAsync(100), a => a.EventType == "ldap.authentication.failed" && !a.Success);
            await service.TestAsync(actor, "test", default); Assert.Equal("successful", (await configs.GetAsync())!.Status);
            var changedEmail = "changed-" + email;
            await sp.GetRequiredService<ILdapUserWriter>().LinkAsync(new DirectoryUser(email, "uid=test", changedEmail, "Renamed Directory User", []), true, false, null, default);
            Assert.Equal(changedEmail, (await sp.GetRequiredService<IUserRepository>().GetByIdAsync(userId))!.Email);
        }
        finally
        {
            if (userId is not null) await sp.GetRequiredService<IUserRepository>().DeleteAsync(userId);
            await mappings.ReplaceAllAsync(originalMappings);
            if (originalConfig is not null) await configs.UpsertAsync(originalConfig); else { await using var c = await sp.GetRequiredService<IDbConnectionFactory>().OpenAsync(); await c.ExecuteAsync("DELETE FROM dbo.ldap_config WHERE id=1"); }
            await sp.GetRequiredService<ILabRepository>().DeleteAsync(lab.Id); await sp.GetRequiredService<ISectionRepository>().DeleteAsync(section.Id);
        }
    }
}
