namespace Atlas.Application.Options;

/// <summary>Configuration bound from the <c>Atlas</c> configuration section.</summary>
public sealed class AuthOptions
{
    public string CookieName { get; set; } = "atlas.session_token";
    public int SessionDays { get; set; } = 7;
    public bool AllowSelfRegistration { get; set; }
    public string FrontendOrigin { get; set; } = "http://localhost:3000";
    public bool RequireHttps { get; set; }
    public List<string> AllowedOrigins { get; set; } = [];
}
