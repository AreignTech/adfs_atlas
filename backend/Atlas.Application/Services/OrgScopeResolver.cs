using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;

namespace Atlas.Application.Services;

/// <summary>Read-only Lab/Section assignment lookup for the current user.</summary>
public sealed class OrgScopeResolver : IOrgScopeResolver
{
    private readonly IOrgAssignmentRepository _assignments;

    public OrgScopeResolver(IOrgAssignmentRepository assignments)
    {
        _assignments = assignments;
    }

    public async Task<OrgScopeDto> ResolveAsync(
        string userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _assignments.GetForUserAsync(userId, cancellationToken);
        return new OrgScopeDto(
            assignment?.LabId,
            assignment?.SectionId,
            assignment is { LabId: not null, SectionId: not null },
            isAdministrator);
    }
}
