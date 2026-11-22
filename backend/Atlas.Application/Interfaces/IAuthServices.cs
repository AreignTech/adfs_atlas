using Atlas.Application.DTOs;
using Atlas.Domain.Entities;

namespace Atlas.Application.Interfaces;

/// <summary>Resolved RBAC state for one authenticated user.</summary>
public sealed record RbacContext(
    string UserId,
    string Email,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<string> Permissions,
    bool IsAdministrator);

public interface IRbacResolver
{
    Task<RbacContext> ResolveAsync(User user, CancellationToken cancellationToken = default);
}

public interface IOrgScopeResolver
{
    Task<OrgScopeDto> ResolveAsync(
        string userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);
}

/// <summary>A validated session plus the context the frontend needs.</summary>
public sealed record AuthSession(
    string Token,
    DateTimeOffset ExpiresAt,
    SessionContextDto Context);

public interface IAuthService
{
    Task<AuthSession> CreateDirectorySessionAsync(string userId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);
    Task<AuthSession> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);

    Task<AuthSession> RegisterAsync(
        RegisterRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);

    Task<AuthSession?> GetSessionAsync(string? token, CancellationToken cancellationToken = default);

    Task LogoutAsync(string? token, CancellationToken cancellationToken = default);

    Task ChangePasswordAsync(
        string userId,
        string? currentToken,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}
