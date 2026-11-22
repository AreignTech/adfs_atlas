using Atlas.Application.DTOs;
using Atlas.Domain.Entities;
namespace Atlas.Application.Interfaces;
public interface IAdminWriter
{
    Task<Guid> SaveCodeAsync(bool lab, Guid? id, OrgCodeInput input, string actor, CancellationToken ct);
    Task DeleteCodeAsync(bool lab, Guid id, string actor, CancellationToken ct);
    Task SaveRoleAsync(AppRole role, IReadOnlyList<Guid> permissions, bool create, SessionContextDto actor, CancellationToken ct);
    Task DeleteRoleAsync(Guid id, SessionContextDto actor, CancellationToken ct);
    Task AssignRoleAsync(Guid memberId, Guid roleId, SessionContextDto actor, CancellationToken ct);
    Task AssignOrgAsync(string userId, AssignOrgRequest input, SessionContextDto actor, CancellationToken ct);
    Task<Guid> InviteAsync(InviteInput input, SessionContextDto actor, CancellationToken ct);
    Task SetMemberStatusAsync(Guid memberId, string status, SessionContextDto actor, CancellationToken ct);
    Task RemoveMemberAsync(Guid memberId, SessionContextDto actor, CancellationToken ct, bool allowSelf = false);
}
