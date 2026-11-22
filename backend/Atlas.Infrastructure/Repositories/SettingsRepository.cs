using System.Text.Json;
using Atlas.Application.Interfaces;
using Dapper;
namespace Atlas.Infrastructure.Repositories;

public sealed class SettingsRepository(IDbConnectionFactory factory) : ISettingsRepository
{
    public async Task<JsonElement?> GetAsync(string userId, string key, CancellationToken ct)
    {
        await using var c = await factory.OpenAsync(ct);
        var value = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT setting_value FROM dbo.user_setting WHERE user_id=@UserId AND setting_key=@Key", new { UserId = userId, Key = key }, cancellationToken: ct));
        return value is null ? null : JsonSerializer.Deserialize<JsonElement>(value);
    }
    private const string Save = "UPDATE dbo.user_setting WITH(UPDLOCK,HOLDLOCK) SET setting_value=@Value,updated_at=SYSUTCDATETIME() WHERE user_id=@UserId AND setting_key=@Key; IF @@ROWCOUNT=0 INSERT INTO dbo.user_setting(user_id,setting_key,setting_value) VALUES(@UserId,@Key,@Value);";
    public async Task SaveAsync(string userId, string key, JsonElement value, CancellationToken ct)
    {
        await using var c = await factory.OpenAsync(ct); await using var t = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(Save, new { UserId = userId, Key = key, Value = value.GetRawText() }, t, cancellationToken: ct)); await t.CommitAsync(ct);
    }
    public async Task SaveProfileAsync(string userId, string name, string email, string? image, JsonElement value, CancellationToken ct)
    {
        await using var c = await factory.OpenAsync(ct); await using var t = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("UPDATE dbo.[user] SET name=@Name,email_verified=CASE WHEN email=@Email THEN email_verified ELSE 0 END,email=@Email,image=@Image,updated_at=SYSUTCDATETIME() WHERE id=@UserId", new { UserId = userId, Name = name, Email = email, Image = image }, t, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition(Save, new { UserId = userId, Key = "profile", Value = value.GetRawText() }, t, cancellationToken: ct)); await t.CommitAsync(ct);
    }
}
