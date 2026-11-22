using System.Net;
using System.Net.Http.Json;
using Atlas.Application.Common;
using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Security;
using Atlas.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Atlas.Tests;

public sealed class SecurityTests : IAsyncLifetime
{
    private readonly AtlasFactory _factory = new();
    private readonly List<string> _users = [];
    private readonly List<Guid> _projects = [];
    private readonly List<Guid> _labs = [];
    private readonly List<Guid> _sections = [];
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        foreach (var id in _projects) await sp.GetRequiredService<IProjectRepository>().DeleteAsync(id);
        foreach (var id in _users) await sp.GetRequiredService<IUserRepository>().DeleteAsync(id);
        foreach (var id in _labs) await sp.GetRequiredService<ILabRepository>().DeleteAsync(id);
        foreach (var id in _sections) await sp.GetRequiredService<ISectionRepository>().DeleteAsync(id);
        await _factory.DisposeAsync();
    }
    private async Task<(User User, Member Member, HttpClient Client)> UserAsync(string role, Guid? lab = null, Guid? section = null)
    {
        using var scope = _factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        var id = Guid.NewGuid().ToString(); var user = new User { Id = id, Email = $"{id}@security.test", Name = "Security Test" }; var member = new Member { UserId = id, Status = "active", Role = role };
        await sp.GetRequiredService<IIdentityWriter>().CreateAsync(user, member, new Account { Id = Guid.NewGuid().ToString(), AccountId = id, UserId = id, ProviderId = "credential", Password = sp.GetRequiredService<IPasswordHasher>().Hash("SecurityTest123!") }, role);
        _users.Add(id);
        if (lab.HasValue) await sp.GetRequiredService<IOrgAssignmentRepository>().UpsertAsync(new UserOrgAssignment { UserId = id, LabId = lab, SectionId = section });
        var client = _factory.CreateClient(); (await AuthTests.PostAsync(client, "/api/auth/login", new { user.Email, password = "SecurityTest123!" })).EnsureSuccessStatusCode(); return (user, member, client);
    }
    [Theory, InlineData(false), InlineData(true)]
    public async Task CrossSectionAndCrossLabAccessIsDeniedAndAudited(bool differentLab)
    {
        using var scope = _factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        var lab = new LabCode { Code = Guid.NewGuid().ToString("N"), Name = "Test lab" }; await sp.GetRequiredService<ILabRepository>().CreateAsync(lab); _labs.Add(lab.Id);
        var otherLab = new LabCode { Code = Guid.NewGuid().ToString("N"), Name = "Other lab" }; await sp.GetRequiredService<ILabRepository>().CreateAsync(otherLab); _labs.Add(otherLab.Id);
        var section = new SectionCode { Code = Guid.NewGuid().ToString("N"), Name = "Test section" }; await sp.GetRequiredService<ISectionRepository>().CreateAsync(section); _sections.Add(section.Id);
        var otherSection = new SectionCode { Code = Guid.NewGuid().ToString("N"), Name = "Other section" }; await sp.GetRequiredService<ISectionRepository>().CreateAsync(otherSection); _sections.Add(otherSection.Id);
        var caller = await UserAsync("Users", lab.Id, section.Id); var owner = await UserAsync("Users", differentLab ? otherLab.Id : lab.Id, otherSection.Id); var admin = await UserAsync("Administrators");
        var project = new Project { Name = "Protected project", Key = Guid.NewGuid().ToString("N")[..8], OwnerId = owner.Member.Id, LabId = differentLab ? otherLab.Id : lab.Id, SectionId = otherSection.Id, StartDate = DateTime.Today, DueDate = DateTime.Today.AddDays(1) };
        await sp.GetRequiredService<IProjectRepository>().CreateAsync(project); _projects.Add(project.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.Client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AuthTests.PostAsync(caller.Client, "/api/tasks", new { projectId = project.Id, title = "Denied", dueDate = "2026-12-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
        var workspace = await caller.Client.GetStringAsync("/api/workspace"); Assert.DoesNotContain(project.Id.ToString(), workspace); Assert.DoesNotContain(owner.User.Email, workspace);
        Assert.Contains(await sp.GetRequiredService<IOrgAuditRepository>().ListRecentAsync(100), a => a.ActorEmail == caller.User.Email && a.Action == "org.access.denied" && !a.Success);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.Client.GetAsync("/api/admin/roles")).StatusCode);
        Assert.Contains(await sp.GetRequiredService<IRbacAuditRepository>().ListRecentAsync(100), a => a.ActorUserId == caller.User.Id && a.Action == "authorization.denied");
    }
    [Fact]
    public async Task All21PermissionKeysAreEnforcedAndLegacyFallbackMatches()
    {
        using var scope = _factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        var actor = await UserAsync("Users"); var guard = sp.GetRequiredService<AccessGuard>();
        var empty = new SessionContextDto(new(actor.User.Id, actor.User.Name, actor.User.Email, false, null), ["Custom"], [], false, new(null, null, false, false));
        var keys = await sp.GetRequiredService<IPermissionRepository>().ListKeysAsync(); Assert.Equal(21, keys.Count); Assert.Equal(keys.Order(), AtlasPermissions.All.Order());
        foreach (var key in keys)
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => guard.PermissionAsync(empty, key));
            await guard.PermissionAsync(empty with { Permissions = [key] }, key);
        }
        await sp.GetRequiredService<IUserRoleRepository>().DeleteForUserAsync(actor.User.Id);
        foreach (var (role, count) in new[] { ("Users", 4), ("Supervisor", 7), ("Legacy Administrator", 21) })
        {
            await sp.GetRequiredService<IMemberRepository>().UpdateRoleAsync(actor.Member.Id, role);
            var resolved = await sp.GetRequiredService<IRbacResolver>().ResolveAsync(actor.User); Assert.Equal(count, resolved.Permissions.Count); Assert.Equal(count == 21, resolved.IsAdministrator);
        }
        await Assert.ThrowsAsync<ForbiddenException>(() => guard.ScopeAsync(empty, null, null));
    }
    [Fact]
    public async Task ProtectedRolesAndSelfRoleChangesAreRejected()
    {
        var admin = await UserAsync("Administrators"); using var scope = _factory.Services.CreateScope(); var role = await scope.ServiceProvider.GetRequiredService<IRoleRepository>().GetByNameAsync("Administrators");
        Assert.Equal(HttpStatusCode.Forbidden, (await AuthTests.PostAsync(admin.Client, $"/api/admin/members/{admin.Member.Id}/role", new { roleId = role!.Id })).StatusCode);
        var token = await admin.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/roles/{role!.Id}"); request.Headers.Add("X-ATLAS-CSRF", token.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Client.SendAsync(request)).StatusCode);
    }
}
