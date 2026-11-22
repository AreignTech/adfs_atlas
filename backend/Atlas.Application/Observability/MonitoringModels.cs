namespace Atlas.Application.Observability;

public sealed record MonitoringSettings
{
    public bool Enabled { get; init; } = true;
    public bool DetailedErrors { get; init; } = true;
    public bool LogSuccessfulRequests { get; init; } = true;
    public int SlowThresholdMs { get; init; } = 1000;
    public int RetentionDays { get; init; } = 30;
    public bool CaptureRequestBody { get; init; }
    public bool CaptureResponseBody { get; init; }
    public int MaxRequestBodyBytes { get; init; } = 4096;
    public int MaxResponseBodyBytes { get; init; } = 4096;
    public string[] SensitiveFields { get; init; } = [];
    public string[] ExcludedPrefixes { get; init; } = [];
    public bool TracingEnabled { get; init; }
    public double TraceSampleRatio { get; init; } = 0.1;
}

public sealed class DiagnosticRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "activity";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public Guid? RequestId { get; set; }
    public Guid? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public string? UserId { get; set; }
    public string? Method { get; set; }
    public string? Endpoint { get; set; }
    public int Status { get; set; }
    public double DurationMs { get; set; }
    public bool IsSlow { get; set; }
    public string? Component { get; set; }
    public string? Severity { get; set; }
    public string Payload { get; set; } = "{}";
}

public sealed class DiagnosticFilter
{
    public string Kind { get; set; } = "activity";
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? Query { get; set; }
    public string? UserId { get; set; }
    public string? Method { get; set; }
    public string? Endpoint { get; set; }
    public int? Status { get; set; }
    public string? Result { get; set; }
    public Guid? RequestId { get; set; }
    public Guid? CorrelationId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string Sort { get; set; } = "timestamp";
    public bool Descending { get; set; } = true;
}
public sealed record DiagnosticPage(IReadOnlyList<DiagnosticRecord> Records, long Total, int Page, int PageSize);
public sealed record HealthSummary(long Total, long Successful, long Failed, double SuccessPercentage, double AverageMs, double P95Ms, long AuthenticationFailures, long RateLimited, long Slow);
public sealed record MetricBucket(string Label, long Total, long Failed, double AverageMs, double P95Ms);
public sealed record ApiStatistics(HealthSummary Summary, IReadOnlyList<MetricBucket> Timeline, IReadOnlyList<MetricBucket> Endpoints, IReadOnlyList<MetricBucket> SlowestEndpoints, IReadOnlyList<MetricBucket> StatusCodes, IReadOnlyList<MetricBucket> Methods);

public interface IMonitoringStore
{
    Task InsertAsync(IReadOnlyList<DiagnosticRecord> records, CancellationToken ct);
    Task<MonitoringSettings> SettingsAsync(CancellationToken ct);
    Task SaveSettingsAsync(MonitoringSettings settings, string actorId, string actorEmail, CancellationToken ct);
    Task<DiagnosticPage> SearchAsync(DiagnosticFilter filter, CancellationToken ct);
    Task<DiagnosticRecord?> FindAsync(Guid id, CancellationToken ct);
    Task<ApiStatistics> StatisticsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task PurgeAsync(int retentionDays, CancellationToken ct);
}
