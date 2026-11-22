using Atlas.Application.Interfaces;
using Atlas.Infrastructure;
using Atlas.Infrastructure.Data.Migrations;
using Dapper;
using Atlas.Application;
using Atlas.Api.Authentication;
using Atlas.Api.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole();

builder.Services.AddAtlasInfrastructure(builder.Configuration);
builder.Services.AddAtlasApplication(builder.Configuration);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddAuthentication(AtlasSessionHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, AtlasSessionHandler>(AtlasSessionHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-ATLAS-CSRF";
    options.Cookie.Name = "atlas.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("ldap-login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
});
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { title = "Invalid CSRF token.", status = 400 });
            return;
        }
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "atlas-api" }));

app.MapGet("/health/db", async (IDbConnectionFactory factory, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    try
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        var value = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT 1", cancellationToken: cancellationToken));
        return value == 1
            ? Results.Ok(new { status = "ok" })
            : Results.Problem(title: "Database health check failed.", statusCode: 503);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Database health check failed");
        return Results.Problem(
            title: "Database health check failed.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapGet("/health/migrations", async (DatabaseMigrator migrator, CancellationToken cancellationToken) =>
    {
        var applied = await migrator.GetAppliedScriptsAsync(cancellationToken);
        return Results.Ok(new { applied });
    });
    app.MapOpenApi();
}
app.MapControllers();

app.Run();

public partial class Program;
