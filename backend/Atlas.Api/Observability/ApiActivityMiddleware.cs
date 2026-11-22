using System.Diagnostics;
using System.Text.Json;
using Atlas.Application.Interfaces;
using Atlas.Application.Observability;
using Microsoft.AspNetCore.Routing;

namespace Atlas.Api.Observability;

public sealed class ApiActivityMiddleware(RequestDelegate next, IConfiguration config, ILogger<ApiActivityMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, MonitoringRuntime runtime)
    {
        var requestId = Guid.NewGuid();
        // Inbound IDs are accepted only from explicitly configured immediate peers, never from forwarded headers.
        var trusted = config.GetSection("Observability:TrustedCorrelationPeers").Get<string[]>() ?? [];
        var correlationId = trusted.Contains(context.Connection.RemoteIpAddress?.ToString()) && Guid.TryParse(context.Request.Headers["X-Correlation-ID"], out var supplied) && supplied != Guid.Empty ? supplied : Guid.NewGuid();
        context.Items["RequestId"] = requestId;
        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers["X-Request-ID"] = requestId.ToString();
        context.Response.Headers["X-Correlation-ID"] = correlationId.ToString();
        Activity.Current?.SetTag("atlas.request_id", requestId.ToString());
        Activity.Current?.SetTag("atlas.correlation_id", correlationId.ToString());
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId, ["CorrelationId"] = correlationId });
        var settings = runtime.Settings;
        var path = context.Request.Path.Value ?? "";
        var log = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && settings.Enabled && !settings.ExcludedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        // Authentication, settings, directory, diagnostics, session and account endpoints never capture bodies.
        var bodyAllowed = new[] { "/api/workspace", "/api/projects", "/api/tasks" }.Any(prefix => path.Equals(prefix,StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix+"/",StringComparison.OrdinalIgnoreCase));
        string? requestBody = null;
        if (log && bodyAllowed && settings.CaptureRequestBody && context.Request.ContentType?.StartsWith("application/json") == true && context.Request.ContentLength is > 0 && context.Request.ContentLength <= settings.MaxRequestBodyBytes)
        {
            try
            {
                context.Request.EnableBuffering();
                var bytes = new byte[(int)context.Request.ContentLength];
                var count = await context.Request.Body.ReadAtLeastAsync(bytes, bytes.Length, false, context.RequestAborted);
                requestBody = count == bytes.Length ? DiagnosticSanitizer.Body(bytes, settings,settings.MaxRequestBodyBytes) : "[omitted]";
            }
            catch { requestBody = "[omitted]"; }
            finally { if (context.Request.Body.CanSeek) context.Request.Body.Position = 0; }
        }
        var original = context.Response.Body;
        using var capture = new BoundedResponseStream(original, log && bodyAllowed && settings.CaptureResponseBody ? settings.MaxResponseBodyBytes : 0);
        if (log) context.Response.Body = capture;
        var watch = Stopwatch.StartNew();
        try { await next(context); }
        finally
        {
            context.Response.Body = original;
            watch.Stop();
            if (log && (settings.LogSuccessfulRequests || context.Response.StatusCode >= 400))
            {
                try
                {
                    var actor = (context.Items["AtlasSession"] as AuthSession)?.Context;
                    var endpoint = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "/[unmatched]";
                    runtime.Enqueue(new DiagnosticRecord {
                        Id = requestId, RequestId = requestId, CorrelationId = correlationId,
                        TraceId = Activity.Current?.TraceId.ToString(), UserId = actor?.User.Id,
                        Method = DiagnosticSanitizer.Safe(context.Request.Method, 10), Endpoint = DiagnosticSanitizer.Safe(endpoint, 512),
                        Status = context.Response.StatusCode, DurationMs = watch.Elapsed.TotalMilliseconds, IsSlow = watch.Elapsed.TotalMilliseconds >= settings.SlowThresholdMs,
                        Component = context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()?.ControllerName,
                        Payload = JsonSerializer.Serialize(new {
                            authenticated = context.User.Identity?.IsAuthenticated ?? false,
                            inboundTraceContextTrusted = trusted.Contains(context.Connection.RemoteIpAddress?.ToString()),
                            ipAddress = context.Connection.RemoteIpAddress?.ToString(), userAgent = DiagnosticSanitizer.Agent(context.Request.Headers.UserAgent.ToString()),
                            query = DiagnosticSanitizer.Query(context.Request.Query),
                            requestHeaders = DiagnosticSanitizer.Headers(context.Request.Headers), responseHeaders = DiagnosticSanitizer.Headers(context.Response.Headers),
                            requestBody, responseBody = capture.HasBody && context.Response.ContentType?.StartsWith("application/json") == true ? DiagnosticSanitizer.Body(capture.Bytes, settings,settings.MaxResponseBodyBytes) : null,
                            responseSize = capture.TotalBytes, exceptionType = settings.DetailedErrors ? context.Items["DiagnosticExceptionType"] : null,
                            stack = settings.DetailedErrors ? context.Items["DiagnosticStack"] : null,
                            error = context.Response.StatusCode >= 400 ? "Request failed. Inspect status, component and related application events." : null,
                            spanId = Activity.Current?.SpanId.ToString()
                        })
                    });
                }
                catch { /* Includes aborted requests and malformed metadata. Never fail the response. */ }
            }
        }
    }
}

internal sealed class BoundedResponseStream(Stream inner, int limit) : Stream
{
    private readonly MemoryStream buffer = new();
    public long TotalBytes { get; private set; }
    public bool HasBody => limit > 0 && TotalBytes > 0 && TotalBytes <= limit;
    public byte[] Bytes => buffer.ToArray();
    private void Capture(ReadOnlySpan<byte> value) { TotalBytes += value.Length; if (limit > 0 && TotalBytes <= limit) buffer.Write(value); else if (buffer.Length > 0) buffer.SetLength(0); }
    public override void Write(byte[] value, int offset, int count) { Capture(value.AsSpan(offset,count)); inner.Write(value,offset,count); }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> value, CancellationToken ct = default) { Capture(value.Span); await inner.WriteAsync(value,ct); }
    public override Task WriteAsync(byte[] value,int offset,int count,CancellationToken ct) => WriteAsync(value.AsMemory(offset,count),ct).AsTask();
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] b,int o,int c) => throw new NotSupportedException();
    public override long Seek(long o,SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) buffer.Dispose(); base.Dispose(disposing); }
}
