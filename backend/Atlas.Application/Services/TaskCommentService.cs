using Atlas.Application.Common;
using Atlas.Application.DTOs;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Security;
using Atlas.Domain.Entities;

namespace Atlas.Application.Services;

public sealed class TaskCommentService(ITaskCommentRepository comments, WorkspaceService workspace, AccessGuard guard)
{
    private static bool CanManage(SessionContextDto actor, TaskComment comment) =>
        actor.Permissions.Contains(AtlasPermissions.TasksEdit) &&
        (comment.UserId == actor.User.Id || actor.IsAdministrator);

    private static TaskCommentDto ToDto(SessionContextDto actor, TaskComment c) => new(
        c.Id, c.TaskId, c.UserId, c.AuthorName, c.AuthorImage, c.Content, c.CreatedAt, c.UpdatedAt,
        CanManage(actor, c), CanManage(actor, c));

    private async Task AccessAsync(SessionContextDto actor, Guid taskId, bool write, CancellationToken ct)
    {
        await guard.PermissionAsync(actor, AtlasPermissions.TasksView, ct);
        if (write) await guard.PermissionAsync(actor, AtlasPermissions.TasksEdit, ct);
        await workspace.TaskAsync(actor, taskId, ct);
    }

    private static string Content(TaskCommentInput input)
    {
        var content = input.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content) || content.Length > 5000)
            throw new ValidationException("Comment must contain between 1 and 5000 characters.");
        return content;
    }

    public async Task<TaskCommentPage> ListAsync(SessionContextDto actor, Guid taskId, int offset, int limit, CancellationToken ct)
    {
        await AccessAsync(actor, taskId, false, ct);
        if (offset < 0 || limit < 1 || limit > 100) throw new ValidationException("Invalid comment pagination.");
        var items = await comments.ListAsync(taskId, offset, limit, ct);
        return new(items.Select(c => ToDto(actor, c)).ToList(), await comments.CountAsync(taskId, ct), offset, limit,
            actor.Permissions.Contains(AtlasPermissions.TasksEdit));
    }

    public async Task<TaskCommentCount> CountAsync(SessionContextDto actor, Guid taskId, CancellationToken ct)
    {
        await AccessAsync(actor, taskId, false, ct);
        return new(await comments.CountAsync(taskId, ct));
    }

    public async Task<TaskCommentDto> CreateAsync(SessionContextDto actor, Guid taskId, TaskCommentInput input, CancellationToken ct)
    {
        await AccessAsync(actor, taskId, true, ct);
        var id = await comments.CreateAsync(taskId, actor.User.Id, Content(input), ct);
        return ToDto(actor, (await comments.GetAsync(taskId, id, ct))!);
    }

    public async Task<TaskCommentDto> UpdateAsync(SessionContextDto actor, Guid taskId, Guid id, TaskCommentInput input, CancellationToken ct)
    {
        await AccessAsync(actor, taskId, true, ct);
        var comment = await comments.GetAsync(taskId, id, ct) ?? throw new NotFoundException("Comment was not found.");
        if (!CanManage(actor, comment)) throw new ForbiddenException("You cannot edit another user's comment.");
        if (!await comments.UpdateAsync(taskId, id, Content(input), ct)) throw new NotFoundException("Comment was not found.");
        return ToDto(actor, await comments.GetAsync(taskId, id, ct) ?? throw new NotFoundException("Comment was not found."));
    }

    public async Task DeleteAsync(SessionContextDto actor, Guid taskId, Guid id, CancellationToken ct)
    {
        await AccessAsync(actor, taskId, true, ct);
        var comment = await comments.GetAsync(taskId, id, ct) ?? throw new NotFoundException("Comment was not found.");
        if (!CanManage(actor, comment)) throw new ForbiddenException("You cannot delete another user's comment.");
        if (!await comments.DeleteAsync(taskId, id, ct)) throw new NotFoundException("Comment was not found.");
    }
}
