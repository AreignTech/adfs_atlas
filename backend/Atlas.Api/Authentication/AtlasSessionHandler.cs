using System.Security.Claims;
using System.Text.Encodings.Web;
using Atlas.Application.Interfaces;
using Atlas.Application.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Atlas.Api.Authentication;

public sealed class AtlasSessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IAuthService auth, IOptions<AuthOptions> settings)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "AtlasSession";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var session = await auth.GetSessionAsync(Request.Cookies[settings.Value.CookieName], Context.RequestAborted);
        if (session is null) return AuthenticateResult.NoResult();
        Context.Items["AtlasSession"] = session;
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, session.Context.User.Id), new(ClaimTypes.Email, session.Context.User.Email) };
        claims.AddRange(session.Context.Permissions.Select(p => new Claim("permission", p)));
        claims.AddRange(session.Context.RoleNames.Select(r => new Claim(ClaimTypes.Role, r)));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }
}
