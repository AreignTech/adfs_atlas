using System.Diagnostics;
using System.Text.Json;
using Atlas.Application.Observability;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Atlas.Api.Observability;

public sealed class MonitoringSampler(MonitoringRuntime runtime) : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters parameters) => !runtime.Settings.TracingEnabled
        ? new SamplingResult(SamplingDecision.Drop)
        : new TraceIdRatioBasedSampler(runtime.Settings.TraceSampleRatio).ShouldSample(parameters);
}

// Runs before export. Only allowlisted technical metadata leaves the process.
public sealed class SafeTraceProcessor(MonitoringRuntime runtime) : BaseProcessor<Activity>
{
    private static readonly HashSet<string> Allowed = ["http.request.method", "http.response.status_code", "http.route", "atlas.request_id", "atlas.correlation_id", "error.type"];
    public override void OnEnd(Activity activity)
    {
        var requestId = Find(activity, "atlas.request_id");
        var correlationId = Find(activity, "atlas.correlation_id");
        foreach (var tag in activity.TagObjects.ToArray())
            if (!Allowed.Contains(tag.Key)) activity.SetTag(tag.Key, null);
        foreach (var baggage in activity.Baggage.ToArray()) activity.SetBaggage(baggage.Key,null);
        activity.TraceStateString=null;
        // Events may contain raw exception messages / SQL. Instrumentation records no exception events.
        activity.DisplayName = activity.Source.Name=="Atlas.Application" && activity.OperationName is "Workspace.Get" or "Workspace.SaveProject"
            ? activity.OperationName
            : activity.Kind switch { ActivityKind.Client => "External or database operation", ActivityKind.Server => $"{activity.GetTagItem("http.request.method")} {activity.GetTagItem("http.route")}", _ => "Internal operation" };
        activity.SetStatus(activity.Status);
        runtime.Enqueue(new DiagnosticRecord {
            Kind = "trace", RequestId = Guid.TryParse(requestId, out var req) ? req : null,
            CorrelationId = Guid.TryParse(correlationId, out var corr) ? corr : null,
            TraceId = activity.TraceId.ToString(), Component = DiagnosticSanitizer.Safe(activity.Source.Name),
            DurationMs = activity.Duration.TotalMilliseconds, Status = activity.Status == ActivityStatusCode.Error ? 500 : 200,
            Payload = JsonSerializer.Serialize(new { spanId = activity.SpanId.ToString(), parentSpanId = activity.ParentSpanId.ToString(), operation = activity.DisplayName, kind = activity.Kind.ToString() })
        });
    }
    private static string? Find(Activity? activity, string key) { while (activity is not null) { if (activity.GetTagItem(key) is string value) return value; activity = activity.Parent; } return null; }
}

public sealed class CorrelationHandler(IHttpContextAccessor accessor, IConfiguration config) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var hosts = config.GetSection("Observability:CorrelationHosts").Get<string[]>() ?? [];
        if (request.RequestUri is not null && hosts.Contains(request.RequestUri.Host, StringComparer.OrdinalIgnoreCase) && accessor.HttpContext?.Items["CorrelationId"] is Guid id)
        {
            request.Headers.Remove("X-Correlation-ID");
            request.Headers.Add("X-Correlation-ID", id.ToString());
        }
        return base.SendAsync(request,ct);
    }
}
