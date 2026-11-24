using Atlas.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Atlas.Api.Middleware;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        context.Items["DiagnosticExceptionType"] = exception.GetType().FullName;
        System.Diagnostics.Activity.Current?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
        System.Diagnostics.Activity.Current?.SetTag("error.type",exception.GetType().FullName);
        context.Items["DiagnosticStack"] = new System.Diagnostics.StackTrace(exception, false).GetFrames()?.Take(20).Select(frame => frame.GetMethod()?.DeclaringType?.FullName + "." + frame.GetMethod()?.Name).ToArray();
        var status = exception switch { AtlasException e => e.StatusCode, BadHttpRequestException e => e.StatusCode, SqlException { Number: 2601 or 2627 } => 409, SqlException { Number: 547 } => 409, ArgumentException or FormatException or System.Text.Json.JsonException => 400, _ => 500 };
        var detail = exception is AtlasException atlas ? atlas.Message : status == 413 ? "The request is too large. Reduce the upload size and try again." : status == 409 ? "The operation conflicts with existing records or relationships." : status == 400 ? "Invalid input." : "The operation could not be completed.";
        if (status == 403 && context.Items["AtlasSession"] is Atlas.Application.Interfaces.AuthSession session)
            await context.RequestServices.GetRequiredService<Atlas.Application.Interfaces.Repositories.IRbacAuditRepository>().InsertAsync(new Atlas.Domain.Entities.RbacAudit { ActorUserId = session.Context.User.Id, ActorEmail = session.Context.User.Email, Action = "authorization.denied", Success = false, Diagnostic = $"Denied operation: {context.Request.Method} {context.Request.Path}" }, ct);
        if (status == 500) logger.LogError(exception, "API operation failed: {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = "Request failed.", Detail = detail }, ct);
        return true;
    }
}
