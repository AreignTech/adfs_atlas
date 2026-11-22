using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Atlas.Application.Observability;

namespace Atlas.Api.Observability;

public sealed class MonitoringRuntime(IMonitoringStore store) : BackgroundService
{
    private readonly Channel<DiagnosticRecord> queue = Channel.CreateBounded<DiagnosticRecord>(new BoundedChannelOptions(4096) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private MonitoringSettings settings = new();
    private long dropped, written, failures;
    public MonitoringSettings Settings => Volatile.Read(ref settings);
    public DateTimeOffset? LastWrite { get; private set; }
    public DateTimeOffset? LastFailure { get; private set; }
    public object Health => new { queued = queue.Reader.Count, dropped = Interlocked.Read(ref dropped), written = Interlocked.Read(ref written), failures = Interlocked.Read(ref failures), lastWrite = LastWrite, lastFailure = LastFailure };
    public void Update(MonitoringSettings value) => Volatile.Write(ref settings, value);
    public void Enqueue(DiagnosticRecord record)
    {
        if (!Settings.Enabled) return;
        if (!queue.Writer.TryWrite(record)) Interlocked.Increment(ref dropped);
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var suppression=OpenTelemetry.SuppressInstrumentationScope.Begin();
        try
        {
            while(queue.Reader.TryPeek(out _))
            {
                var batch=new List<DiagnosticRecord>();
                while(batch.Count<100 && queue.Reader.TryRead(out var record)) batch.Add(record);
                await store.InsertAsync(batch,timeout.Token);
                Interlocked.Add(ref written,batch.Count);
            }
        }
        catch { Interlocked.Increment(ref failures); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var tick = 0;
        do
        {
            try
            {
                // Suppress ingestion's own SQL spans and application logs to avoid recursion.
                using var suppression = OpenTelemetry.SuppressInstrumentationScope.Begin();
                if (tick % 15 == 0) Update(await store.SettingsAsync(stoppingToken));
                if (tick % 15 == 0) await store.PurgeAsync(Settings.RetentionDays, stoppingToken);
                var batch = new List<DiagnosticRecord>();
                while (batch.Count < 1000 && queue.Reader.TryRead(out var record)) batch.Add(record);
                if (batch.Count > 0)
                {
                    try
                    {
                        await store.InsertAsync(batch, stoppingToken);
                        Interlocked.Add(ref written, batch.Count);
                        LastWrite = DateTimeOffset.UtcNow;
                    }
                    catch { Interlocked.Add(ref dropped, batch.Count); throw; }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { Interlocked.Increment(ref failures); LastFailure = DateTimeOffset.UtcNow; }
            tick++;
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

// Reuses ILogger rather than replacing it. Stores event templates, never interpolated arguments or exception messages.
public sealed class DiagnosticLoggerProvider(MonitoringRuntime runtime, IHttpContextAccessor accessor) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new DiagnosticLogger(categoryName, runtime, accessor);
    public void Dispose() { }
    private sealed class DiagnosticLogger(string category, MonitoringRuntime runtime, IHttpContextAccessor accessor) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => runtime.Settings.Enabled && (level >= LogLevel.Warning || category.StartsWith("Atlas.")) && !category.Contains("Observability");
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            try
            {
                var context = accessor.HttpContext;
                var template = (state as IEnumerable<KeyValuePair<string, object?>>)?.FirstOrDefault(pair => pair.Key == "{OriginalFormat}").Value as string;
                runtime.Enqueue(new DiagnosticRecord {
                    Kind = "application", Severity = level.ToString(), Component = DiagnosticSanitizer.Safe(category),
                    RequestId = context?.Items["RequestId"] as Guid?, CorrelationId = context?.Items["CorrelationId"] as Guid?,
                    TraceId = Activity.Current?.TraceId.ToString(),
                    Payload = JsonSerializer.Serialize(new { eventId = eventId.Id, message = template switch {
                        "Login failed for unknown email {Email}" => "Login failed: account not found.",
                        "Login failed for {Email}: invalid credentials" => "Login failed: invalid credentials.",
                        "Login succeeded for {Email}" => "Login succeeded.",
                        "Self-registration created {Email}" => "Self-registration created an account.",
                        "Password changed for {Email}" => "Password changed.",
                        "Database health check failed" => "Database health check failed.",
                        "API operation failed: {TraceId}" => "API operation failed.",
                        _ => "Structured application event; argument values omitted." }, exceptionType = runtime.Settings.DetailedErrors ? exception?.GetType().FullName : null })
                });
            }
            catch { /* Diagnostic failures must never escape into application code. */ }
        }
    }
}
