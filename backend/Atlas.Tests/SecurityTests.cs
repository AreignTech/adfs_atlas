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
    [Theory, InlineData(false, false), InlineData(true, false), InlineData(false, true)]
    public async Task NonMembersAreDeniedEvenWhenTheirOrganizationMatches(bool differentLab, bool sameOrganization)
    {
        using var scope = _factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        var lab = new LabCode { Code = Guid.NewGuid().ToString("N"), Name = "Test lab" }; await sp.GetRequiredService<ILabRepository>().CreateAsync(lab); _labs.Add(lab.Id);
        var otherLab = new LabCode { Code = Guid.NewGuid().ToString("N"), Name = "Other lab" }; await sp.GetRequiredService<ILabRepository>().CreateAsync(otherLab); _labs.Add(otherLab.Id);
        var section = new SectionCode { Code = Guid.NewGuid().ToString("N"), Name = "Test section" }; await sp.GetRequiredService<ISectionRepository>().CreateAsync(section); _sections.Add(section.Id);
        var otherSection = new SectionCode { Code = Guid.NewGuid().ToString("N"), Name = "Other section" }; await sp.GetRequiredService<ISectionRepository>().CreateAsync(otherSection); _sections.Add(otherSection.Id);
        var caller = await UserAsync("Users", lab.Id, section.Id); var owner = await UserAsync("Users", differentLab ? otherLab.Id : lab.Id, sameOrganization ? section.Id : otherSection.Id); var admin = await UserAsync("Administrators");
        var project = new Project { Name = "Protected project", Key = Guid.NewGuid().ToString("N")[..8], OwnerId = owner.Member.Id, LabId = differentLab ? otherLab.Id : lab.Id, SectionId = sameOrganization ? section.Id : otherSection.Id, StartDate = DateTime.Today, DueDate = DateTime.Today.AddDays(1) };
        await sp.GetRequiredService<IProjectRepository>().CreateAsync(project); _projects.Add(project.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.Client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AuthTests.PostAsync(caller.Client, "/api/tasks", new { projectId = project.Id, title = "Denied", dueDate = "2026-12-01" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
        var workspace = await caller.Client.GetStringAsync("/api/workspace"); Assert.DoesNotContain(project.Id.ToString(), workspace); Assert.DoesNotContain(owner.User.Email, workspace);
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

    [Theory]
    [InlineData("lab")]
    [InlineData("section")]
    [InlineData("unassigned")]
    public async Task ProjectTeamMembershipGrantsProjectAndAllTaskAccessAndRemovalRevokesIt(string difference)
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var lab = new LabCode { Code = Guid.NewGuid().ToString("N"), Name = "Shared project lab" };
        var otherLab = new LabCode { Code = Guid.NewGuid().ToString("N"), Name = "Member lab" };
        var section = new SectionCode { Code = Guid.NewGuid().ToString("N"), Name = "Shared section" };
        var otherSection = new SectionCode { Code = Guid.NewGuid().ToString("N"), Name = "Member section" };
        await sp.GetRequiredService<ILabRepository>().CreateAsync(lab); _labs.Add(lab.Id);
        await sp.GetRequiredService<ILabRepository>().CreateAsync(otherLab); _labs.Add(otherLab.Id);
        await sp.GetRequiredService<ISectionRepository>().CreateAsync(section); _sections.Add(section.Id);
        await sp.GetRequiredService<ISectionRepository>().CreateAsync(otherSection); _sections.Add(otherSection.Id);
        var owner = await UserAsync("Users", lab.Id, section.Id);
        var caller = await UserAsync("Users", difference == "unassigned" ? null : difference == "lab" ? otherLab.Id : lab.Id,
            difference == "unassigned" ? null : difference == "section" ? otherSection.Id : section.Id);
        var admin = await UserAsync("Administrators");
        var key = string.Concat(Guid.NewGuid().ToByteArray().Take(4).Select(b => (char)('A' + b % 26)));
        var input = new ProjectInput { Name = "Shared membership project", Key = key, Status = "active", DueDate = "2027-12-01", LabId = lab.Id, SectionId = section.Id, MemberIds = [owner.Member.Id.ToString()] };
        var created = await AuthTests.PostAsync(admin.Client, "/api/projects", input);
        created.EnsureSuccessStatusCode();
        var project = (await created.Content.ReadFromJsonAsync<ProjectDto>())!;
        var projectId = Guid.Parse(project.Id); _projects.Add(projectId);
        var unrelated = new Project { Name = "Unrelated private project", Key = Guid.NewGuid().ToString("N")[..8], OwnerId = owner.Member.Id, LabId = lab.Id, SectionId = section.Id, StartDate = DateTime.Today, DueDate = DateTime.Today.AddDays(1) };
        await sp.GetRequiredService<IProjectRepository>().CreateAsync(unrelated); _projects.Add(unrelated.Id);
        var taskResponse = await AuthTests.PostAsync(admin.Client, "/api/tasks", new { projectId, title = "Shared task", dueDate = "2027-12-01", assigneeId = owner.Member.Id });
        taskResponse.EnsureSuccessStatusCode();
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskDto>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.Client.GetAsync($"/api/projects/{project.Id}")).StatusCode);

        async Task SetTeamAsync(bool includeCaller)
        {
            input.MemberIds = includeCaller ? [owner.Member.Id.ToString(), caller.Member.Id.ToString()] : [owner.Member.Id.ToString()];
            var csrf = await admin.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/csrf");
            using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/{project.Id}") { Content = JsonContent.Create(input) };
            request.Headers.Add("X-ATLAS-CSRF", csrf.GetProperty("token").GetString());
            (await admin.Client.SendAsync(request)).EnsureSuccessStatusCode();
        }

        await SetTeamAsync(true);
        var workspace = (await caller.Client.GetFromJsonAsync<WorkspaceDto>("/api/workspace"))!;
        Assert.Contains(workspace.Projects, p => p.Id == project.Id);
        Assert.Contains(workspace.Tasks, t => t.Id == task.Id);
        Assert.Contains(workspace.Members, m => m.Id == owner.Member.Id.ToString());
        Assert.DoesNotContain(workspace.Projects, p => p.Id == unrelated.Id.ToString());
        Assert.Equal(HttpStatusCode.OK, (await caller.Client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.Client.GetAsync($"/api/projects/{unrelated.Id}")).StatusCode);
        var results = (await caller.Client.GetFromJsonAsync<PageResult<Project>>("/api/projects?search=Shared"))!;
        Assert.Contains(results.Items, p => p.Id == projectId);
        var filter = new OrgScopeFilter(false, difference == "unassigned" ? null : difference == "lab" ? otherLab.Id : lab.Id,
            difference == "unassigned" ? null : difference == "section" ? otherSection.Id : section.Id, caller.User.Id);
        var tasks = await sp.GetRequiredService<ISearchRepository>().TasksAsync(filter, new());
        Assert.Contains(tasks.Items, t => t.Id.ToString() == task.Id);
        var added = await AuthTests.PostAsync(caller.Client, $"/api/tasks/{task.Id}/comments", new { content = "Team member can access task details" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await caller.Client.GetAsync($"/api/tasks/{task.Id}/comments")).StatusCode);
        var noView = new SessionContextDto(new(caller.User.Id, caller.User.Name, caller.User.Email, false, null), ["Custom"], [], false,
            new(filter.LabId, filter.SectionId, difference != "unassigned", false));
        Assert.Empty((await sp.GetRequiredService<Atlas.Application.Services.WorkspaceService>().GetAsync(noView, default)).Projects);
        var callerCsrf = await caller.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/csrf");
        using var deniedDelete = new HttpRequestMessage(HttpMethod.Delete, $"/api/projects/{project.Id}");
        deniedDelete.Headers.Add("X-ATLAS-CSRF", callerCsrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.Client.SendAsync(deniedDelete)).StatusCode);

        await SetTeamAsync(false);
        var revoked = (await caller.Client.GetFromJsonAsync<WorkspaceDto>("/api/workspace"))!;
        Assert.DoesNotContain(revoked.Projects, p => p.Id == project.Id);
        Assert.DoesNotContain(revoked.Tasks, t => t.Id == task.Id);
        Assert.DoesNotContain(revoked.Members, m => m.Id == owner.Member.Id.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.Client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.Client.GetAsync($"/api/tasks/{task.Id}/comments")).StatusCode);
        Assert.DoesNotContain((await sp.GetRequiredService<ISearchRepository>().TasksAsync(filter, new())).Items, t => t.Id.ToString() == task.Id);
    }

    [Fact]
    public async Task CodesOnlyClassifyDirectoryMembersAndDoNotRestrictProjectCreationOrAssignment()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var lab = new LabCode { Code = Guid.NewGuid().ToString("N"), Name = "Directory lab" };
        var section = new SectionCode { Code = Guid.NewGuid().ToString("N"), Name = "Directory section" };
        await sp.GetRequiredService<ILabRepository>().CreateAsync(lab); _labs.Add(lab.Id);
        await sp.GetRequiredService<ISectionRepository>().CreateAsync(section); _sections.Add(section.Id);
        var creator = await UserAsync("Users");
        var teammate = await UserAsync("Users", lab.Id, section.Id);
        var directory = (await creator.Client.GetFromJsonAsync<List<MemberDto>>("/api/members/team-options"))!;
        var candidate = Assert.Single(directory, m => m.Id == teammate.Member.Id.ToString());
        Assert.Equal(lab.Id, candidate.LabId); Assert.Equal(section.Id, candidate.SectionId);
        Assert.Equal(lab.Name, candidate.LabName); Assert.Equal(section.Name, candidate.SectionName);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/members/team-options")).StatusCode);
        var noPermissions = new SessionContextDto(new(creator.User.Id, creator.User.Name, creator.User.Email, false, null), [], [], false, new(null, null, false, false));
        await Assert.ThrowsAsync<ForbiddenException>(() => sp.GetRequiredService<Atlas.Application.Services.WorkspaceService>().TeamOptionsAsync(noPermissions, default));

        var input = new ProjectInput { Name = "No organization required", Key = string.Concat(Guid.NewGuid().ToByteArray().Take(4).Select(b => (char)('A' + b % 26))), DueDate = "2027-12-01", MemberIds = [creator.Member.Id.ToString(), teammate.Member.Id.ToString()] };
        var created = await AuthTests.PostAsync(creator.Client, "/api/projects", input);
        created.EnsureSuccessStatusCode();
        var project = (await created.Content.ReadFromJsonAsync<ProjectDto>())!;
        _projects.Add(Guid.Parse(project.Id));
        Assert.Null(project.LabId); Assert.Null(project.SectionId);
        var assigned = await AuthTests.PostAsync(creator.Client, "/api/tasks", new { projectId = project.Id, title = "Cross-section teammate assignment", dueDate = "2027-12-01", assigneeId = teammate.Member.Id });
        assigned.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await teammate.Client.GetAsync($"/api/projects/{project.Id}")).StatusCode);

        // Removing the owner from the explicit team does not transfer ownership.
        input.MemberIds = [teammate.Member.Id.ToString()];
        var csrf = await creator.Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/csrf");
        using var update = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/{project.Id}") { Content = JsonContent.Create(input) };
        update.Headers.Add("X-ATLAS-CSRF", csrf.GetProperty("token").GetString());
        (await creator.Client.SendAsync(update)).EnsureSuccessStatusCode();
        Assert.Equal(creator.Member.Id, (await sp.GetRequiredService<IProjectRepository>().GetByIdAsync(Guid.Parse(project.Id)))!.OwnerId);
        Assert.Equal(HttpStatusCode.OK, (await creator.Client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
    }
}
