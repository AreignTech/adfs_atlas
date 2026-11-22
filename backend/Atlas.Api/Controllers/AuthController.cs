using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Options;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Atlas.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(IAuthService auth, IOptions<AuthOptions> options, IWebHostEnvironment environment) : ControllerBase
{
    private AuthOptions Settings => options.Value;
    private CookieOptions Cookie(DateTimeOffset? expires = null) => new() { HttpOnly = true, Secure = !environment.IsDevelopment() || Settings.RequireHttps, SameSite = SameSiteMode.Lax, Path = "/", IsEssential = true, Expires = expires };
    [HttpGet("csrf"), AllowAnonymous]
    public object Csrf(IAntiforgery antiforgery)
    {
        Response.Headers.CacheControl = "no-store";
        return new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken };
    }
    [HttpGet("session"), AllowAnonymous]
    public IActionResult Session()
    {
        Response.Headers.CacheControl = "no-store";
        return HttpContext.Items["AtlasSession"] is AuthSession session ? Ok(new { user = session.Context.User, session = new { expiresAt = session.ExpiresAt }, context = session.Context }) : Ok(null);
    }
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<object> Login(LoginRequest request, CancellationToken ct) => Issue(await auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent, ct));
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<object> Register(RegisterRequest request, CancellationToken ct) => Issue(await auth.RegisterAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent, ct));
    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(Request.Cookies[Settings.CookieName], ct);
        Response.Cookies.Delete(Settings.CookieName, Cookie());
        return NoContent();
    }
    [HttpPost("password"), Authorize]
    public async Task<IActionResult> Password(ChangePasswordRequest request, CancellationToken ct)
    {
        var session = (AuthSession)HttpContext.Items["AtlasSession"]!;
        await auth.ChangePasswordAsync(session.Context.User.Id, session.Token, request, ct);
        return NoContent();
    }
    private object Issue(AuthSession session)
    {
        Response.Cookies.Append(Settings.CookieName, session.Token, Cookie(session.ExpiresAt));
        Response.Headers.CacheControl = "no-store";
        return new { user = session.Context.User, session = new { expiresAt = session.ExpiresAt }, context = session.Context };
    }

    [HttpPost("delete-account"), Authorize, EnableRateLimiting("login")]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest input, [FromServices] IAccountRepository accounts, [FromServices] IMemberRepository members, [FromServices] IPasswordHasher hasher, [FromServices] IAdminWriter writer, CancellationToken ct)
    {
        var actor = ((AuthSession)HttpContext.Items["AtlasSession"]!).Context;
        var account = await accounts.GetCredentialAsync(actor.User.Id, ct);
        if (account?.Password is null || input.CurrentPassword.Length > 4000 || !hasher.Verify(account.Password, input.CurrentPassword)) throw new Atlas.Application.Common.ValidationException("Current password is incorrect. Directory-only accounts must contact an administrator.");
        var member = await members.GetByUserIdAsync(actor.User.Id, ct) ?? throw new Atlas.Application.Common.NotFoundException("Account membership was not found.");
        await writer.RemoveMemberAsync(member.Id, actor, ct, true);
        Response.Cookies.Delete(Settings.CookieName, Cookie());
        return NoContent();
    }
}
