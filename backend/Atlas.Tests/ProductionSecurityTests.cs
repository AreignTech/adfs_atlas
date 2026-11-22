using System.Net;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Atlas.Tests;

public sealed class ProductionSecurityTests
{
    private sealed class ProductionFactory : AtlasFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) { base.ConfigureWebHost(builder); builder.UseEnvironment("Production"); }
    }
    [Fact]
    public async Task ProductionCookiesAreSecureAndDiagnosticsAreNotExposed()
    {
        await using var factory = new ProductionFactory(); using var scope = factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        var id = Guid.NewGuid().ToString(); var email = $"{id}@production-security.test";
        await sp.GetRequiredService<IIdentityWriter>().CreateAsync(new User { Id = id, Name = "Security", Email = email }, new Member { UserId = id, Status = "active", Role = "Users" }, new Account { Id = Guid.NewGuid().ToString(), AccountId = id, UserId = id, ProviderId = "credential", Password = sp.GetRequiredService<IPasswordHasher>().Hash("ProductionTest123!") }, "Users");
        try
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var response = await AuthTests.PostAsync(client, "/api/auth/login", new { email, password = "ProductionTest123!" }); response.EnsureSuccessStatusCode();
            var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("atlas.session_token="));
            Assert.Contains("secure", cookie.ToLowerInvariant()); Assert.Contains("httponly", cookie.ToLowerInvariant());
            Assert.True((await client.GetAsync("/api/workspace")).Headers.CacheControl?.NoStore);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/health/migrations")).StatusCode);
        }
        finally { await sp.GetRequiredService<IUserRepository>().DeleteAsync(id); }
    }
}
