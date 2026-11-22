using Atlas.Application.DTOs;

namespace Atlas.Api.Observability;

// Response metadata for existing anonymous response objects; these mirror their wire shape.
public sealed record CsrfDocumentation(string Token);
public sealed record SessionExpiryDocumentation(DateTimeOffset ExpiresAt);
public sealed record SessionDocumentation(UserDto User, SessionExpiryDocumentation Session, SessionContextDto Context);
public sealed record SuccessDocumentation(bool Success);
public sealed record DirectoryStatusDocumentation(bool Enabled);
public sealed record DirectoryLoginDocumentation(bool Success, string RedirectTo);
public sealed record DirectoryTestDocumentation(bool Success, string Message);
public sealed record ProfileDocumentation(Atlas.Domain.Entities.User? User, System.Text.Json.JsonElement? Profile);
public sealed record PreferencesDocumentation(System.Text.Json.JsonElement? Value);
public sealed record IngestionDocumentation(int Queued, long Dropped, long Written, long Failures, DateTimeOffset? LastWrite, DateTimeOffset? LastFailure);
