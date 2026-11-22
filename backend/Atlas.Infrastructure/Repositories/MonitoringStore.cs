using System.Text.Json;
using Atlas.Application.Interfaces;
using Atlas.Application.Observability;
using Dapper;

namespace Atlas.Infrastructure.Repositories;

public sealed class MonitoringStore(IDbConnectionFactory factory) : IMonitoringStore
{
    public async Task InsertAsync(IReadOnlyList<DiagnosticRecord> records, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT dbo.api_diagnostic_record(id,kind,timestamp,request_id,correlation_id,trace_id,user_id,method,endpoint,status,duration_ms,is_slow,component,severity,payload)
            SELECT id,kind,timestamp,request_id,correlation_id,trace_id,user_id,method,endpoint,status,duration_ms,is_slow,component,severity,payload
            FROM OPENJSON(@Records) WITH (
                id UNIQUEIDENTIFIER '$.Id',kind VARCHAR(16) '$.Kind',timestamp DATETIMEOFFSET '$.Timestamp',
                request_id UNIQUEIDENTIFIER '$.RequestId',correlation_id UNIQUEIDENTIFIER '$.CorrelationId',trace_id VARCHAR(32) '$.TraceId',
                user_id NVARCHAR(255) '$.UserId',method VARCHAR(10) '$.Method',endpoint NVARCHAR(512) '$.Endpoint',
                status INT '$.Status',duration_ms FLOAT '$.DurationMs',is_slow BIT '$.IsSlow',component NVARCHAR(255) '$.Component',
                severity VARCHAR(16) '$.Severity',payload NVARCHAR(MAX) '$.Payload')
            """, new {Records=JsonSerializer.Serialize(records)}, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }
    public async Task<MonitoringSettings> SettingsAsync(CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        var json = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT configuration FROM dbo.api_monitoring_config WHERE id=1", cancellationToken: ct));
        return json is null ? new() : JsonSerializer.Deserialize<MonitoringSettings>(json)!;
    }
    public async Task SaveSettingsAsync(MonitoringSettings settings, string actorId, string actorEmail, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.api_monitoring_config WITH(UPDLOCK,HOLDLOCK) SET configuration=@Json,updated_at=SYSUTCDATETIME() WHERE id=1;
            IF @@ROWCOUNT=0 INSERT dbo.api_monitoring_config(id,configuration) VALUES(1,@Json);
            INSERT dbo.rbac_audit(id,actor_user_id,actor_email,action,success,diagnostic,created_at)
            VALUES(NEWID(),@ActorId,@ActorEmail,'api.configuration.changed',1,'Monitoring settings updated',SYSUTCDATETIME());
            """, new { Json = JsonSerializer.Serialize(settings), ActorId = actorId, ActorEmail = actorEmail }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }
    private const string Where = """
        WHERE kind=@Kind AND timestamp>=@From AND timestamp<=@To
        AND (@UserId IS NULL OR user_id=@UserId) AND (@Method IS NULL OR method=@Method)
        AND (@Endpoint IS NULL OR CHARINDEX(@Endpoint,endpoint)>0) AND (@Status IS NULL OR status=@Status)
        AND (@RequestId IS NULL OR request_id=@RequestId) AND (@CorrelationId IS NULL OR correlation_id=@CorrelationId)
        AND (@Query IS NULL OR CHARINDEX(@Query,endpoint)>0 OR CHARINDEX(@Query,component)>0
          OR CONVERT(VARCHAR(36),request_id)=@Query OR CONVERT(VARCHAR(36),correlation_id)=@Query OR user_id=@Query OR CHARINDEX(@Query,payload)>0)
        AND (@Result IS NULL OR (@Result='failed' AND status>=400) OR (@Result='success' AND status BETWEEN 200 AND 399)
          OR (@Result='slow' AND is_slow=1) OR (@Result='authentication' AND status=401) OR (@Result='authorization' AND status=403))
        """;
    public async Task<DiagnosticPage> SearchAsync(DiagnosticFilter filter, CancellationToken ct)
    {
        var size = Math.Clamp(filter.PageSize, 1, 100);
        var page = Math.Clamp(filter.Page, 1, 100000);
        var args = new DynamicParameters(filter);
        args.Add("From", filter.From ?? DateTimeOffset.UtcNow.AddDays(-1));
        args.Add("To", filter.To ?? DateTimeOffset.UtcNow);
        args.Add("Offset", (page-1)*size); args.Add("Size", size);
        var sort = filter.Sort switch { "duration" => "duration_ms", "status" => "status", "method" => "method", "endpoint" => "endpoint", _ => "timestamp" };
        var direction = filter.Descending ? "DESC" : "ASC";
        await using var db = await factory.OpenAsync(ct);
        using var result = await db.QueryMultipleAsync(new CommandDefinition($"SELECT COUNT_BIG(*) FROM dbo.api_diagnostic_record {Where}; SELECT * FROM dbo.api_diagnostic_record {Where} ORDER BY {sort} {direction},id OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY", args, cancellationToken: ct));
        var count = await result.ReadSingleAsync<long>();
        return new((await result.ReadAsync<DiagnosticRecord>()).ToList(), count, page, size);
    }
    public async Task<DiagnosticRecord?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<DiagnosticRecord>(new CommandDefinition("SELECT * FROM dbo.api_diagnostic_record WHERE id=@Id", new { Id = id }, cancellationToken: ct));
    }
    public async Task<ApiStatistics> StatisticsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        // A bounded reporting window is validated by the controller. Aggregate in SQL, never load payloads.
        const string metrics = "COUNT_BIG(*) Total,COALESCE(SUM(CONVERT(BIGINT,CASE WHEN status>=400 THEN 1 ELSE 0 END)),0) Failed,COALESCE(AVG(duration_ms),0) AverageMs,MAX(group_p95) P95Ms";
        using var result = await db.QueryMultipleAsync(new CommandDefinition($"""
            SELECT status,duration_ms,endpoint,method,timestamp,is_slow,
              PERCENTILE_CONT(0.95) WITHIN GROUP(ORDER BY duration_ms) OVER() p95
            INTO #requests FROM dbo.api_diagnostic_record WHERE kind='activity' AND timestamp>=@From AND timestamp<=@To;
            SELECT COUNT_BIG(*) Total,
              COALESCE(SUM(CONVERT(BIGINT,CASE WHEN status BETWEEN 200 AND 399 THEN 1 ELSE 0 END)),0) Successful,
              COALESCE(SUM(CONVERT(BIGINT,CASE WHEN status>=400 THEN 1 ELSE 0 END)),0) Failed,
              CONVERT(FLOAT,COALESCE(100.0*SUM(CASE WHEN status BETWEEN 200 AND 399 THEN 1.0 ELSE 0 END)/NULLIF(COUNT_BIG(*),0),0)) SuccessPercentage,
              COALESCE(AVG(duration_ms),0) AverageMs,COALESCE(MAX(p95),0) P95Ms,
              COALESCE(SUM(CONVERT(BIGINT,CASE WHEN status=401 THEN 1 ELSE 0 END)),0) AuthenticationFailures,
              COALESCE(SUM(CONVERT(BIGINT,CASE WHEN status=429 THEN 1 ELSE 0 END)),0) RateLimited,
              COALESCE(SUM(CONVERT(BIGINT,is_slow)),0) Slow FROM #requests;
            SELECT CONVERT(VARCHAR(33),DATEADD(minute,bucket*@Bucket,@From),127) Label,COUNT_BIG(*) Total,
              SUM(CONVERT(BIGINT,CASE WHEN status>=400 THEN 1 ELSE 0 END)) Failed,AVG(duration_ms) AverageMs,MAX(bucketP95) P95Ms
            FROM (SELECT *,DATEDIFF(minute,@From,timestamp)/@Bucket bucket,
              PERCENTILE_CONT(0.95) WITHIN GROUP(ORDER BY duration_ms) OVER(PARTITION BY DATEDIFF(minute,@From,timestamp)/@Bucket) bucketP95 FROM #requests) t GROUP BY bucket ORDER BY bucket;
            SELECT *,PERCENTILE_CONT(0.95) WITHIN GROUP(ORDER BY duration_ms) OVER(PARTITION BY method,endpoint) group_p95 INTO #endpoint_requests FROM #requests;
            SELECT TOP(10) CONCAT(method,' ',endpoint) Label,{metrics} FROM #endpoint_requests GROUP BY method,endpoint ORDER BY Total DESC;
            SELECT TOP(10) CONCAT(method,' ',endpoint) Label,{metrics} FROM #endpoint_requests GROUP BY method,endpoint ORDER BY AverageMs DESC;
            SELECT CONVERT(VARCHAR(10),status) Label,{metrics} FROM (SELECT *,PERCENTILE_CONT(0.95) WITHIN GROUP(ORDER BY duration_ms) OVER(PARTITION BY status) group_p95 FROM #requests) t GROUP BY status ORDER BY status;
            SELECT method Label,{metrics} FROM (SELECT *,PERCENTILE_CONT(0.95) WITHIN GROUP(ORDER BY duration_ms) OVER(PARTITION BY method) group_p95 FROM #requests) t GROUP BY method ORDER BY Total DESC;
            DROP TABLE #endpoint_requests;
            DROP TABLE #requests;
            """, new { From = from, To = to, Bucket = Math.Max(1, (int)(to-from).TotalMinutes/60) }, cancellationToken: ct));
        return new(await result.ReadSingleAsync<HealthSummary>(), (await result.ReadAsync<MetricBucket>()).ToList(), (await result.ReadAsync<MetricBucket>()).ToList(), (await result.ReadAsync<MetricBucket>()).ToList(), (await result.ReadAsync<MetricBucket>()).ToList(), (await result.ReadAsync<MetricBucket>()).ToList());
    }
    public async Task PurgeAsync(int retentionDays, CancellationToken ct)
    {
        await using var db = await factory.OpenAsync(ct);
        for(var batch=0;batch<16;batch++)
        {
            var deleted=await db.ExecuteAsync(new CommandDefinition("DELETE TOP(2000) FROM dbo.api_diagnostic_record WHERE kind IN ('activity','application','trace') AND timestamp<@Cutoff", new { Cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays) }, cancellationToken: ct));
            if(deleted<2000) break;
        }
    }
}
