using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atlas.Application.Common;
using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Security;
using Atlas.Domain.Entities;

namespace Atlas.Application.Services;

public sealed class WorkspaceService(IProjectRepository projects, ITaskRepository tasks, IMemberRepository members, IProjectMemberRepository memberships, IWorkspaceWriter writer, AccessGuard guard)
{
    public static readonly string[] TaskStatuses = ["todo", "in-progress", "in-review", "completed"];
    public static readonly string[] ProjectStatuses = ["planning", "active", "on-hold", "completed"];
    public static void Text(string? value, int min, int max, string field) { if (value is null || value.Length < min || value.Length > max) throw new ValidationException($"Invalid {field}."); }
    public static DateTime Date(string value) => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new ValidationException("Expected a valid YYYY-MM-DD date.");
    private static string Iso(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static TaskDto ToDto(TaskItem t) => new() { Id = t.Id.ToString(), Reference = t.Reference, Title = t.Title, ProjectId = t.ProjectId.ToString(), Status = t.Status, Priority = t.Priority, AssigneeId = t.AssigneeId?.ToString() ?? "", DueDate = Iso(t.DueDate), Position = t.Position, LabId = t.LabId, SectionId = t.SectionId };
    public static ProjectDto ToDto(Project p, IEnumerable<Guid> memberIds) => new() { Id = p.Id.ToString(), Name = p.Name, Key = p.Key, Description = p.Description, Status = p.Status, Color = p.Color, StartDate = Iso(p.StartDate), DueDate = Iso(p.DueDate), LabId = p.LabId, SectionId = p.SectionId, MemberIds = memberIds.Select(id => id.ToString()).ToList() };
    public static MemberDto ToDto(MemberWithUser m) => new() { Id = m.Id.ToString(), UserId = m.UserId, Name = m.Name, Email = m.Email, Initials = string.Concat(m.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0]))), Role = m.Role, Status = m.Status == "active" ? "Active" : m.Status == "invited" ? "Invited" : m.Status == "inactive" ? "Inactive" : "Away", JoinedDate = m.JoinedAt.ToString("yyyy-MM-dd"), LastActive = m.LastActiveAt?.ToString("yyyy-MM-dd") ?? "Never signed in", LabId = m.LabId, SectionId = m.SectionId, LabCode = m.LabCode, LabName = m.LabName, SectionCode = m.SectionCode, SectionName = m.SectionName };
    public async Task<WorkspaceDto> GetAsync(SessionContextDto actor, CancellationToken ct)
    {
        using var span=Atlas.Application.Observability.ApplicationTracing.Source.StartActivity("Workspace.Get");
        var filter = new OrgScopeFilter(actor.IsAdministrator, actor.Scope.LabId, actor.Scope.SectionId);
        var visibleProjects = actor.Permissions.Contains("tasks.view") ? await projects.ListAsync(filter, ct) : [];
        var visibleTasks = actor.Permissions.Contains("tasks.view") ? await tasks.ListAsync(filter, ct) : [];
        var allMemberships = await memberships.ListAllAsync(ct);
        var visibleMembers = (await members.ListWithUserAsync(ct)).Where(m => m.UserId == actor.User.Id || actor.IsAdministrator || (actor.Scope.IsAssigned && m.LabId == actor.Scope.LabId && m.SectionId == actor.Scope.SectionId));
        return new WorkspaceDto { Projects = visibleProjects.Select(p => ToDto(p, allMemberships.Where(m => m.ProjectId == p.Id).Select(m => m.MemberId))).ToList(), Tasks = visibleTasks.Select(ToDto).ToList(), Members = visibleMembers.Select(ToDto).ToList() };
    }
    public async Task<Project> ProjectAsync(SessionContextDto actor, Guid id, CancellationToken ct)
    {
        var project = await projects.GetByIdAsync(id, ct) ?? throw new NotFoundException("Project was not found.");
        await guard.ScopeAsync(actor, project.LabId, project.SectionId, ct);
        return project;
    }
    public async Task<TaskItem> TaskAsync(SessionContextDto actor, Guid id, CancellationToken ct)
    {
        var task = await tasks.GetByIdAsync(id, ct) ?? throw new NotFoundException("Task was not found.");
        await guard.ScopeAsync(actor, task.LabId, task.SectionId, ct);
        await ProjectAsync(actor, task.ProjectId, ct);
        return task;
    }
    public async Task<ProjectDto> SaveProjectAsync(SessionContextDto actor, Guid? id, ProjectInput input, CancellationToken ct)
    {
        using var span=Atlas.Application.Observability.ApplicationTracing.Source.StartActivity("Workspace.SaveProject");
        await guard.PermissionAsync(actor, id is null ? "tasks.create" : "tasks.edit", ct);
        var previous = id is null ? null : await ProjectAsync(actor, id.Value, ct);
        Text(input.Name, 2, 120, "project name");
        if (!Regex.IsMatch(input.Key, "^[A-Za-z]{2,4}$") || !ProjectStatuses.Contains(input.Status)) throw new ValidationException("Invalid project key or status.");
        Text(input.Description ?? "", 0, 2000, "description"); Text(input.Color, 1, 64, "color");
        if (previous is null || input.LabId != previous.LabId || input.SectionId != previous.SectionId) await guard.ActiveOrgAsync(input.LabId, input.SectionId, ct);
        await guard.ScopeAsync(actor, input.LabId, input.SectionId, ct);
        var ids = input.MemberIds.Select(Guid.Parse).Distinct().ToList();
        var available = await members.ListWithUserAsync(ct);
        foreach (var memberId in ids)
        {
            var candidate = available.SingleOrDefault(m => m.Id == memberId) ?? throw new ValidationException("Member was not found.");
            if (!actor.IsAdministrator && (candidate.LabId != input.LabId || candidate.SectionId != input.SectionId)) throw new ForbiddenException("Project members must belong to its Lab and Section.");
        }
        var owner = ids.FirstOrDefault();
        if (owner == Guid.Empty) owner = previous?.OwnerId ?? available.FirstOrDefault(m => m.UserId == actor.User.Id)?.Id ?? throw new ValidationException("A project owner is required.");
        var project = new Project { Id = id ?? Guid.Empty, Name = input.Name, Key = input.Key.ToUpperInvariant(), Description = input.Description ?? "", Status = input.Status, Color = input.Color, OwnerId = owner, StartDate = input.StartDate is null ? previous?.StartDate ?? DateTime.UtcNow.Date : Date(input.StartDate), DueDate = Date(input.DueDate), LabId = input.LabId, SectionId = input.SectionId };
        await writer.SaveProjectAsync(project, ids, id is null, ct);
        return ToDto(project, ids);
    }
    public async Task<TaskDto> CreateTaskAsync(SessionContextDto actor, TaskInput input, CancellationToken ct)
    {
        await guard.PermissionAsync(actor, "tasks.create", ct);
        var project = await ProjectAsync(actor, input.ProjectId ?? Guid.Empty, ct);
        var task = new TaskItem { ProjectId = project.Id, Title = input.Title, Description = input.Description ?? "", Status = input.Status, Priority = input.Priority, AssigneeId = string.IsNullOrEmpty(input.AssigneeId) ? null : Guid.Parse(input.AssigneeId), DueDate = Date(input.DueDate), LabId = project.LabId, SectionId = project.SectionId, CompletedAt = input.Status == "completed" ? DateTimeOffset.UtcNow : null };
        await ValidateTaskAsync(actor, task, ct); await tasks.CreateAsync(task, ct); return ToDto(task);
    }
    public async Task<TaskDto> PatchTaskAsync(SessionContextDto actor, Guid id, JsonElement patch, CancellationToken ct)
    {
        await guard.PermissionAsync(actor, "tasks.edit", ct);
        if (patch.ValueKind != JsonValueKind.Object) throw new ValidationException("Expected a task update object.");
        foreach (var property in patch.EnumerateObject())
            if (new[] { "projectId", "title", "description", "status", "priority", "dueDate", "assigneeId" }.Contains(property.Name) && property.Value.ValueKind != JsonValueKind.String && !(property.Name == "assigneeId" && property.Value.ValueKind == JsonValueKind.Null)) throw new ValidationException("Invalid task update value.");
        var task = await TaskAsync(actor, id, ct);
        if (patch.TryGetProperty("projectId", out var projectId) && projectId.GetGuid() != task.ProjectId) throw new ValidationException("A task cannot be moved to another project through this operation.");
        if (patch.TryGetProperty("title", out var title)) task.Title = title.GetString()!;
        if (patch.TryGetProperty("description", out var description)) task.Description = description.GetString() ?? "";
        if (patch.TryGetProperty("status", out var status)) { task.Status = status.GetString()!; task.CompletedAt = task.Status == "completed" ? task.CompletedAt ?? DateTimeOffset.UtcNow : null; }
        if (patch.TryGetProperty("priority", out var priority)) task.Priority = priority.GetString()!;
        if (patch.TryGetProperty("dueDate", out var dueDate)) task.DueDate = Date(dueDate.GetString()!);
        if (patch.TryGetProperty("assigneeId", out var assignee)) { await guard.PermissionAsync(actor, "tasks.assign", ct); task.AssigneeId = assignee.ValueKind == JsonValueKind.Null || string.IsNullOrEmpty(assignee.GetString()) ? null : assignee.GetGuid(); }
        await ValidateTaskAsync(actor, task, ct, false); await tasks.UpdateAsync(task, ct); return ToDto(task);
    }
    private async Task ValidateTaskAsync(SessionContextDto actor, TaskItem task, CancellationToken ct, bool assigning = true)
    {
        Text(task.Title, 1, 300, "task title"); Text(task.Description, 0, 5000, "task description");
        if (!TaskStatuses.Contains(task.Status) || !new[] { "urgent", "high", "medium", "low" }.Contains(task.Priority)) throw new ValidationException("Invalid task status or priority.");
        if (task.AssigneeId is Guid assignee)
        {
            if (assigning) await guard.PermissionAsync(actor, "tasks.assign", ct);
            var member = (await members.ListWithUserAsync(ct)).SingleOrDefault(m => m.Id == assignee) ?? throw new ValidationException("Assignee was not found.");
            if (!actor.IsAdministrator && (member.LabId != task.LabId || member.SectionId != task.SectionId)) throw new ForbiddenException("Assignee must belong to the task's Lab and Section.");
        }
    }
    public async Task ReorderAsync(SessionContextDto actor, TaskReorderInput input, CancellationToken ct)
    {
        await guard.PermissionAsync(actor, "tasks.edit", ct); await ProjectAsync(actor, input.ProjectId, ct);
        if (input.Items.Count > 500 || input.Items.Select(i => i.TaskId).Distinct().Count() != input.Items.Count) throw new ValidationException("Invalid task ordering.");
        foreach (var item in input.Items)
        {
            if (!TaskStatuses.Contains(item.Status) || item.Position < 0 || !Guid.TryParse(item.TaskId, out var taskId)) throw new ValidationException("Invalid task ordering.");
            if ((await TaskAsync(actor, taskId, ct)).ProjectId != input.ProjectId) throw new ForbiddenException("Task belongs to a different project.");
        }
        await tasks.ReorderAsync(input.ProjectId, input.Items, ct);
    }
}
