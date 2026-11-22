using System.Security.Cryptography;
using Atlas.Application.Common;
using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Options;
using Atlas.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.Application.Services;

/// <summary>
/// Deliberate replacement for Better Auth's session handling.
///
/// Passwords are still verified with the existing Better Auth scrypt hashes, so
/// seeded and existing users sign in unchanged. Sessions use an opaque random
/// token stored in the preserved <c>session</c> table and delivered through an
/// HttpOnly cookie by the API layer.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IAccountRepository _accounts;
    private readonly ISessionRepository _sessions;
    private readonly IMemberRepository _members;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRbacResolver _rbac;
    private readonly IOrgScopeResolver _orgScope;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthService> _logger;
    private readonly IIdentityWriter _identityWriter;

    public AuthService(
        IUserRepository users,
        IAccountRepository accounts,
        ISessionRepository sessions,
        IMemberRepository members,
        IPasswordHasher passwordHasher,
        IRbacResolver rbac,
        IOrgScopeResolver orgScope,
        IOptions<AuthOptions> options,
        ILogger<AuthService> logger,
        IIdentityWriter identityWriter)
    {
        _users = users;
        _accounts = accounts;
        _sessions = sessions;
        _members = members;
        _passwordHasher = passwordHasher;
        _rbac = rbac;
        _orgScope = orgScope;
        _options = options.Value;
        _logger = logger;
        _identityWriter = identityWriter;
    }

    public async Task<AuthSession> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 255 || string.IsNullOrEmpty(request.Password) || request.Password.Length > 4000)
        {
            throw new ValidationException("Email and password are required.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("Login failed for unknown email {Email}", email);
            throw new UnauthorizedException("Invalid email or password.");
        }

        var account = await _accounts.GetCredentialAsync(user.Id, cancellationToken);
        if (account?.Password is null || !_passwordHasher.Verify(account.Password, request.Password))
        {
            _logger.LogWarning("Login failed for {Email}: invalid credentials", email);
            throw new UnauthorizedException("Invalid email or password.");
        }

        _logger.LogInformation("Login succeeded for {Email}", email);
        return await CreateSessionAsync(user, ipAddress, userAgent, cancellationToken);
    }

    public async Task<AuthSession> CreateDirectorySessionAsync(string userId, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default) => await CreateSessionAsync(await _users.GetByIdAsync(userId, cancellationToken) ?? throw new UnauthorizedException(), ipAddress, userAgent, cancellationToken);

    public async Task<AuthSession> RegisterAsync(
        RegisterRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (!_options.AllowSelfRegistration)
        {
            throw new ForbiddenException("Self-registration is disabled.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 2)
        {
            throw new ValidationException("Enter your name.");
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@', StringComparison.Ordinal))
        {
            throw new ValidationException("Enter a valid email address.");
        }

        ValidatePassword(request.Password);

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _users.EmailExistsAsync(email, cancellationToken))
        {
            throw new ConflictException("An account with that email already exists.");
        }

        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name.Trim(),
            Email = email,
            EmailVerified = false
        };
        var membership = new Member
        {
            UserId = user.Id,
            Role = "Users",
            Status = "invited"
        };
        var credential = new Account
        {
            Id = Guid.NewGuid().ToString("N"),
            AccountId = user.Id,
            ProviderId = "credential",
            UserId = user.Id,
            Password = _passwordHasher.Hash(request.Password)
        };
        await _identityWriter.CreateAsync(user, membership, credential, "Users", cancellationToken);

        _logger.LogInformation("Self-registration created {Email}", email);
        return await CreateSessionAsync(user, ipAddress, userAgent, cancellationToken);
    }

    public async Task<AuthSession?> GetSessionAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (token.Length > 128) return null;
        var session = await _sessions.GetByTokenAsync(HashToken(token), cancellationToken);
        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await _sessions.DeleteByIdAsync(session.Id, cancellationToken);
            return null;
        }

        var user = await _users.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null)
        {
            await _sessions.DeleteByIdAsync(session.Id, cancellationToken);
            return null;
        }

        var context = await BuildContextAsync(user, cancellationToken);
        return new AuthSession(token, session.ExpiresAt, context);
    }

    public async Task LogoutAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        await _sessions.DeleteByTokenAsync(HashToken(token), cancellationToken);
    }

    public async Task ChangePasswordAsync(
        string userId,
        string? currentToken,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException();

        var account = await _accounts.GetCredentialAsync(user.Id, cancellationToken);
        if (account?.Password is null ||
            !_passwordHasher.Verify(account.Password, request.CurrentPassword))
        {
            throw new ValidationException("Current password is incorrect.");
        }

        ValidatePassword(request.NewPassword);

        await _identityWriter.ChangePasswordAsync(
            user.Id,
            _passwordHasher.Hash(request.NewPassword),
            currentToken is null ? null : HashToken(currentToken),
            cancellationToken);

        // Sign out every other device, keeping the caller's session alive.
        _logger.LogInformation("Password changed for {Email}", user.Email);
    }

    private async Task<AuthSession> CreateSessionAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var days = _options.SessionDays > 0 ? _options.SessionDays : 7;
        var expiresAt = now.AddDays(days);
        var token = NewSessionToken();

        await _sessions.CreateAsync(new Session
        {
            Id = Guid.NewGuid().ToString("N"),
            Token = HashToken(token),
            UserId = user.Id,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = now,
            IpAddress = ipAddress,
            UserAgent = userAgent
        }, cancellationToken);

        var context = await BuildContextAsync(user, cancellationToken);
        return new AuthSession(token, expiresAt, context);
    }

    private async Task<SessionContextDto> BuildContextAsync(User user, CancellationToken cancellationToken)
    {
        var rbac = await _rbac.ResolveAsync(user, cancellationToken);
        var scope = await _orgScope.ResolveAsync(user.Id, rbac.IsAdministrator, cancellationToken);

        return new SessionContextDto(
            new UserDto(user.Id, user.Name, user.Email, user.EmailVerified, user.Image),
            rbac.RoleNames,
            rbac.Permissions,
            rbac.IsAdministrator,
            scope);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) ||
            password.Length < 8 || password.Length > 4000 ||
            !password.Any(char.IsUpper) ||
            !password.Any(char.IsDigit))
        {
            throw new ValidationException(
                "Use at least 8 characters with an uppercase letter and a number.");
        }
    }

    private static string NewSessionToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Deterministic <c>usr_</c> id, matching the existing invite/seed scheme:
    /// base64url(lowercased email) truncated to 22 characters.
    /// </summary>
    private static string SeedUserId(string email)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant());
        var base64 = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"usr_{base64[..Math.Min(22, base64.Length)]}";
    }
}
