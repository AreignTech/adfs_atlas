using Atlas.Application.Interfaces;
using Atlas.Application.Options;
using Atlas.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAtlasApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection("Atlas"));

        services.AddScoped<IRbacResolver, RbacResolver>();
        services.AddScoped<IOrgScopeResolver, OrgScopeResolver>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<Atlas.Application.Security.AccessGuard>();
        services.AddScoped<WorkspaceService>();
        services.AddScoped<TaskCommentService>();
        services.AddScoped<LdapService>();

        return services;
    }
}
