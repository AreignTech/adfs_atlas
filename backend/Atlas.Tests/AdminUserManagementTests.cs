using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Atlas.Tests;

public sealed class AdminUserManagementTests : IAsyncLifetime
{
    private readonly AtlasFactory _factory = new();
    private readonly List<string> _users = [];
    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Atlas.Infrastructure.Data.Migrations.DatabaseMigrator>().MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        foreach (var id in _users) await sp.GetRequiredService<IUserRepository>().DeleteAsync(id);
        await _factory.DisposeAsync();
    }
    private async Task<(User User, Member Member, HttpClient Client)> UserAsync(string role, bool withCredential = true)
    {
        using var scope = _factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        var id = Guid.NewGuid().ToString(); var user = new User { Id = id, Email = $"{id}@admin-mgmt.test", Name = "Mgmt Target" };
        var member = new Member { UserId = id, Status = withCredential ? "active" : "invited", Role = role };
        await sp.GetRequiredService<IIdentityWriter>().CreateAsync(user, member, withCredential ? new Account { Id = Guid.NewGuid().ToString(), AccountId = id, UserId = id, ProviderId = "credential", Password = sp.GetRequiredService<IPasswordHasher>().Hash("SecurityTest123!") } : null, role);
        _users.Add(id);
        if (!withCredential) return (user, member, null!);
        var client = _factory.CreateClient(); (await AuthTests.PostAsync(client, "/api/auth/login", new { user.Email, password = "SecurityTest123!" })).EnsureSuccessStatusCode(); return (user, member, client);
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-ATLAS-CSRF", token.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    private async Task<bool> AuditExistsAsync(string action, string affectedUserId)
    {
        using var scope = _factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IRbacAuditRepository>().ListRecentAsync(300)).Any(a => a.Action == action && a.AffectedUserId == affectedUserId);
    }

    [Fact]
    public async Task AdministratorEditsMemberProfileAndItPersistsAndIsAudited()
    {
        var admin = await UserAsync("Administrators"); var target = await UserAsync("Users");
        var newEmail = $"edited-{Guid.NewGuid():N}@admin-mgmt.test";
        var response = await SendAsync(admin.Client, HttpMethod.Put, $"/api/admin/members/{target.Member.Id}", new { name = "Renamed Person", email = newEmail });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Renamed Person", body); Assert.Contains(newEmail, body);
        Assert.Contains(newEmail, await admin.Client.GetStringAsync("/api/workspace"));
        Assert.True(await AuditExistsAsync("user.updated", target.User.Id));
    }
    [Fact]
    public async Task EditRejectsDuplicateEmailWithConflict()
    {
        var admin = await UserAsync("Administrators"); var target = await UserAsync("Users"); var other = await UserAsync("Users");
        var response = await SendAsync(admin.Client, HttpMethod.Put, $"/api/admin/members/{target.Member.Id}", new { name = "Renamed Person", email = other.User.Email });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
    [Fact]
    public async Task EditRejectsInvalidEmailAndUnknownUser()
    {
        var admin = await UserAsync("Administrators"); var target = await UserAsync("Users");
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin.Client, HttpMethod.Put, $"/api/admin/members/{target.Member.Id}", new { name = "Renamed Person", email = "not an email" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin.Client, HttpMethod.Put, $"/api/admin/members/{target.Member.Id}", new { name = "x", email = $"ok-{Guid.NewGuid():N}@admin-mgmt.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(admin.Client, HttpMethod.Put, $"/api/admin/members/{Guid.NewGuid()}", new { name = "Renamed Person", email = $"ok-{Guid.NewGuid():N}@admin-mgmt.test" })).StatusCode);
    }
    [Fact]
    public async Task AdminPasswordResetReplacesCredentialAndRevokesEverySession()
    {
        var admin = await UserAsync("Administrators"); var victim = await UserAsync("Users");
        var otherDevice = _factory.CreateClient();
        (await AuthTests.PostAsync(otherDevice, "/api/auth/login", new { victim.User.Email, password = "SecurityTest123!" })).EnsureSuccessStatusCode();
        var response = await SendAsync(admin.Client, HttpMethod.Post, $"/api/admin/members/{victim.Member.Id}/password", new { newPassword = "ResetPassword789!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ResetPassword789!", body);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthTests.PostAsync(_factory.CreateClient(), "/api/auth/login", new { victim.User.Email, password = "SecurityTest123!" })).StatusCode);
        Assert.True(string.IsNullOrWhiteSpace(await victim.Client.GetStringAsync("/api/auth/session")));
        Assert.True(string.IsNullOrWhiteSpace(await otherDevice.GetStringAsync("/api/auth/session")));
        Assert.Equal(HttpStatusCode.OK, (await AuthTests.PostAsync(_factory.CreateClient(), "/api/auth/login", new { victim.User.Email, password = "ResetPassword789!" })).StatusCode);
        Assert.True(await AuditExistsAsync("user.password_reset", victim.User.Id));
    }
    [Fact]
    public async Task PasswordResetRejectsPolicyViolationsAndUnacceptedInvitations()
    {
        var admin = await UserAsync("Administrators"); var target = await UserAsync("Users"); var invited = await UserAsync("Users", withCredential: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin.Client, HttpMethod.Post, $"/api/admin/members/{target.Member.Id}/password", new { newPassword = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin.Client, HttpMethod.Post, $"/api/admin/members/{target.Member.Id}/password", new { newPassword = "alllowercase1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin.Client, HttpMethod.Post, $"/api/admin/members/{target.Member.Id}/password", new { newPassword = "NoDigitsHere" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin.Client, HttpMethod.Post, $"/api/admin/members/{invited.Member.Id}/password", new { newPassword = "ResetPassword789!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(admin.Client, HttpMethod.Post, $"/api/admin/members/{Guid.NewGuid()}/password", new { newPassword = "ResetPassword789!" })).StatusCode);
    }
    [Fact]
    public async Task DeactivationRevokesSessionsAndBlocksSignInUntilReactivated()
    {
        var admin = await UserAsync("Administrators"); var victim = await UserAsync("Users");
        Assert.Equal(HttpStatusCode.OK, (await AuthTests.PostAsync(admin.Client, $"/api/admin/members/{victim.Member.Id}/status", new { status = "Inactive" })).StatusCode);
        Assert.True(string.IsNullOrWhiteSpace(await victim.Client.GetStringAsync("/api/auth/session")));
        Assert.Equal(HttpStatusCode.Forbidden, (await AuthTests.PostAsync(_factory.CreateClient(), "/api/auth/login", new { victim.User.Email, password = "SecurityTest123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuthTests.PostAsync(admin.Client, $"/api/admin/members/{victim.Member.Id}/status", new { status = "Active" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AuthTests.PostAsync(_factory.CreateClient(), "/api/auth/login", new { victim.User.Email, password = "SecurityTest123!" })).StatusCode);
        Assert.True(await AuditExistsAsync("user.status_changed", victim.User.Id));
    }
    [Fact]
    public async Task NonAdministratorsAndAnonymousCannotEditResetOrDeactivate()
    {
        var caller = await UserAsync("Users"); var admin = await UserAsync("Administrators");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(caller.Client, HttpMethod.Put, $"/api/admin/members/{admin.Member.Id}", new { name = "Evil Rename", email = $"evil-{Guid.NewGuid():N}@admin-mgmt.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AuthTests.PostAsync(caller.Client, $"/api/admin/members/{admin.Member.Id}/password", new { newPassword = "Escalation123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AuthTests.PostAsync(caller.Client, $"/api/admin/members/{admin.Member.Id}/status", new { status = "Inactive" })).StatusCode);
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/admin/members/{Guid.NewGuid()}", new { name = "x", email = "x@y.z" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/admin/members/{Guid.NewGuid()}/password", new { newPassword = "whatever1" })).StatusCode);
    }
}
