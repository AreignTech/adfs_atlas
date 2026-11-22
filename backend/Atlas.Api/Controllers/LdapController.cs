using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Options;
using Atlas.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Atlas.Api.Controllers;

[Authorize, ApiController, Route("api/ldap")]
public sealed class LdapController(LdapService service, IOptions<AuthOptions> settings, IWebHostEnvironment environment) : ControllerBase
{
    private SessionContextDto Actor => ((AuthSession)HttpContext.Items["AtlasSession"]!).Context;
    [HttpGet("config")] public Task<LdapAdminConfigDto> Config(CancellationToken ct) => service.ConfigAsync(Actor, ct);
    [HttpPost("config")] public async Task<object> Save(LdapConfigInput input, CancellationToken ct) { await service.SaveAsync(Actor, input, ct); return new { success = true }; }
    [HttpPost("test")] public Task<object> Test(LdapTestRequest input, CancellationToken ct) => service.TestAsync(Actor, input.Username, ct);
    [HttpGet("mappings")] public Task<object> Mappings(CancellationToken ct) => service.MappingsAsync(Actor, ct);
    [HttpPost("mappings")] public async Task<object> SaveMappings(List<LdapMappingInput> input, CancellationToken ct) { await service.SaveMappingsAsync(Actor, input, ct); return new { success = true }; }
    [HttpGet("audit")] public Task<object> Audit(CancellationToken ct) => service.AuditsAsync(Actor, ct);
    [HttpGet("/api/auth/ldap/status"), AllowAnonymous] public async Task<object> Status(CancellationToken ct) => new { enabled = await service.EnabledAsync(ct) };
    [HttpPost("/api/auth/ldap"), AllowAnonymous, EnableRateLimiting("ldap-login")]
    public async Task<object> Login(LdapLoginRequest input, CancellationToken ct)
    {
        var session = await service.LoginAsync(input, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent, ct);
        Response.Cookies.Append(settings.Value.CookieName, session.Token, new CookieOptions { HttpOnly = true, Secure = !environment.IsDevelopment() || settings.Value.RequireHttps, SameSite = SameSiteMode.Lax, Path = "/", Expires = session.ExpiresAt });
        Response.Headers.CacheControl = "no-store"; return new { success = true, redirectTo = "/dashboard" };
    }
}
