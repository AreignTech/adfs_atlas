using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Atlas.Infrastructure.Data.Migrations;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Atlas.Tests;

public sealed class AdminWorkflowTests
{
    [Fact]
    public async Task LastActiveAdministratorCannotBeDemotedDisabledOrRemoved()
    {
        await using var factory = new AtlasFactory(); using var scope = factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        var userRoles = sp.GetRequiredService<IUserRoleRepository>();
        Assert.Equal(1, await userRoles.CountActiveAdministratorsAsync());
        MemberWithUser? administrator = null;
        foreach (var member in await sp.GetRequiredService<IMemberRepository>().ListWithUserAsync())
            if (member.Status == "active" && (await userRoles.ListForUserAsync(member.UserId)).Any(r => r.RoleName == "Administrators")) administrator = member;
        Assert.NotNull(administrator);
        var usersRole = (await sp.GetRequiredService<IRoleRepository>().GetByNameAsync("Users"))!;
        var actor = new Atlas.Application.DTOs.SessionContextDto(new("test-authority", "Test", "test@authority.local", false, null), ["Administrators"], Atlas.Application.Security.AtlasPermissions.All, true, new(null, null, false, true));
        var writer = sp.GetRequiredService<IAdminWriter>();
        await Assert.ThrowsAsync<Atlas.Application.Common.ForbiddenException>(() => writer.AssignRoleAsync(administrator.Id, usersRole.Id, actor, default));
        await Assert.ThrowsAsync<Atlas.Application.Common.ForbiddenException>(() => writer.SetMemberStatusAsync(administrator.Id, "away", actor, default));
        await Assert.ThrowsAsync<Atlas.Application.Common.ForbiddenException>(() => writer.RemoveMemberAsync(administrator.Id, actor, default));
        Assert.Equal("Administrators", (await userRoles.GetForUserAsync(administrator.UserId))!.RoleName);
        Assert.Equal("active", (await sp.GetRequiredService<IMemberRepository>().GetByIdAsync(administrator.Id))!.Status);
    }
    private static async Task<JsonElement> Post(HttpClient client, string path, object body)
    {
        var response = await AuthTests.PostAsync(client, path, body); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<JsonElement>());
    }
    private static async Task Delete(HttpClient client, string path)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Delete, path); request.Headers.Add("X-ATLAS-CSRF", token.GetProperty("token").GetString()); (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task RoleOrganizationInvitationAndMemberLifecycleAreTransactionalAndAudited()
    {
        await using var factory = new AtlasFactory(); using var scope = factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        await sp.GetRequiredService<DatabaseMigrator>().MigrateAsync();
        var id = Guid.NewGuid().ToString(); var email = $"{id}@admin.test";
        await sp.GetRequiredService<IIdentityWriter>().CreateAsync(new User { Id = id, Email = email, Name = "Test Administrator" }, new Member { UserId = id, Role = "Administrators", Status = "active" }, new Account { Id = Guid.NewGuid().ToString(), UserId = id, AccountId = id, ProviderId = "credential", Password = sp.GetRequiredService<IPasswordHasher>().Hash("AdminWorkflow123!") }, "Administrators");
        string? invitedId = null; Guid? labId = null, sectionId = null, roleId = null;
        try
        {
            var client = factory.CreateClient(); await Post(client, "/api/auth/login", new { email, password = "AdminWorkflow123!" });
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var lab = await Post(client, "/api/admin/labs", new { code = "L" + suffix, name = "Lifecycle lab", isActive = true }); labId = lab.GetProperty("id").GetGuid();
            var section = await Post(client, "/api/admin/sections", new { code = "S" + suffix, name = "Lifecycle section", isActive = true }); sectionId = section.GetProperty("id").GetGuid();
            var role = await Post(client, "/api/admin/roles", new { name = "Lifecycle " + suffix, description = "Test", permissionKeys = new[] { "tasks.view" } }); roleId = role.GetProperty("id").GetGuid();
            Assert.True(role.GetProperty("createdAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-1));
            var member = await Post(client, "/api/admin/members", new { name = "Invited User", email = $"{suffix}@invite.test", roleId, labId, sectionId }); invitedId = member.GetProperty("userId").GetString(); var memberId = member.GetProperty("id").GetGuid();
            Assert.NotNull(invitedId);
            Assert.Equal("Invited", member.GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await AuthTests.PostAsync(client, $"/api/admin/members/{memberId}/status", new { status = "Active" })).StatusCode);
            using var invitee = factory.CreateClient();
            var invitation = await Post(client, $"/api/admin/members/{memberId}/invitation", new { });
            var oldToken = invitation.GetProperty("token").GetString();
            invitation = await Post(client, $"/api/admin/members/{memberId}/invitation", new { });
            var token = invitation.GetProperty("token").GetString();
            object Registration(string? invitationToken, string? address = null) => new { name = "Accepted User", email = address ?? $"{suffix}@invite.test", password = "Invitation123!", invitationToken };
            Assert.Equal(HttpStatusCode.BadRequest, (await AuthTests.PostAsync(invitee, "/api/auth/register", Registration(oldToken))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await AuthTests.PostAsync(invitee, "/api/auth/register", Registration(token, "wrong@invite.test"))).StatusCode);
            await using (var connection = await sp.GetRequiredService<IDbConnectionFactory>().OpenAsync())
                await connection.ExecuteAsync("UPDATE dbo.member_invitation SET expires_at=DATEADD(day,-1,SYSUTCDATETIME()) WHERE user_id=@Id", new { Id = invitedId });
            Assert.Equal(HttpStatusCode.BadRequest, (await AuthTests.PostAsync(invitee, "/api/auth/register", Registration(token))).StatusCode);
            invitation = await Post(client, $"/api/admin/members/{memberId}/invitation", new { });
            token = invitation.GetProperty("token").GetString();
            var accepted = await Post(invitee, "/api/auth/register", Registration(token));
            Assert.Equal(invitedId, accepted.GetProperty("user").GetProperty("id").GetString());
            var active = (await sp.GetRequiredService<IMemberRepository>().GetByIdAsync(memberId))!;
            Assert.Equal("active", active.Status);
            Assert.NotNull(active.LastActiveAt);
            Assert.Equal(roleId, (await sp.GetRequiredService<IUserRoleRepository>().GetForUserAsync(invitedId!))!.RoleId);
            var assignment = (await sp.GetRequiredService<IOrgAssignmentRepository>().GetForUserAsync(invitedId!))!;
            Assert.Equal(labId, assignment.LabId);
            Assert.Equal(sectionId, assignment.SectionId);
            Assert.Equal(HttpStatusCode.BadRequest, (await AuthTests.PostAsync(invitee, "/api/auth/register", Registration(token))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await AuthTests.PostAsync(client, $"/api/admin/members/{memberId}/invitation", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await AuthTests.PostAsync(client, $"/api/admin/members/{memberId}/status", new { status = "Invited" })).StatusCode);
            var usersRole = (await sp.GetRequiredService<IRoleRepository>().GetByNameAsync("Users"))!;
            await Post(client, $"/api/admin/members/{memberId}/role", new { roleId = usersRole.Id });
            await Post(client, $"/api/admin/users/{invitedId}/organization", new { labId, sectionId });
            Assert.Equal("Users", (await sp.GetRequiredService<IMemberRepository>().GetByIdAsync(memberId))!.Role);
            await Delete(client, $"/api/admin/members/{memberId}"); Assert.Null(await sp.GetRequiredService<IUserRepository>().GetByIdAsync(invitedId)); invitedId = null;
            await Delete(client, $"/api/admin/roles/{roleId}"); roleId = null;
            await Delete(client, $"/api/admin/labs/{labId}"); labId = null;
            await Delete(client, $"/api/admin/sections/{sectionId}"); sectionId = null;
            Assert.Contains(await sp.GetRequiredService<IRbacAuditRepository>().ListRecentAsync(100), a => a.ActorUserId == id && a.Action == "user.invited");
            Assert.Contains(await sp.GetRequiredService<IOrgAuditRepository>().ListRecentAsync(100), a => a.ActorEmail == email && a.Action == "user.organization.changed");
        }
        finally
        {
            if (invitedId is not null) await sp.GetRequiredService<IUserRepository>().DeleteAsync(invitedId);
            if (roleId is Guid role) await sp.GetRequiredService<IRoleRepository>().DeleteAsync(role);
            if (labId is Guid lab) await sp.GetRequiredService<ILabRepository>().DeleteAsync(lab);
            if (sectionId is Guid section) await sp.GetRequiredService<ISectionRepository>().DeleteAsync(section);
            await sp.GetRequiredService<IUserRepository>().DeleteAsync(id);
        }
    }
}
