using Atlas.Application.Common;
using Atlas.Application.DTOs;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;

namespace Atlas.Application.Security;

public sealed class AccessGuard(IRbacAuditRepository rbacAudit, IOrgAuditRepository orgAudit, ILabRepository labs, ISectionRepository sections)
{
    public async Task PermissionAsync(SessionContextDto actor, string permission, CancellationToken ct = default)
    {
        if (actor.Permissions.Contains(permission, StringComparer.Ordinal)) return;
        await rbacAudit.InsertAsync(new RbacAudit { ActorUserId = actor.User.Id, ActorEmail = actor.User.Email, Action = "authorization.denied", Success = false, Diagnostic = $"Missing permission: {permission}" }, ct);
        throw new ForbiddenException();
    }
    public async Task ScopeAsync(SessionContextDto actor, Guid? labId, Guid? sectionId, CancellationToken ct = default)
    {
        if (actor.IsAdministrator) return;
        if (actor.Scope.IsAssigned && labId is not null && sectionId is not null && actor.Scope.LabId == labId && actor.Scope.SectionId == sectionId) return;
        await orgAudit.InsertAsync(new OrgAudit { ActorEmail = actor.User.Email, Action = "org.access.denied", AffectedLabId = labId, AffectedSectionId = sectionId, Success = false, Diagnostic = "Requested organizational scope did not match the account assignment." }, ct);
        throw new ForbiddenException("You do not have access to this organizational area.");
    }
    public async Task ActiveOrgAsync(Guid? labId, Guid? sectionId, CancellationToken ct = default)
    {
        if (labId is null || sectionId is null || await labs.GetByIdAsync(labId.Value, ct) is not { IsActive: true } || await sections.GetByIdAsync(sectionId.Value, ct) is not { IsActive: true })
            throw new ValidationException("Select valid active Lab and Section Codes.");
    }
}
