using Atlas.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace Atlas.Api.Observability;

public sealed class MonitoringRequirement : IAuthorizationRequirement;
public sealed class MonitoringAuthorization : AuthorizationHandler<MonitoringRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MonitoringRequirement requirement)
    {
        if (context.Resource is HttpContext http && http.Items["AtlasSession"] is AuthSession session &&
            session.Context.IsAdministrator && session.Context.Permissions.Contains("admin.access")) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
