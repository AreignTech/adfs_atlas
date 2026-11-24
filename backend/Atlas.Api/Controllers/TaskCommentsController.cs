using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

[Authorize, ApiController, Route("api/tasks/{taskId:guid}/comments")]
public sealed class TaskCommentsController(TaskCommentService service) : ControllerBase
{
    private SessionContextDto Actor => ((AuthSession)HttpContext.Items["AtlasSession"]!).Context;

    [HttpGet]
    public Task<TaskCommentPage> List(Guid taskId, CancellationToken ct, [FromQuery] int offset = 0, [FromQuery] int limit = 20) =>
        service.ListAsync(Actor, taskId, offset, limit, ct);

    [HttpGet("count")]
    public Task<TaskCommentCount> Count(Guid taskId, CancellationToken ct) => service.CountAsync(Actor, taskId, ct);

    [HttpPost]
    [ProducesResponseType<TaskCommentDto>(201)]
    public async Task<ActionResult<TaskCommentDto>> Create(Guid taskId, TaskCommentInput input, CancellationToken ct)
    {
        var comment = await service.CreateAsync(Actor, taskId, input, ct);
        return StatusCode(201, comment);
    }

    [HttpPut("{commentId:guid}")]
    public Task<TaskCommentDto> Update(Guid taskId, Guid commentId, TaskCommentInput input, CancellationToken ct) =>
        service.UpdateAsync(Actor, taskId, commentId, input, ct);

    [HttpDelete("{commentId:guid}")]
    public async Task<IActionResult> Delete(Guid taskId, Guid commentId, CancellationToken ct)
    {
        await service.DeleteAsync(Actor, taskId, commentId, ct);
        return NoContent();
    }
}
