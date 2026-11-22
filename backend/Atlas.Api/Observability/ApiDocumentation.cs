using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace Atlas.Api.Observability;

public static class ApiDocumentation
{
    public static void Configure(Microsoft.AspNetCore.OpenApi.OpenApiOptions options)
    {
        options.AddSchemaTransformer((schema,context,ct) => {
            var required=context.JsonTypeInfo.Type.Name switch {
                "LoginRequest" => new[] {"email","password"}, "RegisterRequest" => new[] {"name","email","password"}, _ => Array.Empty<string>() };
            if(required.Length>0) { schema.Required ??=new HashSet<string>(); foreach(var field in required) schema.Required.Add(field); }
            return Task.CompletedTask;
        });
        options.AddDocumentTransformer((document, context, ct) => {
            document.Info.Title="ATLAS API";
            document.Info.Description="ATLAS session-authenticated API. Use the signed-in administrator session. Unsafe methods require X-ATLAS-CSRF from GET /api/auth/csrf. Scalar supplies this automatically. Endpoint execution always enforces the caller's existing permissions and organization scope.";
            document.Servers=[new OpenApiServer { Url="/api/atlas" }];
            // The browser proxy already adds /api; remove that prefix from generated paths.
            var paths=new OpenApiPaths();
            foreach (var path in document.Paths) if(path.Key.StartsWith("/api/")) paths.Add(path.Key[4..],path.Value);
            document.Paths=paths;
            document.Components ??=new OpenApiComponents();
            document.Components.SecuritySchemes ??=new Dictionary<string,IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Session"]=new OpenApiSecurityScheme { Type=SecuritySchemeType.ApiKey,In=ParameterLocation.Cookie,Name="atlas.session_token",Description="Existing HttpOnly session cookie; sign in through ATLAS. Never paste a session token into documentation." };
            document.Components.SecuritySchemes["CSRF"]=new OpenApiSecurityScheme { Type=SecuritySchemeType.ApiKey,In=ParameterLocation.Header,Name="X-ATLAS-CSRF",Description="Antiforgery token from GET /auth/csrf, automatically supplied by the embedded client." };
            return Task.CompletedTask;
        });
        options.AddOperationTransformer((operation, context, ct) => {
            var endpoint=context.Description.ActionDescriptor.EndpointMetadata;
            var anonymous=endpoint.OfType<IAllowAnonymous>().Any();
            var protectedEndpoint=!anonymous && endpoint.OfType<IAuthorizeData>().Any();
            var unsafeMethod=context.Description.HttpMethod is not ("GET" or "HEAD" or "OPTIONS");
            operation.Summary ??= context.Description.ActionDescriptor.RouteValues.TryGetValue("action",out var action) ? action : null;
            if(operation.RequestBody?.Content?.TryGetValue("application/json",out var json)==true)
            {
                var route=context.Description.RelativePath;
                if(route=="api/auth/login") json.Example=JsonNode.Parse("""{"email":"person@example.com","password":"ExamplePassword8"}""");
                if(route=="api/auth/register") json.Example=JsonNode.Parse("""{"name":"Example User","email":"person@example.com","password":"ExamplePassword8"}""");
            }
            operation.Description ??=protectedEndpoint ? "Requires an ATLAS session. Action-specific permissions and organizational scope are checked by the backend." : "Anonymous access permitted. Rate limits and CSRF protection apply where configured.";
            if(context.Description.RelativePath?.Contains("admin/api-management")==true) operation.Description+=" Administrator with admin.access required. Configuration updates additionally require admin.settings. Exports are limited to 1000 rows.";
            operation.Responses ??=new OpenApiResponses();
            foreach(var status in new[] {"400","401","403","404","409","429","500"})
                operation.Responses.TryAdd(status,new OpenApiResponse { Description=status switch {"400"=>"Invalid input or CSRF token.","401"=>"Authentication required or invalid credentials.","403"=>"Insufficient permissions or scope.","404"=>"Resource not found.","409"=>"Conflicting record or relationship.","429"=>"Rate limit exceeded.",_=>"Server error; no sensitive details are returned."}, Content=new Dictionary<string,OpenApiMediaType> { ["application/problem+json"]=new() { Schema=new OpenApiSchema { Type=JsonSchemaType.Object,Properties=new Dictionary<string,IOpenApiSchema> { ["status"]=new OpenApiSchema { Type=JsonSchemaType.Integer },["title"]=new OpenApiSchema {Type=JsonSchemaType.String},["detail"]=new OpenApiSchema { Type=JsonSchemaType.String } } } } } });
            // Security references are resolved against the document containing this operation.
            operation.Security=[];
            foreach(var status in new[] {"400","401","403","404","409","429","500"})
                if(operation.Responses[status].Content?.TryGetValue("application/problem+json",out var media)==true)
                    media.Example=System.Text.Json.JsonSerializer.SerializeToNode(new { status=int.Parse(status,System.Globalization.CultureInfo.InvariantCulture),title="Request failed.",detail=operation.Responses[status].Description });
            if(protectedEndpoint || unsafeMethod)
            {
                var requirement=new OpenApiSecurityRequirement();
                if(protectedEndpoint) requirement.Add(new OpenApiSecuritySchemeReference("Session",context.Document),[]);
                if(unsafeMethod) requirement.Add(new OpenApiSecuritySchemeReference("CSRF",context.Document),[]);
                operation.Security.Add(requirement);
            }
            return Task.CompletedTask;
        });
    }
    public static async Task AuditAsync(HttpContext context, IRbacAuditRepository audit, CancellationToken ct)
    {
        var actor=((AuthSession)context.Items["AtlasSession"]!).Context;
        await audit.InsertAsync(new RbacAudit { ActorUserId=actor.User.Id,ActorEmail=actor.User.Email,Action="api.documentation.read",Success=true,Diagnostic=$"Request ID: {context.Items["RequestId"]}" },ct);
    }
}
