using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Services;
using Atlas.Domain.Entities;
using Atlas.Infrastructure.Security;
using Atlas.Infrastructure.Data.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Atlas.Tests;

public class AtlasFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connection = Environment.GetEnvironmentVariable("ATLAS_TEST_CONNECTION") ?? "Server=localhost\\SQL2022DEV;Database=ATLAS_MigrationVerification;Integrated Security=True;TrustServerCertificate=True;Encrypt=True";
        if (!new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog.Contains("Verification", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("All integration tests require a dedicated database whose name contains Verification.");
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Atlas"] = connection,
            ["Atlas:AllowSelfRegistration"] = "false"
            , ["Ldap:EncryptionKey"] = "compatibility-key"
        }));
    }
}

public sealed class AuthTests : IAsyncLifetime
{
    private readonly AtlasFactory _factory = new();
    private readonly string _userId = Guid.NewGuid().ToString();
    private string Email => $"auth-{_userId}@atlas.test";
    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync();
        var connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        await using (connection)
            if (!connection.Database.Contains("Verification")) throw new InvalidOperationException("Tests require an isolated Verification database.");
        await scope.ServiceProvider.GetRequiredService<IIdentityWriter>().CreateAsync(new User { Id = _userId, Name = "Auth Test", Email = Email }, new Member { UserId = _userId, Status = "active", Role = "Users" }, new Account { Id = Guid.NewGuid().ToString(), AccountId = _userId, UserId = _userId, ProviderId = "credential", Password = new BetterAuthPasswordHasher().Hash("Compatibility123!") }, "Users");
    }
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IUserRepository>().DeleteAsync(_userId);
        await _factory.DisposeAsync();
    }
    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-ATLAS-CSRF", token.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    [Fact]
    public void ExistingNodeScryptHashVerifies()
    {
        const string hash = "0123456789abcdef0123456789abcdef:7c54fbbf774013e2ff2c99128555760da219817e489905d56b68ed109814f52dbfd3b11416bac3bc5891f5e130824c465a89c1434233437caccde5102faf8327";
        var hasher = new BetterAuthPasswordHasher();
        Assert.True(hasher.Verify(hash, "Compatibility123!"));
        Assert.False(hasher.Verify(hash, "wrong"));
        Assert.False(hasher.Verify("bad:data", "wrong"));
    }
    [Fact]
    public async Task LoginRequiresCsrfAndCredentialsAndIssuesHttpOnlyCookie()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { Email, password = "Compatibility123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, "/api/auth/login", new { Email, password = "wrong" })).StatusCode);
        var response = await PostAsync(client, "/api/auth/login", new { Email, password = "Compatibility123!" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("atlas.session_token="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant());
        Assert.DoesNotContain("token", (await response.Content.ReadAsStringAsync()).ToLowerInvariant());
        Assert.Contains(Email, await client.GetStringAsync("/api/auth/session"));
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "/api/auth/logout", new { })).StatusCode);
        Assert.True(string.IsNullOrWhiteSpace(await client.GetStringAsync("/api/auth/session")));
    }
    [Fact]
    public async Task PasswordChangeRevokesOtherSessionsAndPreservesCurrentSession()
    {
        var client = _factory.CreateClient();
        var other = _factory.CreateClient();
        (await PostAsync(client, "/api/auth/login", new { Email, password = "Compatibility123!" })).EnsureSuccessStatusCode();
        (await PostAsync(other, "/api/auth/login", new { Email, password = "Compatibility123!" })).EnsureSuccessStatusCode();
        var result = await PostAsync(client, "/api/auth/password", new { currentPassword = "Compatibility123!", newPassword = "ChangedPassword456!" });
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        Assert.Contains(Email, await client.GetStringAsync("/api/auth/session"));
        Assert.True(string.IsNullOrWhiteSpace(await other.GetStringAsync("/api/auth/session")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(other, "/api/auth/login", new { Email, password = "Compatibility123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(other, "/api/auth/login", new { Email, password = "ChangedPassword456!" })).StatusCode);
    }
    [Fact]
    public async Task SelfDeletionRequiresPasswordAndRevokesTheSession()
    {
        var client = _factory.CreateClient(); (await PostAsync(client, "/api/auth/login", new { Email, password = "Compatibility123!" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, "/api/auth/delete-account", new { currentPassword = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(client, "/api/auth/delete-account", new { currentPassword = "Compatibility123!" })).StatusCode);
        Assert.True(string.IsNullOrWhiteSpace(await client.GetStringAsync("/api/auth/session")));
        using var scope = _factory.Services.CreateScope(); Assert.Null(await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetByIdAsync(_userId));
    }
    [Fact]
    public async Task ProfileAndNotificationSettingsPersistForTheAuthenticatedUser()
    {
        var client = _factory.CreateClient();
        (await PostAsync(client, "/api/auth/login", new { Email, password = "Compatibility123!" })).EnsureSuccessStatusCode();
        (await PostAsync(client, "/api/settings/notifications", new { matrix = new { taskAssigned = new { email = true, inApp = true, push = false } }, digestFrequency = "daily", quietHoursStart = "22:00", quietHoursEnd = "06:00", quietHoursEnabled = false, productNews = false })).EnsureSuccessStatusCode();
        var saved = await client.GetFromJsonAsync<JsonElement>("/api/settings/notifications"); Assert.Equal("daily", saved.GetProperty("value").GetProperty("digestFrequency").GetString());
        (await PostAsync(client, "/api/settings/profile", new { firstName = "Updated", lastName = "User", email = Email, profileImage = (string?)null })).EnsureSuccessStatusCode();
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/settings/profile"); Assert.Equal("Updated User", profile.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/settings/connections")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(client, "/api/settings/connections", new { github = true })).StatusCode);
        using var scope = _factory.Services.CreateScope(); Assert.NotNull(await scope.ServiceProvider.GetRequiredService<ISettingsRepository>().GetAsync(_userId, "notifications", default)); Assert.Null(await scope.ServiceProvider.GetRequiredService<ISettingsRepository>().GetAsync("other-user", "notifications", default));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/settings/profile")).StatusCode);
    }
    [Fact]
    public async Task ProfileImagesPersistAtTheDecodedSizeLimitAndCanBeRemoved()
    {
        var client = _factory.CreateClient();
        (await PostAsync(client, "/api/auth/login", new { Email, password = "Compatibility123!" })).EnsureSuccessStatusCode();
        var bytes = new byte[2 * 1024 * 1024];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        var image = "data:image/png;base64," + Convert.ToBase64String(bytes);
        (await PostAsync(client, "/api/settings/profile", new { firstName = "Avatar", lastName = "Test", email = Email, profileImage = image })).EnsureSuccessStatusCode();
        var saved = await client.GetFromJsonAsync<JsonElement>("/api/settings/profile");
        Assert.True(image == saved.GetProperty("user").GetProperty("image").GetString(), "The image must persist without truncation.");
        // Editing text without sending a photo must not silently erase the current photo.
        (await PostAsync(client, "/api/settings/profile", new { firstName = "Avatar", lastName = "Updated", email = Email })).EnsureSuccessStatusCode();
        using var scope = _factory.Services.CreateScope();
        Assert.True(image == (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetByIdAsync(_userId))!.Image);
        (await PostAsync(client, "/api/settings/profile", new { firstName = "Avatar", lastName = "Updated", email = Email, profileImage = (string?)null })).EnsureSuccessStatusCode();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetByIdAsync(_userId))!.Image);
    }

    [Fact]
    public async Task GifImagesAreSupportedAndMalformedOrOversizeImagesReturnValidationErrors()
    {
        var client = _factory.CreateClient();
        (await PostAsync(client, "/api/auth/login", new { Email, password = "Compatibility123!" })).EnsureSuccessStatusCode();
        const string gif = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";
        (await PostAsync(client, "/api/settings/profile", new { firstName = "GIF", lastName = "Test", email = Email, profileImage = gif })).EnsureSuccessStatusCode();
        foreach (var image in new[] { "data:image/svg+xml;base64,PHN2Zy8+", "data:image/png;base64,invalid!", "data:image/jpeg;base64,aGVsbG8=", "data:image/webp;base64,", "data:image/png;base64," + Convert.ToBase64String(new byte[2 * 1024 * 1024 + 1]) })
        {
            var response = await PostAsync(client, "/api/settings/profile", new { firstName = "GIF", lastName = "Test", email = Email, profileImage = image });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("2 MB", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, "/api/settings/profile", new { firstName = "GIF", lastName = "Test", email = Email, profileImage = true })).StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(gif, (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetByIdAsync(_userId))!.Image);
    }

    [Fact]
    public async Task OversizedRequestErrorsAreReportedAs413InsteadOf500()
    {
        var handler = new Atlas.Api.Middleware.ApiExceptionHandler(Microsoft.Extensions.Logging.Abstractions.NullLogger<Atlas.Api.Middleware.ApiExceptionHandler>.Instance);
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await handler.TryHandleAsync(context, new Microsoft.AspNetCore.Http.BadHttpRequestException("Request body too large", 413), default);
        Assert.Equal(413, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var problem = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Contains("too large", problem.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task RegistrationIsDisabledAndSessionsAreStoredAsHashes()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(client, "/api/auth/register", new { name = "New User", email = "register@atlas.test", password = "Password123!" })).StatusCode);
        var login = await PostAsync(client, "/api/auth/login", new { Email, password = "Compatibility123!" });
        var raw = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("atlas.session_token=")).Split(';')[0].Split('=')[1];
        using var scope = _factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        Assert.Null(await sessions.GetByTokenAsync(raw));
        Assert.NotNull(await sessions.GetByTokenAsync(AuthService.HashToken(raw)));
    }
    [Fact]
    public async Task RegistrationValidatesCreatesHashedCredentialsAndAllowsLogin()
    {
        await using var factory = new AtlasFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Atlas:AllowSelfRegistration"] = "true" })));
        using var client = factory.CreateClient();
        var email = $"registration-{Guid.NewGuid():N}@atlas.test";
        const string password = "Registration123!";
        string? createdId = null;
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        try
        {
            foreach (var invalid in new[] {
                new { name = "", email, password },
                new { name = "New User", email = "invalid@", password },
                new { name = "New User", email, password = "short" },
            })
                Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, "/api/auth/register", invalid)).StatusCode);

            var response = await PostAsync(client, "/api/auth/register", new { name = "New User", email, password, role = "Administrators" });
            response.EnsureSuccessStatusCode();
            var user = await users.GetByEmailAsync(email);
            Assert.NotNull(user);
            createdId = user.Id;
            var account = await scope.ServiceProvider.GetRequiredService<IAccountRepository>().GetCredentialAsync(user.Id);
            Assert.NotNull(account?.Password);
            Assert.NotEqual(password, account.Password);
            Assert.True(new BetterAuthPasswordHasher().Verify(account.Password, password));
            var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
            Assert.False(session.GetProperty("context").GetProperty("isAdministrator").GetBoolean());
            Assert.Contains("Users", session.GetProperty("context").GetProperty("roleNames").EnumerateArray().Select(role => role.GetString()));
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(client, "/api/auth/register", new { name = "New User", email, password })).StatusCode);
            (await PostAsync(client, "/api/auth/logout", new { })).EnsureSuccessStatusCode();
            (await PostAsync(client, "/api/auth/login", new { email, password })).EnsureSuccessStatusCode();
            Assert.Contains(email, await client.GetStringAsync("/api/auth/session"));
            var member = await scope.ServiceProvider.GetRequiredService<IMemberRepository>().GetByUserIdAsync(createdId!);
            Assert.NotNull(member);
            Assert.Equal("active", member.Status);
            Assert.NotNull(member.LastActiveAt);
        }
        finally
        {
            var user = createdId is null ? await users.GetByEmailAsync(email) : null;
            if ((createdId ?? user?.Id) is string id) await users.DeleteAsync(id);
        }
    }
    [Fact]
    public async Task IdentityCreationRollsBackIfCredentialInsertFails()
    {
        using var scope = _factory.Services.CreateScope();
        var id = Guid.NewGuid().ToString();
        await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<IIdentityWriter>().CreateAsync(new User { Id = id, Name = "Rollback", Email = $"{id}@atlas.test" }, new Member { UserId = id, Role = "Users" }, new Account { Id = Guid.NewGuid().ToString(), UserId = "missing-user", AccountId = id, ProviderId = "credential" }));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetByIdAsync(id));
    }
    [Fact]
    public async Task ExpiredAndInvalidSessionsAreRejected()
    {
        using var scope = _factory.Services.CreateScope();
        const string token = "expired-test-session";
        await scope.ServiceProvider.GetRequiredService<ISessionRepository>().CreateAsync(new Session { Id = Guid.NewGuid().ToString(), Token = AuthService.HashToken(token), UserId = _userId, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"atlas.session_token={token}");
        Assert.True(string.IsNullOrWhiteSpace(await client.GetStringAsync("/api/auth/session")));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ISessionRepository>().GetByTokenAsync(AuthService.HashToken(token)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/password", new { })).StatusCode);
    }
}
