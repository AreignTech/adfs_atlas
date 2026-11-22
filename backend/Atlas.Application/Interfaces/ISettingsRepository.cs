using System.Text.Json;
namespace Atlas.Application.Interfaces;
public interface ISettingsRepository
{
    Task<JsonElement?> GetAsync(string userId, string key, CancellationToken ct);
    Task SaveAsync(string userId, string key, JsonElement value, CancellationToken ct);
    Task SaveProfileAsync(string userId, string name, string email, string? image, JsonElement value, CancellationToken ct);
}
