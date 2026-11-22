using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Atlas.Application.Observability;

namespace Atlas.Api.Observability;

public static partial class DiagnosticSanitizer
{
    private static readonly string[] Secrets = ["password", "secret", "token", "key", "cookie", "authorization", "credential", "payment", "email", "name", "address", "phone", "card", "cvv", "pin", "ssn", "bank", "account"];
    public static string Safe(string? value, int max = 255) => ControlCharacters().Replace(value ?? "", " ")[..Math.Min(max, ControlCharacters().Replace(value ?? "", " ").Length)];
    [GeneratedRegex(@"[\x00-\x1f\x7f]")]
    private static partial Regex ControlCharacters();
    public static string Body(byte[] bytes, MonitoringSettings settings, int maxBytes = 16384)
    {
        try
        {
            var node = JsonNode.Parse(bytes);
            var result=Redact(node, settings, 0)?.ToJsonString() ?? "null";
            return System.Text.Encoding.UTF8.GetByteCount(result)<=maxBytes ? result : "[omitted: sanitized body exceeds capture limit]";
        }
        catch { return "[omitted: non-JSON, oversized, or invalid body]"; }
    }
    private static JsonNode? Redact(JsonNode? node, MonitoringSettings settings, int depth)
    {
        if (depth > 12) return JsonValue.Create("[omitted: depth limit]");
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var property in obj.Take(100))
            {
                var sensitive = Secrets.Concat(settings.SensitiveFields).Any(key => property.Key.Contains(key, StringComparison.OrdinalIgnoreCase));
                var technicalPrimitive=property.Key is "position" or "page" or "pageSize" or "limit" or "offset" or "count" or "total" or "durationMs" or "enabled" or "isActive";
                var container=property.Value is JsonObject or JsonArray;
                result[Safe(property.Key, 80)] = sensitive || (!container && !technicalPrimitive) ? JsonValue.Create("[redacted]")
                    : technicalPrimitive && property.Value is JsonValue primitive && primitive.GetValueKind()!=JsonValueKind.String ? primitive.DeepClone()
                    : Redact(property.Value, settings, depth+1);
            }
            return result;
        }
        if (node is JsonArray array) return new JsonArray(array.Take(30).Select(item => Redact(item, settings, depth+1)).ToArray());
        // Arbitrary primitives can hold credentials. Only named technical fields above survive.
        if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String) return JsonValue.Create("[redacted]");
        return node is null ? null : JsonValue.Create("[redacted]");
    }
    public static Dictionary<string, string> Headers(IHeaderDictionary headers)
    {
        var result=new Dictionary<string,string>();
        if(headers.TryGetValue("Content-Type",out var type))
        {
            var media=type.ToString().Split(';')[0].Trim().ToLowerInvariant();
            result["Content-Type"]=new[] {"application/json","application/problem+json","text/plain","text/csv","application/octet-stream","multipart/form-data"}.Contains(media) ? media : "[other content type]";
        }
        if(headers.TryGetValue("Content-Length",out var length) && long.TryParse(length,out var count) && count>=0) result["Content-Length"]=count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return result;
    }
    public static Dictionary<string,string> Query(IQueryCollection query)
    {
        string[] known=["page","pageSize","sort","descending","from","to","status","method","userId","endpoint","query","requestId","correlationId","kind","result","format","includeInactive","limit","search"];
        return query.Take(30).Select((item,index)=>new KeyValuePair<string,string>(known.Contains(item.Key,StringComparer.OrdinalIgnoreCase) ? item.Key : $"[field-{index}]","[redacted]")).ToDictionary(pair=>pair.Key,pair=>pair.Value);
    }
    public static string Agent(string agent) => agent.Contains("Edg/") ? "Edge" : agent.Contains("Firefox/") ? "Firefox" : agent.Contains("Chrome/") ? "Chrome" : agent.Contains("Safari/") ? "Safari" : "Other / not supplied";
}
