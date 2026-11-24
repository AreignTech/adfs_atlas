using System.Net.Mail;
using System.Text.Json;
using Atlas.Application.Common;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

[Authorize, ApiController, Route("api/settings")]
public sealed class SettingsController(ISettingsRepository settings, IUserRepository users) : ControllerBase
{
    private string UserId => ((AuthSession)HttpContext.Items["AtlasSession"]!).Context.User.Id;
    [HttpGet("profile")]
    [ProducesResponseType<Atlas.Api.Observability.ProfileDocumentation>(200)]
    public async Task<object> Profile(CancellationToken ct) => new { user = await users.GetByIdAsync(UserId, ct), profile = await settings.GetAsync(UserId, "profile", ct) };
    [HttpPost("profile")]
    [ProducesResponseType<Atlas.Api.Observability.SuccessDocumentation>(200)]
    [RequestSizeLimit(3_000_000)]
    public async Task<object> SaveProfile(JsonElement input, CancellationToken ct)
    {
        if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("firstName", out var first) || first.ValueKind != JsonValueKind.String || !input.TryGetProperty("lastName", out var last) || last.ValueKind != JsonValueKind.String || !input.TryGetProperty("email", out var emailValue) || emailValue.ValueKind != JsonValueKind.String) throw new ValidationException("First name, last name and email are required.");
        var name = ((input.GetProperty("firstName").GetString() ?? "") + " " + (input.GetProperty("lastName").GetString() ?? "")).Trim(); var email = input.GetProperty("email").GetString() ?? "";
        WorkspaceService.Text(name, 2, 120, "name"); if (!MailAddress.TryCreate(email, out var address) || address.Address != email || email.Length > 255) throw new ValidationException("Invalid email.");
        var image = input.TryGetProperty("profileImage", out var avatar) ? avatar.GetString() : null;
        if (image is not null && (image.Length > 2_500_000 || !(image.StartsWith("data:image/png;base64,") || image.StartsWith("data:image/jpeg;base64,") || image.StartsWith("data:image/webp;base64,")))) throw new ValidationException("Use a PNG, JPEG or WebP image under 2 MB.");
        await settings.SaveProfileAsync(UserId, name, email.ToLowerInvariant(), image, input, ct); return new { success = true };
    }
    [HttpGet("{key}")]
    [ProducesResponseType<Atlas.Api.Observability.PreferencesDocumentation>(200)]
    public async Task<object> Get(string key, CancellationToken ct) { ValidateKey(key); return new { value = await settings.GetAsync(UserId, key, ct) }; }
    [HttpPost("{key}")]
    [ProducesResponseType<Atlas.Api.Observability.SuccessDocumentation>(200)]
    public async Task<object> Save(string key, JsonElement input, CancellationToken ct)
    {
        ValidateKey(key); if (input.ValueKind != JsonValueKind.Object || input.GetRawText().Length > 50000) throw new ValidationException("Invalid settings payload.");
        if (key == "notifications")
        {
            if (!input.TryGetProperty("digestFrequency", out var digest) || digest.ValueKind != JsonValueKind.String || !new[] { "instant", "daily", "weekly" }.Contains(digest.GetString())) throw new ValidationException("Invalid digest frequency.");
            foreach (var field in new[] { "quietHoursEnabled", "productNews" }) if (!input.TryGetProperty(field, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ValidationException("Invalid notification preference.");
            foreach (var field in new[] { "quietHoursStart", "quietHoursEnd" }) if (!input.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String || !System.Text.RegularExpressions.Regex.IsMatch(value.GetString()!, "^([01][0-9]|2[0-3]):[0-5][0-9]$")) throw new ValidationException("Invalid quiet hours.");
            if (!input.TryGetProperty("matrix", out var matrix) || matrix.ValueKind != JsonValueKind.Object) throw new ValidationException("Invalid notification matrix.");
            foreach (var entry in matrix.EnumerateObject()) if (!new[] { "taskAssigned", "taskMentioned", "dueSoon", "overdue", "projectUpdate", "weeklyDigest" }.Contains(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object || entry.Value.EnumerateObject().Count() != 3 || entry.Value.EnumerateObject().Any(channel => !new[] { "email", "inApp", "push" }.Contains(channel.Name) || channel.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))) throw new ValidationException("Invalid notification channel.");
        }
        await settings.SaveAsync(UserId, key, input, ct); return new { success = true };
    }
    private static void ValidateKey(string key) { if (key != "notifications") throw new NotFoundException("Settings were not found."); }
}
