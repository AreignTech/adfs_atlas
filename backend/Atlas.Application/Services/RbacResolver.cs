using System.Text.RegularExpressions;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Security;
using Atlas.Domain.Entities;

namespace Atlas.Application.Services;

/// <summary>
/// Resolves a user's roles and permissions exactly as the original
/// <c>getRbacContext</c> did, including the legacy member-role fallback used
/// when no explicit app_user_role assignment exists.
/// </summary>
public sealed partial class RbacResolver : IRbacResolver
{
    private readonly IUserRoleRepository _userRoles;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IMemberRepository _members;

    public RbacResolver(
        IUserRoleRepository userRoles,
        IRolePermissionRepository rolePermissions,
        IMemberRepository members)
    {
        _userRoles = userRoles;
        _rolePermissions = rolePermissions;
        _members = members;
    }

    public async Task<RbacContext> ResolveAsync(User user, CancellationToken cancellationToken = default)
    {
        var assignments = await _userRoles.ListForUserAsync(user.Id, cancellationToken);

        IReadOnlyList<string> roleNames;
        IReadOnlyList<string> permissions;

        if (assignments.Count > 0)
        {
            roleNames = assignments
                .Select(assignment => assignment.RoleName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            permissions = await _rolePermissions.ListKeysForRolesAsync(
                assignments.Select(assignment => assignment.RoleId),
                cancellationToken);
        }
        else
        {
            var membership = await _members.GetByUserIdAsync(user.Id, cancellationToken);
            var fallbackRole = membership?.Role ?? "Users";
            roleNames = [fallbackRole];
            permissions = AtlasPermissions.ForLegacyRole(fallbackRole);
        }

        var isAdministrator =
            roleNames.Any(name => name.Equals("Administrators", StringComparison.OrdinalIgnoreCase)) ||
            roleNames.Any(name => AdminNamePattern().IsMatch(name));

        return new RbacContext(user.Id, user.Email, roleNames, permissions, isAdministrator);
    }

    [GeneratedRegex("admin", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AdminNamePattern();
}
