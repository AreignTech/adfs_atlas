using System.Text.Json;
using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Security;
using Atlas.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

[Authorize, ApiController, Route("api")]
public sealed class WorkspaceController(WorkspaceService service, IProjectRepository projects, ITaskRepository tasks, IProjectMemberRepository memberships, AccessGuard guard, ISearchRepository search) : ControllerBase
{
    private SessionContextDto Actor => ((AuthSession)HttpContext.Items["AtlasSession"]!).Context;
    [HttpGet("workspace")]
    public Task<WorkspaceDto> Workspace(CancellationToken ct) => service.GetAsync(Actor, ct);
    [HttpGet("members/team-options")]
    public Task<IReadOnlyList<MemberDto>> TeamOptions(CancellationToken ct) => service.TeamOptionsAsync(Actor, ct);
    [HttpGet("projects/{id:guid}")]
    public async Task<ProjectDto> Project(Guid id, CancellationToken ct)
    {
        await guard.PermissionAsync(Actor, "tasks.view", ct);
        return WorkspaceService.ToDto(await service.ProjectAsync(Actor, id, ct), await memberships.ListForProjectAsync(id, ct));
    }
    [HttpGet("projects")]
    [ProducesResponseType<PageResult<Atlas.Domain.Entities.Project>>(200)]
    public async Task<object> Projects([FromQuery] SearchQuery query, CancellationToken ct) { await guard.PermissionAsync(Actor, "tasks.view", ct); return await search.ProjectsAsync(new(Actor.IsAdministrator, Actor.Scope.LabId, Actor.Scope.SectionId, Actor.User.Id), query, ct); }
    [HttpPost("projects")]
    public Task<ProjectDto> CreateProject(ProjectInput input, CancellationToken ct) => service.SaveProjectAsync(Actor, null, input, ct);
    [HttpPut("projects/{id:guid}")]
    public Task<ProjectDto> UpdateProject(Guid id, ProjectInput input, CancellationToken ct) => service.SaveProjectAsync(Actor, id, input, ct);
    [HttpPatch("projects/{id:guid}")]
    public async Task<ProjectDto> PatchProject(Guid id, JsonElement input, CancellationToken ct)
    {
        await guard.PermissionAsync(Actor, "tasks.edit", ct);
        var previous = await service.ProjectAsync(Actor, id, ct);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var merged = JsonSerializer.SerializeToNode(new ProjectInput { Name = previous.Name, Key = previous.Key, Description = previous.Description, Status = previous.Status, Color = previous.Color, StartDate = previous.StartDate.ToString("yyyy-MM-dd"), DueDate = previous.DueDate.ToString("yyyy-MM-dd"), MemberIds = (await memberships.ListForProjectAsync(id, ct)).Select(m => m.ToString()).ToList(), LabId = previous.LabId, SectionId = previous.SectionId }, options)!.AsObject();
        foreach (var property in input.EnumerateObject()) if (merged.ContainsKey(property.Name)) merged[property.Name] = System.Text.Json.Nodes.JsonNode.Parse(property.Value.GetRawText());
        return await service.SaveProjectAsync(Actor, id, merged.Deserialize<ProjectInput>(options)!, ct);
    }
    [HttpPost("projects/{id:guid}/status")]
    public async Task<ProjectDto> ProjectStatus(Guid id, StatusUpdateRequest input, CancellationToken ct)
    {
        await guard.PermissionAsync(Actor, "tasks.edit", ct); var project = await service.ProjectAsync(Actor, id, ct);
        if (!WorkspaceService.ProjectStatuses.Contains(input.Status)) throw new Atlas.Application.Common.ValidationException("Invalid project status.");
        await projects.SetStatusAsync(id, input.Status, ct); project.Status = input.Status; return WorkspaceService.ToDto(project, await memberships.ListForProjectAsync(id, ct));
    }
    [HttpDelete("projects/{id:guid}")]
    public async Task<Guid> DeleteProject(Guid id, CancellationToken ct) { await guard.PermissionAsync(Actor, "tasks.delete", ct); await service.ProjectAsync(Actor, id, ct); await projects.DeleteAsync(id, ct); return id; }
    [HttpPost("tasks")]
    public Task<TaskDto> CreateTask(TaskInput input, CancellationToken ct) => service.CreateTaskAsync(Actor, input, ct);
    [HttpPatch("tasks/{id:guid}")]
    public Task<TaskDto> UpdateTask(Guid id, JsonElement input, CancellationToken ct) => service.PatchTaskAsync(Actor, id, input, ct);
    [HttpPost("tasks/reorder")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Reorder(TaskReorderInput input, CancellationToken ct) { await service.ReorderAsync(Actor, input, ct); return NoContent(); }
    [HttpDelete("tasks/{id:guid}")]
    public async Task<Guid> DeleteTask(Guid id, CancellationToken ct) { await guard.PermissionAsync(Actor, "tasks.delete", ct); await service.TaskAsync(Actor, id, ct); await tasks.DeleteAsync(id, ct); return id; }
    [HttpPost("tasks/{id:guid}/duplicate")]
    public async Task<TaskDto> Duplicate(Guid id, CancellationToken ct)
    {
        var original = await service.TaskAsync(Actor, id, ct);
        return await service.CreateTaskAsync(Actor, new TaskInput { ProjectId = original.ProjectId, Title = original.Title.Length > 293 ? original.Title[..293] + " (copy)" : original.Title + " (copy)", Description = original.Description, Status = "todo", Priority = original.Priority, AssigneeId = original.AssigneeId?.ToString(), DueDate = original.DueDate.ToString("yyyy-MM-dd") }, ct);
    }
}
