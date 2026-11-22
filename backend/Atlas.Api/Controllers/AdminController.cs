using System.Net.Mail;
using System.Text.RegularExpressions;
using Atlas.Application.Common;
using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Security;
using Atlas.Application.Services;
using Atlas.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

[Authorize, ApiController, Route("api/admin")]
public sealed class AdminController(AccessGuard guard, IAdminWriter writer, ILabRepository labs, ISectionRepository sections, IOrgAuditRepository orgAudits, IRoleRepository roles, IPermissionRepository permissions, IRolePermissionRepository rolePermissions, IRbacAuditRepository rbacAudits, IMemberRepository members, IOrgAssignmentRepository assignments) : ControllerBase
{
    private SessionContextDto Actor => ((AuthSession)HttpContext.Items["AtlasSession"]!).Context;
    [HttpGet("labs")]
    public async Task<object> Labs(bool includeInactive = false, CancellationToken ct = default) { if (includeInactive) await guard.PermissionAsync(Actor, "admin.settings", ct); return await labs.ListAsync(includeInactive, ct); }
    [HttpGet("sections")]
    public async Task<object> Sections(bool includeInactive = false, CancellationToken ct = default) { if (includeInactive) await guard.PermissionAsync(Actor, "admin.settings", ct); return await sections.ListAsync(includeInactive, ct); }
    private async Task<object> SaveCode(bool lab, Guid? id, OrgCodeInput input, CancellationToken ct)
    {
        await guard.PermissionAsync(Actor, "admin.settings", ct);
        WorkspaceService.Text(input.Name, 1, 120, "name"); WorkspaceService.Text(input.Description ?? "", 0, 2000, "description");
        if (!Regex.IsMatch(input.Code, "^[A-Za-z0-9_-]{1,40}$")) throw new ValidationException("Invalid organization code.");
        var entityId = await writer.SaveCodeAsync(lab, id, input, Actor.User.Email, ct);
        return lab ? (object)(await labs.GetByIdAsync(entityId, ct))! : (await sections.GetByIdAsync(entityId, ct))!;
    }
    [HttpPost("labs")] public Task<object> CreateLab(OrgCodeInput input, CancellationToken ct) => SaveCode(true, null, input, ct);
    [HttpPut("labs/{id:guid}")] public Task<object> UpdateLab(Guid id, OrgCodeInput input, CancellationToken ct) => SaveCode(true, id, input, ct);
    [HttpPost("sections")] public Task<object> CreateSection(OrgCodeInput input, CancellationToken ct) => SaveCode(false, null, input, ct);
    [HttpPut("sections/{id:guid}")] public Task<object> UpdateSection(Guid id, OrgCodeInput input, CancellationToken ct) => SaveCode(false, id, input, ct);
    [HttpDelete("labs/{id:guid}")] public async Task<object> DeleteLab(Guid id, CancellationToken ct) { await guard.PermissionAsync(Actor, "admin.settings", ct); await writer.DeleteCodeAsync(true, id, Actor.User.Email, ct); return new { success = true }; }
    [HttpDelete("sections/{id:guid}")] public async Task<object> DeleteSection(Guid id, CancellationToken ct) { await guard.PermissionAsync(Actor, "admin.settings", ct); await writer.DeleteCodeAsync(false, id, Actor.User.Email, ct); return new { success = true }; }
    [HttpGet("org-audit")] public async Task<object> OrgAudit(int limit = 200, CancellationToken ct = default) { await guard.PermissionAsync(Actor, "admin.access", ct); return await orgAudits.ListRecentAsync(Math.Clamp(limit, 1, 500), ct); }
    [HttpPost("users/{id}/organization")] public async Task<object> AssignOrg(string id, AssignOrgRequest input, CancellationToken ct) { await guard.PermissionAsync(Actor, "admin.settings", ct); await guard.ActiveOrgAsync(input.LabId, input.SectionId, ct); var previous = await assignments.GetForUserAsync(id, ct); await guard.ScopeAsync(Actor, previous?.LabId, previous?.SectionId, ct); await guard.ScopeAsync(Actor, input.LabId, input.SectionId, ct); await writer.AssignOrgAsync(id, input, Actor, ct); return new { success = true }; }
    [HttpGet("permissions")] public async Task<object> Permissions(CancellationToken ct) { await guard.PermissionAsync(Actor, "roles.view", ct); return await permissions.ListAsync(ct); }
    [HttpGet("rbac-audit")] public async Task<object> RbacAudit(CancellationToken ct) { await guard.PermissionAsync(Actor, "admin.audit_logs", ct); return await rbacAudits.ListRecentAsync(200, ct); }
    [HttpGet("roles")]
    public async Task<object> Roles(CancellationToken ct)
    {
        await guard.PermissionAsync(Actor, "roles.view", ct); var result = new List<RoleDto>();
        foreach (var role in await roles.ListAsync(ct)) result.Add(new RoleDto { Id = role.Id, Name = role.Name, Description = role.Description, IsActive = role.IsActive, IsSystemRole = role.IsSystemRole, CreatedAt = role.CreatedAt, UpdatedAt = role.UpdatedAt, AssignedUsers = await roles.CountAssignedUsersAsync(role.Id, ct), AssignedUserNames = (await roles.ListAssignedUserNamesAsync(role.Id, ct)).ToList(), PermissionKeys = (await rolePermissions.ListKeysForRolesAsync([role.Id], ct)).ToList() });
        return result;
    }
    private async Task<object> SaveRole(Guid? id, RoleInput input, CancellationToken ct)
    {
        await guard.PermissionAsync(Actor, id is null ? "roles.create" : "roles.edit", ct); WorkspaceService.Text(input.Name.Trim(), 2, 80, "role name"); WorkspaceService.Text(input.Description, 0, 1000, "description");
        if (input.PermissionKeys.Count > 100) throw new ValidationException("Too many permissions.");
        var selected = await permissions.GetByKeysAsync(input.PermissionKeys, ct);
        if (selected.Count != input.PermissionKeys.Distinct().Count()) throw new ValidationException("Unknown permission key.");
        var role = new AppRole { Id = id ?? Guid.Empty, Name = input.Name.Trim(), Description = input.Description };
        await writer.SaveRoleAsync(role, selected.Select(p => p.Id).ToList(), id is null, Actor, ct); return (await roles.GetByIdAsync(role.Id, ct))!;
    }
    [HttpPost("roles")] public Task<object> CreateRole(RoleInput input, CancellationToken ct) => SaveRole(null, input, ct);
    [HttpPut("roles/{id:guid}")] public Task<object> UpdateRole(Guid id, RoleInput input, CancellationToken ct) => SaveRole(id, input, ct);
    [HttpDelete("roles/{id:guid}")] public async Task<object> DeleteRole(Guid id, CancellationToken ct) { await guard.PermissionAsync(Actor, "roles.delete", ct); await writer.DeleteRoleAsync(id, Actor, ct); return new { success = true }; }
    private async Task<MemberWithUser> Target(Guid id, CancellationToken ct) { var target = (await members.ListWithUserAsync(ct)).SingleOrDefault(m => m.Id == id) ?? throw new NotFoundException("User was not found."); await guard.ScopeAsync(Actor, target.LabId, target.SectionId, ct); return target; }
    [HttpPost("members/{id:guid}/role")] public async Task<object> AssignRole(Guid id, AssignRoleRequest input, CancellationToken ct) { await guard.PermissionAsync(Actor, "users.assign_roles", ct); await Target(id, ct); await writer.AssignRoleAsync(id, input.RoleId, Actor, ct); return new { success = true }; }
    [HttpPost("members")]
    public async Task<MemberDto> Invite(InviteInput input, CancellationToken ct)
    {
        await guard.PermissionAsync(Actor, "users.create", ct); await guard.PermissionAsync(Actor, "users.assign_roles", ct); await guard.ActiveOrgAsync(input.LabId, input.SectionId, ct); await guard.ScopeAsync(Actor, input.LabId, input.SectionId, ct);
        WorkspaceService.Text(input.Name, 2, 120, "name"); if (!MailAddress.TryCreate(input.Email, out var email) || email.Address != input.Email || input.Email.Length > 255) throw new ValidationException("Invalid email.");
        var id = await writer.InviteAsync(input, Actor, ct); return WorkspaceService.ToDto((await members.ListWithUserAsync(ct)).Single(m => m.Id == id));
    }
    [HttpPost("members/{id:guid}/status")]
    public async Task<MemberDto> MemberStatus(Guid id, StatusUpdateRequest input, CancellationToken ct)
    {
        await guard.PermissionAsync(Actor, "users.edit", ct); await Target(id, ct); var status = input.Status.ToLowerInvariant(); if (!new[] { "active", "invited", "away" }.Contains(status)) throw new ValidationException("Invalid member status.");
        await writer.SetMemberStatusAsync(id, status, Actor, ct); return WorkspaceService.ToDto((await members.ListWithUserAsync(ct)).Single(m => m.Id == id));
    }
    [HttpDelete("members/{id:guid}")] public async Task<Guid> DeleteMember(Guid id, CancellationToken ct) { await guard.PermissionAsync(Actor, "users.delete", ct); await Target(id, ct); await writer.RemoveMemberAsync(id, Actor, ct); return id; }
}
