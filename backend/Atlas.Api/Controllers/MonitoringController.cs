using System.Globalization;
using System.Text;
using System.Text.Json;
using Atlas.Api.Observability;
using Atlas.Application.Common;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Observability;
using Atlas.Application.Security;
using Atlas.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Atlas.Api.Controllers;

[Authorize(Policy = "ApiManagement"), ApiController, Route("api/admin/api-management")]
public sealed class MonitoringController(IMonitoringStore store, MonitoringRuntime runtime, IRbacAuditRepository audit, AccessGuard guard) : ControllerBase
{
    private AuthSession Session => (AuthSession)HttpContext.Items["AtlasSession"]!;
    private Task Audit(string action, CancellationToken ct) => audit.InsertAsync(new RbacAudit { ActorUserId = Session.Context.User.Id, ActorEmail = Session.Context.User.Email, Action = action, Success = true, Diagnostic = $"Request ID: {HttpContext.Items["RequestId"]}; correlation ID: {HttpContext.Items["CorrelationId"]}" }, ct);
    [HttpGet("activity")]
    public async Task<DiagnosticPage> Activity([FromQuery] DiagnosticFilter filter, CancellationToken ct)
    {
        Validate(filter);
        await Audit("api.diagnostics.search",ct);
        return await store.SearchAsync(filter,ct);
    }
    [HttpGet("activity/{id:guid}")]
    public async Task<DiagnosticRecord> Detail(Guid id,CancellationToken ct)
    {
        await Audit("api.diagnostics.read",ct);
        return await store.FindAsync(id,ct) ?? throw new NotFoundException("Diagnostic record not found.");
    }
    [HttpGet("statistics")]
    public async Task<ApiStatistics> Statistics(DateTimeOffset? from,DateTimeOffset? to,CancellationToken ct)
    {
        var filter = new DiagnosticFilter { From = from, To = to }; Validate(filter);
        return await store.StatisticsAsync(filter.From!.Value,filter.To!.Value,ct);
    }
    [HttpGet("ingestion")]
    [ProducesResponseType<IngestionDocumentation>(200)]
    public object Ingestion() => runtime.Health;
    [HttpGet("configuration")]
    public async Task<MonitoringSettings> Configuration(CancellationToken ct) => await store.SettingsAsync(ct);
    [HttpPut("configuration")]
    public async Task<MonitoringSettings> SaveConfiguration(MonitoringSettings input,CancellationToken ct)
    {
        await guard.PermissionAsync(Session.Context,"admin.settings",ct);
        if (input.SlowThresholdMs is < 1 or > 300000 || input.RetentionDays is < 1 or > 365 ||
            input.MaxRequestBodyBytes is < 0 or > 16384 || input.MaxResponseBodyBytes is < 0 or > 16384 ||
            !double.IsFinite(input.TraceSampleRatio) || input.TraceSampleRatio is < 0 or > 1 ||
            input.SensitiveFields is null || input.ExcludedPrefixes is null || input.SensitiveFields.Length > 50 || input.ExcludedPrefixes.Length > 30 ||
            input.SensitiveFields.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 80 || !s.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) ||
            input.ExcludedPrefixes.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 200 || !s.StartsWith("/api/") || s.Any(char.IsControl)))
            throw new ValidationException("Invalid monitoring settings. Check thresholds, retention, capture limits and rules.");
        await store.SaveSettingsAsync(input,Session.Context.User.Id,Session.Context.User.Email,ct);
        runtime.Update(input);
        return input;
    }
    [HttpGet("export"), EnableRateLimiting("diagnostic-export")]
    public async Task<IActionResult> Export([FromQuery] DiagnosticFilter filter, string format = "json", CancellationToken ct = default)
    {
        Validate(filter);
        if (format is not ("json" or "csv")) throw new ValidationException("Use json or csv.");
        await Audit("api.diagnostics.export",ct);
        // Bounded exports use the same filters and sorting as the table; max 1000 rows per export.
        var records = new List<DiagnosticRecord>();
        filter.PageSize=100;
        for (var page=1;page<=10;page++) { filter.Page=page; var result=await store.SearchAsync(filter,ct); records.AddRange(result.Records); if (result.Records.Count<100) break; }
        Response.Headers["X-Export-Limit"]="1000";
        if (format=="json") return File(JsonSerializer.SerializeToUtf8Bytes(records,new JsonSerializerOptions(JsonSerializerDefaults.Web)),"application/json","api-diagnostics.json");
        static string Csv(object? value) { var text=Convert.ToString(value,CultureInfo.InvariantCulture) ?? ""; if (text.TrimStart().StartsWithAny('=','+','-','@','\t','\r')) text="'"+text; return "\""+text.Replace("\"","\"\"")+"\""; }
        var csv=new StringBuilder("Timestamp,RequestId,CorrelationId,UserId,Method,Endpoint,Status,DurationMs,Kind,Component\r\n");
        foreach(var record in records) csv.AppendLine(string.Join(",", new object?[] {record.Timestamp,record.RequestId,record.CorrelationId,record.UserId,record.Method,record.Endpoint,record.Status,record.DurationMs,record.Kind,record.Component}.Select(Csv)));
        return File(Encoding.UTF8.GetBytes(csv.ToString()),"text/csv","api-diagnostics.csv");
    }
    private static void Validate(DiagnosticFilter filter)
    {
        filter.From ??= DateTimeOffset.UtcNow.AddDays(-1); filter.To ??= DateTimeOffset.UtcNow;
        if (filter.From>filter.To || filter.To-filter.From>TimeSpan.FromDays(31) || filter.Kind is not ("activity" or "application" or "trace") ||
            filter.Query?.Length>255 || filter.Endpoint?.Length>512 || filter.UserId?.Length>255 || filter.Method?.Length>10)
            throw new ValidationException("Choose a valid reporting range of at most 31 days and valid search filters.");
    }
}

internal static class CsvExtensions
{
    internal static bool StartsWithAny(this string value, params char[] chars) => value.Length>0 && chars.Contains(value[0]);
}
