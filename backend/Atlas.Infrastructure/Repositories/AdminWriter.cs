using System.Data;
using Atlas.Application.Common;
using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Domain.Entities;
using Dapper;

namespace Atlas.Infrastructure.Repositories;

public sealed class AdminWriter(IDbConnectionFactory factory) : IAdminWriter
{
    private async Task<T> TransactionAsync<T>(Func<System.Data.Common.DbConnection, System.Data.Common.DbTransaction, Task<T>> action, CancellationToken ct)
    {
        await using var connection = await factory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var result = await action(connection, transaction);
        await transaction.CommitAsync(ct);
        return result;
    }
    private static Task Audit(System.Data.Common.DbConnection c, System.Data.Common.DbTransaction t, SessionContextDto actor, string action, string? userId = null, Guid? roleId = null) => c.ExecuteAsync("INSERT INTO dbo.rbac_audit(actor_user_id,actor_email,action,affected_user_id,affected_role_id,success) VALUES(@ActorId,@ActorEmail,@Action,@UserId,@RoleId,1)", new { ActorId = actor.User.Id, ActorEmail = actor.User.Email, Action = action, UserId = userId, RoleId = roleId }, t);
    public Task<Guid> SaveCodeAsync(bool lab, Guid? id, OrgCodeInput input, string actor, CancellationToken ct) => TransactionAsync(async (c, t) =>
    {
        var table = lab ? "lab_code" : "section_code"; var entityId = id ?? Guid.NewGuid();
        var sql = id is null ? $"INSERT INTO dbo.{table}(id,code,name,description,is_active,created_by) VALUES(@Id,@Code,@Name,@Description,@IsActive,@Actor)" : $"UPDATE dbo.{table} SET code=@Code,name=@Name,description=@Description,is_active=@IsActive,updated_at=SYSUTCDATETIME() WHERE id=@Id";
        if (await c.ExecuteAsync(sql, new { Id = entityId, Code = input.Code.ToUpperInvariant(), input.Name, Description = input.Description ?? "", input.IsActive, Actor = actor }, t) != 1) throw new NotFoundException("Organization code was not found.");
        await c.ExecuteAsync("INSERT INTO dbo.org_audit(actor_email,action,affected_lab_id,affected_section_id,new_value,success) VALUES(@Actor,@Action,@LabId,@SectionId,@Value,1)", new { Actor = actor, Action = (lab ? "lab." : "section.") + (id is null ? "created" : "updated"), LabId = lab ? (Guid?)entityId : null, SectionId = lab ? null : (Guid?)entityId, Value = input.Code }, t);
        return entityId;
    }, ct);
    public async Task DeleteCodeAsync(bool lab, Guid id, string actor, CancellationToken ct) => await TransactionAsync(async (c, t) =>
    {
        var table = lab ? "lab_code" : "section_code";
        if (await c.ExecuteAsync($"DELETE FROM dbo.{table} WHERE id=@Id", new { Id = id }, t) != 1) throw new NotFoundException("Organization code was not found.");
        await c.ExecuteAsync("INSERT INTO dbo.org_audit(actor_email,action,affected_lab_id,affected_section_id,success) VALUES(@Actor,@Action,@LabId,@SectionId,1)", new { Actor = actor, Action = lab ? "lab.deleted" : "section.deleted", LabId = lab ? (Guid?)id : null, SectionId = lab ? null : (Guid?)id }, t);
        return true;
    }, ct);
    public async Task SaveRoleAsync(AppRole role, IReadOnlyList<Guid> permissions, bool create, SessionContextDto actor, CancellationToken ct) => await TransactionAsync(async (c, t) =>
    {
        if (create) role.Id = Guid.NewGuid();
        else
        {
            var previous = await c.QuerySingleOrDefaultAsync<AppRole>("SELECT * FROM dbo.app_role WITH(UPDLOCK,HOLDLOCK) WHERE id=@Id", role, t) ?? throw new NotFoundException("Role was not found.");
            if (previous.IsSystemRole && previous.Name != role.Name) throw new ValidationException("Default system role names cannot be changed.");
        }
        await c.ExecuteAsync(create ? "INSERT INTO dbo.app_role(id,name,description,is_system_role,is_active) VALUES(@Id,@Name,@Description,0,1)" : "UPDATE dbo.app_role SET name=@Name,description=@Description,updated_at=SYSUTCDATETIME() WHERE id=@Id", role, t);
        await c.ExecuteAsync("DELETE FROM dbo.app_role_permission WHERE role_id=@Id", role, t);
        foreach (var permissionId in permissions.Distinct()) await c.ExecuteAsync("INSERT INTO dbo.app_role_permission(role_id,permission_id) VALUES(@RoleId,@PermissionId)", new { RoleId = role.Id, PermissionId = permissionId }, t);
        await Audit(c, t, actor, create ? "role.created" : "role.updated", roleId: role.Id); return true;
    }, ct);
    public async Task DeleteRoleAsync(Guid id, SessionContextDto actor, CancellationToken ct) => await TransactionAsync(async (c, t) =>
    {
        var role = await c.QuerySingleOrDefaultAsync<AppRole>("SELECT * FROM dbo.app_role WITH(UPDLOCK,HOLDLOCK) WHERE id=@Id", new { Id = id }, t) ?? throw new NotFoundException("Role was not found.");
        if (role.IsSystemRole) throw new ForbiddenException("Default system roles are protected and cannot be deleted.");
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.app_user_role WHERE role_id=@Id", new { Id = id }, t) > 0) throw new ConflictException("Reassign users from this role before deleting it.");
        await c.ExecuteAsync("DELETE FROM dbo.app_role WHERE id=@Id", new { Id = id }, t); await Audit(c, t, actor, "role.deleted", roleId: id); return true;
    }, ct);
    private static async Task ProtectAdmin(System.Data.Common.DbConnection c, System.Data.Common.DbTransaction t, string userId)
    {
        var isAdmin = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.app_user_role ur JOIN dbo.app_role r ON r.id=ur.role_id WHERE ur.user_id=@UserId AND r.name='Administrators'", new { UserId = userId }, t) > 0;
        if (isAdmin && await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.app_user_role ur WITH(UPDLOCK,HOLDLOCK) JOIN dbo.app_role r ON r.id=ur.role_id JOIN dbo.member m ON m.user_id=ur.user_id WHERE r.name='Administrators' AND m.status='active'", transaction: t) <= 1)
            throw new ForbiddenException("The last active administrator cannot be removed, disabled or demoted.");
    }
    public async Task AssignRoleAsync(Guid memberId, Guid roleId, SessionContextDto actor, CancellationToken ct) => await TransactionAsync(async (c, t) =>
    {
        var member = await c.QuerySingleOrDefaultAsync<Member>("SELECT * FROM dbo.member WITH(UPDLOCK,HOLDLOCK) WHERE id=@Id", new { Id = memberId }, t) ?? throw new NotFoundException("User was not found.");
        if (member.UserId == actor.User.Id) throw new ForbiddenException("Administrators cannot change their own role.");
        var role = await c.QuerySingleOrDefaultAsync<AppRole>("SELECT * FROM dbo.app_role WHERE id=@Id", new { Id = roleId }, t) ?? throw new NotFoundException("Role was not found.");
        if (role.Name != "Administrators") await ProtectAdmin(c, t, member.UserId);
        await c.ExecuteAsync("DELETE FROM dbo.app_user_role WHERE user_id=@UserId; INSERT INTO dbo.app_user_role(user_id,role_id,assigned_by) VALUES(@UserId,@RoleId,@Actor); UPDATE dbo.member SET role=@RoleName WHERE id=@Id", new { member.UserId, RoleId = roleId, Actor = actor.User.Email, RoleName = role.Name, Id = memberId }, t);
        await Audit(c, t, actor, "user.role_changed", member.UserId, roleId); return true;
    }, ct);
    public async Task AssignOrgAsync(string userId, AssignOrgRequest input, SessionContextDto actor, CancellationToken ct) => await TransactionAsync(async (c, t) =>
    {
        await c.ExecuteAsync("UPDATE dbo.user_org_assignment WITH(UPDLOCK,HOLDLOCK) SET lab_id=@LabId,section_id=@SectionId,assigned_by=@Actor,assigned_at=SYSUTCDATETIME() WHERE user_id=@UserId; IF @@ROWCOUNT=0 INSERT INTO dbo.user_org_assignment(user_id,lab_id,section_id,assigned_by) VALUES(@UserId,@LabId,@SectionId,@Actor); INSERT INTO dbo.org_audit(actor_email,action,affected_user_id,affected_lab_id,affected_section_id,success) VALUES(@Actor,'user.organization.changed',@UserId,@LabId,@SectionId,1)", new { UserId = userId, input.LabId, input.SectionId, Actor = actor.User.Email }, t); return true;
    }, ct);
    public Task<Guid> InviteAsync(InviteInput input, SessionContextDto actor, CancellationToken ct) => TransactionAsync(async (c, t) =>
    {
        var id = Guid.NewGuid(); var userId = Guid.NewGuid().ToString();
        var role = await c.QuerySingleOrDefaultAsync<AppRole>("SELECT * FROM dbo.app_role WHERE id=@Id", new { Id = input.RoleId }, t) ?? throw new ValidationException("Select a valid application role.");
        await c.ExecuteAsync("INSERT INTO dbo.[user](id,name,email) VALUES(@UserId,@Name,@Email); INSERT INTO dbo.member(id,user_id,role,status) VALUES(@Id,@UserId,@Role,'invited'); INSERT INTO dbo.app_user_role(user_id,role_id,assigned_by) VALUES(@UserId,@RoleId,@Actor); INSERT INTO dbo.user_org_assignment(user_id,lab_id,section_id,assigned_by) VALUES(@UserId,@LabId,@SectionId,@Actor)", new { Id = id, UserId = userId, input.Name, Email = input.Email.Trim().ToLowerInvariant(), Role = role.Name, input.RoleId, input.LabId, input.SectionId, Actor = actor.User.Email }, t);
        await Audit(c, t, actor, "user.invited", userId, role.Id); return id;
    }, ct);
    public async Task SetMemberStatusAsync(Guid memberId, string status, SessionContextDto actor, CancellationToken ct) => await TransactionAsync(async (c, t) =>
    {
        var member = await c.QuerySingleOrDefaultAsync<Member>("SELECT * FROM dbo.member WITH(UPDLOCK,HOLDLOCK) WHERE id=@Id", new { Id = memberId }, t) ?? throw new NotFoundException("User was not found.");
        if (status != "active") await ProtectAdmin(c, t, member.UserId);
        await c.ExecuteAsync("UPDATE dbo.member SET status=@Status WHERE id=@Id", new { Status = status, Id = memberId }, t); await Audit(c, t, actor, "user.status_changed", member.UserId); return true;
    }, ct);
    public async Task RemoveMemberAsync(Guid memberId, SessionContextDto actor, CancellationToken ct, bool allowSelf = false) => await TransactionAsync(async (c, t) =>
    {
        var member = await c.QuerySingleOrDefaultAsync<Member>("SELECT * FROM dbo.member WITH(UPDLOCK,HOLDLOCK) WHERE id=@Id", new { Id = memberId }, t) ?? throw new NotFoundException("User was not found.");
        if (member.UserId == actor.User.Id && !allowSelf) throw new ForbiddenException("You cannot remove your own account here.");
        await ProtectAdmin(c, t, member.UserId);
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.project WHERE owner_id=@Id", new { Id = memberId }, t) > 0)
        {
            var successor = await c.ExecuteScalarAsync<Guid?>("SELECT TOP 1 m.id FROM dbo.member m LEFT JOIN dbo.user_org_assignment a ON a.user_id=m.user_id WHERE m.id<>@Id AND (@Admin=1 OR (a.lab_id=@LabId AND a.section_id=@SectionId)) ORDER BY m.joined_at,m.id", new { Id = memberId, Admin = actor.IsAdministrator, actor.Scope.LabId, actor.Scope.SectionId }, t) ?? throw new ConflictException("No eligible successor exists for the member's projects.");
            await c.ExecuteAsync("UPDATE dbo.project SET owner_id=@Successor,updated_at=SYSUTCDATETIME() WHERE owner_id=@Id", new { Id = memberId, Successor = successor }, t);
        }
        await c.ExecuteAsync("DELETE FROM dbo.[user] WHERE id=@Id", new { Id = member.UserId }, t); await Audit(c, t, actor, "user.removed", member.UserId); return true;
    }, ct);
}
