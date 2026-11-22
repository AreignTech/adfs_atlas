using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Atlas.Api.Observability;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Observability;
using Atlas.Domain.Entities;
using Atlas.Infrastructure.Data.Migrations;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Atlas.Tests;

public sealed class MonitoringTests : IAsyncLifetime
{
    private readonly AtlasFactory factory=new();
    private readonly List<string> users=[];
    private MonitoringSettings original=new();
    private const string Root="/api/admin/api-management";
    public async Task InitializeAsync()
    {
        using var scope=factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync();
        original=await scope.ServiceProvider.GetRequiredService<IMonitoringStore>().SettingsAsync(default);
    }
    public async Task DisposeAsync()
    {
        using var scope=factory.Services.CreateScope();
        var store=scope.ServiceProvider.GetRequiredService<IMonitoringStore>();
        await store.SaveSettingsAsync(original,"test-runner","test@atlas.test",default);
        factory.Services.GetRequiredService<MonitoringRuntime>().Update(original);
        foreach(var id in users) await scope.ServiceProvider.GetRequiredService<IUserRepository>().DeleteAsync(id);
        await factory.DisposeAsync();
    }
    private async Task<HttpClient> Client(string role)
    {
        using var scope=factory.Services.CreateScope(); var sp=scope.ServiceProvider;
        var id=Guid.NewGuid().ToString(); users.Add(id);
        var user=new User { Id=id,Name="Monitoring Test",Email=$"{id}@monitoring.test" };
        await sp.GetRequiredService<IIdentityWriter>().CreateAsync(user,new Member {UserId=id,Role=role,Status="active"},new Account { Id=Guid.NewGuid().ToString(),AccountId=id,UserId=id,ProviderId="credential",Password=sp.GetRequiredService<IPasswordHasher>().Hash("Monitoring123!") },role);
        var client=factory.CreateClient();
        (await AuthTests.PostAsync(client,"/api/auth/login",new {user.Email,password="Monitoring123!"})).EnsureSuccessStatusCode();
        return client;
    }
    private static async Task<HttpResponseMessage> Put(HttpClient client,MonitoringSettings settings)
    {
        var token=await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request=new HttpRequestMessage(HttpMethod.Put,Root+"/configuration") {Content=JsonContent.Create(settings)};
        request.Headers.Add("X-ATLAS-CSRF",token.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    [Fact]
    public async Task DocumentationAndAllDiagnosticEndpointsRequireAdministrator()
    {
        using var anonymous=factory.CreateClient(); using var ordinary=await Client("Users"); using var admin=await Client("Administrators");
        foreach(var path in new[] {"/openapi/v1.json",Root+"/openapi/v1.json",Root+"/openapi/v1.yaml",Root+"/activity",Root+"/statistics",Root+"/configuration",Root+"/ingestion",Root+"/export"})
        {
            Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await ordinary.GetAsync(path)).StatusCode);
            var result=await admin.GetAsync(path);
            Assert.True(result.IsSuccessStatusCode,$"{path}: {result.StatusCode} {await result.Content.ReadAsStringAsync()}");
        }
        var spec=await admin.GetFromJsonAsync<JsonElement>(Root+"/openapi/v1.json");
        Assert.True(spec.GetProperty("paths").TryGetProperty("/workspace",out _));
        Assert.True(spec.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Session",out _));
        var yaml=await admin.GetStringAsync(Root+"/openapi/v1.yaml"); Assert.Contains("openapi:",yaml);
        Assert.Equal(HttpStatusCode.Forbidden,(await Put(ordinary,original)).StatusCode);
        using var scope=factory.Services.CreateScope();
        Assert.Contains(await scope.ServiceProvider.GetRequiredService<IRbacAuditRepository>().ListRecentAsync(100),entry=>entry.Action=="api.documentation.read");
    }
    [Fact]
    public async Task RequestsAreCorrelatedRedactedPersistedAndConfigurationAudited()
    {
        using var admin=await Client("Administrators");
        (await Put(admin,original with { Enabled=true,SlowThresholdMs=1,CaptureRequestBody=true,CaptureResponseBody=true,TracingEnabled=true,TraceSampleRatio=1 })).EnsureSuccessStatusCode();
        var attacker=Guid.NewGuid();
        using var request=new HttpRequestMessage(HttpMethod.Get,"/api/workspace?api_key=NEVER_STORE_ME&search=PRIVATE_QUERY");
        request.Headers.Add("X-Correlation-ID",attacker.ToString());
        request.Headers.Add("X-Api-Key","NEVER_STORE_HEADER");
        var response=await admin.SendAsync(request); response.EnsureSuccessStatusCode();
        var requestId=Guid.Parse(response.Headers.GetValues("X-Request-ID").Single());
        var correlation=Guid.Parse(response.Headers.GetValues("X-Correlation-ID").Single()); Assert.NotEqual(attacker,correlation);
        var store=factory.Services.GetRequiredService<IMonitoringStore>();
        DiagnosticRecord? record=null;
        for(var attempt=0;attempt<40 && record is null;attempt++) { await Task.Delay(100); record=await store.FindAsync(requestId,default); }
        Assert.NotNull(record); Assert.Equal(correlation,record.CorrelationId); Assert.NotNull(record.TraceId); Assert.True(record.IsSlow);
        Assert.DoesNotContain("NEVER_STORE",record.Payload); Assert.DoesNotContain("PRIVATE_QUERY",record.Payload); Assert.Contains("[redacted]",record.Payload);
        var matching=await admin.GetFromJsonAsync<DiagnosticPage>(Root+$"/activity?requestId={requestId}"); Assert.Single(matching!.Records);
        var spans=await admin.GetFromJsonAsync<DiagnosticPage>(Root+$"/activity?kind=trace&correlationId={correlation}");
        Assert.NotEmpty(spans!.Records);
        var exported=await admin.GetStringAsync(Root+$"/export?requestId={requestId}&format=json"); Assert.Contains(requestId.ToString(),exported); Assert.DoesNotContain("NEVER_STORE",exported);
        using var scope=factory.Services.CreateScope();
        var audit=await scope.ServiceProvider.GetRequiredService<IRbacAuditRepository>().ListRecentAsync(100);
        Assert.Contains(audit,item=>item.Action=="api.configuration.changed"); Assert.Contains(audit,item=>item.Action=="api.diagnostics.export");
    }
    [Fact]
    public async Task SqlStatisticsFilteringPaginationAndExportsUseActualRows()
    {
        using var admin=await Client("Administrators"); var store=factory.Services.GetRequiredService<IMonitoringStore>();
        var from=new DateTimeOffset(2040,1,1,0,0,0,TimeSpan.Zero); var correlation=Guid.NewGuid();
        var records=new[] {200,200,401,500}.Select((status,index)=>new DiagnosticRecord { Timestamp=from.AddMinutes(index),CorrelationId=correlation,Status=status,DurationMs=(index+1)*100,Endpoint="/api/test-"+correlation,Method="GET",IsSlow=index>=2 }).ToArray();
        try
        {
            await store.InsertAsync(records,default);
            var stats=await store.StatisticsAsync(from,from.AddMinutes(10),default);
            Assert.Equal(4,stats.Summary.Total); Assert.Equal(2,stats.Summary.Failed); Assert.Equal(50,stats.Summary.SuccessPercentage); Assert.Equal(250,stats.Summary.AverageMs); Assert.Equal(385,stats.Summary.P95Ms,3);
            Assert.Equal(195,stats.StatusCodes.Single(bucket=>bucket.Label=="200").P95Ms,3);
            var result=await store.SearchAsync(new DiagnosticFilter {From=from,To=from.AddMinutes(10),CorrelationId=correlation,Result="failed",PageSize=1},default);
            Assert.Equal(2,result.Total); Assert.Single(result.Records);
            var csv=await admin.GetStringAsync(Root+$"/export?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(from.AddMinutes(10).ToString("O"))}&correlationId={correlation}&format=csv");
            Assert.Contains(correlation.ToString(),csv);
        }
        finally { await using var db=await factory.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync(); await db.ExecuteAsync("DELETE dbo.api_diagnostic_record WHERE correlation_id=@correlation",new {correlation}); }
    }
    [Fact]
    public void BodyRedactionDoesNotTrustFieldNamesOrMalformedJson()
    {
        var safe=DiagnosticSanitizer.Body(Encoding.UTF8.GetBytes("""{"password":"secret","other":"hidden secret","nested":{"email":"private","count":3},"enabled":true}"""),new());
        Assert.DoesNotContain("secret",safe); Assert.DoesNotContain("private",safe); Assert.Contains("\"count\":3",safe);
        Assert.Contains("omitted",DiagnosticSanitizer.Body(Encoding.UTF8.GetBytes("password=secret"),new()));
    }
    [Fact]
    public async Task FailedRequestsAndRateLimitsAreAutomaticallyRecorded()
    {
        using var anonymous=factory.CreateClient(); using var user=await Client("Users");
        var responses=new List<HttpResponseMessage> {
            await anonymous.GetAsync("/api/workspace"),
            await user.GetAsync(Root+"/activity"),
            await anonymous.GetAsync("/api/endpoint-that-does-not-exist"),
            await anonymous.PostAsJsonAsync("/api/auth/login",new {email="invalid",password="NEVER_LOG_THIS"}),
        };
        for(var i=0;i<22;i++) responses.Add(await AuthTests.PostAsync(anonymous,"/api/auth/login",new {email="missing@atlas.test",password="NEVER_LOG_THIS"}));
        Assert.Contains(responses,response=>response.StatusCode==HttpStatusCode.TooManyRequests);
        var store=factory.Services.GetRequiredService<IMonitoringStore>();
        foreach(var response in responses.GroupBy(response=>response.StatusCode).Select(group=>group.First()))
        {
            var id=Guid.Parse(response.Headers.GetValues("X-Request-ID").Single());
            DiagnosticRecord? record=null;
            for(var i=0;i<40 && record is null;i++) {await Task.Delay(100);record=await store.FindAsync(id,default);}
            Assert.NotNull(record); Assert.Equal((int)response.StatusCode,record.Status); Assert.DoesNotContain("NEVER_LOG_THIS",record.Payload);
        }
    }
    [Fact]
    public async Task StorageFailureDoesNotBreakNormalApiRequests()
    {
        await using var unavailable=factory.WithWebHostBuilder(builder=>builder.ConfigureServices(services=> {
            services.RemoveAll<IMonitoringStore>(); services.AddSingleton<IMonitoringStore,UnavailableStore>();
        }));
        using var client=unavailable.CreateClient();
        for(var i=0;i<3;i++) (await client.GetAsync("/api/auth/csrf")).EnsureSuccessStatusCode();
        await Task.Delay(1100);
        (await client.GetAsync("/api/auth/csrf")).EnsureSuccessStatusCode();
        var health=JsonSerializer.SerializeToElement(unavailable.Services.GetRequiredService<MonitoringRuntime>().Health);
        Assert.True(health.GetProperty("failures").GetInt64()>0);
    }

    private sealed class UnavailableStore : IMonitoringStore
    {
        public Task InsertAsync(IReadOnlyList<DiagnosticRecord> records,CancellationToken ct)=>Task.FromException(new IOException("Storage unavailable"));
        public Task<MonitoringSettings> SettingsAsync(CancellationToken ct)=>Task.FromException<MonitoringSettings>(new IOException("Storage unavailable"));
        public Task SaveSettingsAsync(MonitoringSettings settings,string actorId,string actorEmail,CancellationToken ct)=>throw new NotSupportedException();
        public Task<DiagnosticPage> SearchAsync(DiagnosticFilter filter,CancellationToken ct)=>throw new NotSupportedException();
        public Task<DiagnosticRecord?> FindAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<ApiStatistics> StatisticsAsync(DateTimeOffset from,DateTimeOffset to,CancellationToken ct)=>throw new NotSupportedException();
        public Task PurgeAsync(int retentionDays,CancellationToken ct)=>Task.CompletedTask;
    }
}
