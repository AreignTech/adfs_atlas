using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.Application.Common;
using Atlas.Application.DTOs;
using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Application.Services;
using Atlas.Domain.Entities;
using Atlas.Infrastructure.Data.Migrations;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Atlas.Tests;

public sealed class TaskCommentTests : IAsyncLifetime
{
    private readonly AtlasFactory factory = new();
    private readonly List<string> users = [];
    private readonly List<Guid> projects = [];
    private Guid labId, sectionId, taskId, secondTaskId;
    private string authorId = "";
    private HttpClient author = null!, peer = null!, outsider = null!, admin = null!;
    private string Path => $"/api/tasks/{taskId}/comments";

    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<DatabaseMigrator>().MigrateAsync();
        var lab = new LabCode { Code = Guid.NewGuid().ToString("N"), Name = "Comment lab" };
        var section = new SectionCode { Code = Guid.NewGuid().ToString("N"), Name = "Comment section" };
        await sp.GetRequiredService<ILabRepository>().CreateAsync(lab);
        await sp.GetRequiredService<ISectionRepository>().CreateAsync(section);
        labId = lab.Id; sectionId = section.Id;
        (author, authorId, var memberId) = await UserAsync("Users", true);
        (peer, _, var peerMemberId) = await UserAsync("Users", true);
        (outsider, _, _) = await UserAsync("Users", false);
        (admin, _, _) = await UserAsync("Administrators", false);
        var project = new Project { Name = "Comment project", Key = Guid.NewGuid().ToString("N")[..8], OwnerId = memberId, LabId = labId, SectionId = sectionId, StartDate = DateTime.Today, DueDate = DateTime.Today.AddDays(1) };
        await sp.GetRequiredService<IProjectRepository>().CreateAsync(project);
        projects.Add(project.Id);
        await sp.GetRequiredService<IProjectMemberRepository>().ReplaceForProjectAsync(project.Id, [memberId, peerMemberId]);
        foreach (var title in new[] { "Comment task", "Other task" })
        {
            var task = new TaskItem { ProjectId = project.Id, Title = title, DueDate = DateTime.Today.AddDays(1), LabId = labId, SectionId = sectionId };
            await sp.GetRequiredService<ITaskRepository>().CreateAsync(task);
            if (taskId == Guid.Empty) taskId = task.Id; else secondTaskId = task.Id;
        }
    }

    private async Task<(HttpClient, string, Guid)> UserAsync(string role, bool assigned)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var id = Guid.NewGuid().ToString();
        var user = new User { Id = id, Name = "Comment Author", Email = $"{id}@comments.test", Image = "/avatar.png" };
        var member = new Member { UserId = id, Role = role, Status = "active" };
        await sp.GetRequiredService<IIdentityWriter>().CreateAsync(user, member,
            new Account { Id = Guid.NewGuid().ToString(), AccountId = id, UserId = id, ProviderId = "credential", Password = sp.GetRequiredService<IPasswordHasher>().Hash("CommentsTest123!") }, role);
        users.Add(id);
        if (assigned) await sp.GetRequiredService<IOrgAssignmentRepository>().UpsertAsync(new UserOrgAssignment { UserId = id, LabId = labId, SectionId = sectionId });
        var client = factory.CreateClient();
        (await AuthTests.PostAsync(client, "/api/auth/login", new { user.Email, password = "CommentsTest123!" })).EnsureSuccessStatusCode();
        return (client, id, member.Id);
    }

    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        foreach (var id in projects) await sp.GetRequiredService<IProjectRepository>().DeleteAsync(id);
        foreach (var id in users) await sp.GetRequiredService<IUserRepository>().DeleteAsync(id);
        await sp.GetRequiredService<ILabRepository>().DeleteAsync(labId);
        await sp.GetRequiredService<ISectionRepository>().DeleteAsync(sectionId);
        await factory.DisposeAsync();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-ATLAS-CSRF", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    private async Task<TaskCommentDto> AddAsync(string content = "A comment")
    {
        var response = await AuthTests.PostAsync(author, Path, new { content, userId = "forged-user", taskId = secondTaskId, createdAt = "2000-01-01" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskCommentDto>())!;
    }

    [Fact]
    public async Task CommentsPersistWithAuthenticatedAuthorAndCorrectTaskAndCounts()
    {
        Assert.Empty((await author.GetFromJsonAsync<TaskCommentPage>(Path))!.Items);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var first = await AddAsync("  First comment  ");
        var second = await AddAsync("Second comment");
        Assert.Equal(authorId, first.UserId);
        Assert.Equal(taskId, first.TaskId);
        Assert.Equal("First comment", first.Content);
        Assert.Equal("Comment Author", first.AuthorName);
        Assert.Equal("/avatar.png", first.AuthorImage);
        Assert.InRange(first.CreatedAt, before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Null(first.UpdatedAt);
        var page = (await author.GetFromJsonAsync<TaskCommentPage>(Path))!;
        Assert.Equal(new[] { second.Id, first.Id }, page.Items.Select(c => c.Id));
        Assert.Equal(2, page.Total);
        Assert.Equal(2, (await author.GetFromJsonAsync<TaskCommentCount>(Path + "/count"))!.Count);
        Assert.Equal(0, (await author.GetFromJsonAsync<TaskCommentCount>($"/api/tasks/{secondTaskId}/comments/count"))!.Count);
        using var scope = factory.Services.CreateScope();
        await using var connection = await scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        Assert.Equal("First comment", await connection.QuerySingleAsync<string>("SELECT content FROM dbo.task_comment WHERE id=@Id", new { first.Id }));
        Assert.Equal(2, (await scope.ServiceProvider.GetRequiredService<ITaskRepository>().GetByIdAsync(taskId))!.CommentCount);
        var search = await scope.ServiceProvider.GetRequiredService<ISearchRepository>().TasksAsync(new(false, labId, sectionId, authorId), new());
        Assert.Equal(2, search.Items.Single(t => t.Id == taskId).CommentCount);
        var workspace = (await author.GetFromJsonAsync<WorkspaceDto>("/api/workspace"))!;
        Assert.Equal(2, workspace.Tasks.Single(t => t.Id == taskId.ToString()).CommentCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    [InlineData("\u00a0\u2003")]
    [InlineData(null)]
    public async Task RejectsEmptyComments(string? content)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await AuthTests.PostAsync(author, Path, new { content })).StatusCode);
        Assert.Equal(0, (await author.GetFromJsonAsync<TaskCommentCount>(Path + "/count"))!.Count);
    }

    [Fact]
    public async Task OwnerCanEditAndDeleteWithModificationTimestamp()
    {
        var comment = await AddAsync();
        var response = await SendAsync(author, HttpMethod.Put, $"{Path}/{comment.Id}", new { content = " Updated " });
        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<TaskCommentDto>())!;
        Assert.Equal("Updated", updated.Content);
        Assert.Equal(comment.CreatedAt, updated.CreatedAt);
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(author, HttpMethod.Put, $"{Path}/{comment.Id}", new { content = "\t " })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(author, HttpMethod.Delete, $"{Path}/{comment.Id}")).StatusCode);
        Assert.Equal(0, (await author.GetFromJsonAsync<TaskCommentCount>(Path + "/count"))!.Count);
        using var scope = factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ITaskCommentRepository>().GetAsync(taskId, comment.Id, default));
    }

    [Fact]
    public async Task PeerCannotModifyAnotherAuthorsCommentButAdministratorCan()
    {
        var comment = await AddAsync();
        var visible = (await peer.GetFromJsonAsync<TaskCommentPage>(Path))!.Items.Single();
        Assert.False(visible.CanEdit); Assert.False(visible.CanDelete);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(peer, HttpMethod.Put, $"{Path}/{comment.Id}", new { content = "Attack" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(peer, HttpMethod.Delete, $"{Path}/{comment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(admin, HttpMethod.Put, $"{Path}/{comment.Id}", new { content = "Moderated" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(admin, HttpMethod.Delete, $"{Path}/{comment.Id}")).StatusCode);
    }

    [Fact]
    public async Task InaccessibleTasksAreProtectedForEveryCommentOperation()
    {
        var comment = await AddAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(Path + "/count")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AuthTests.PostAsync(outsider, Path, new { content = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(outsider, HttpMethod.Put, $"{Path}/{comment.Id}", new { content = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(outsider, HttpMethod.Delete, $"{Path}/{comment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsJsonAsync(Path, new { content = "Missing CSRF" })).StatusCode);
    }

    [Fact]
    public async Task CannotAddressACommentThroughAnotherTaskAndMissingTasksReturn404()
    {
        var comment = await AddAsync();
        var wrong = $"/api/tasks/{secondTaskId}/comments/{comment.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(author, HttpMethod.Put, wrong, new { content = "Wrong task" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(author, HttpMethod.Delete, wrong)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync($"/api/tasks/{Guid.NewGuid()}/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AuthTests.PostAsync(author, $"/api/tasks/{Guid.NewGuid()}/comments", new { content = "No task" })).StatusCode);
    }

    [Fact]
    public async Task PaginationAndMaximumContentLengthAreValidated()
    {
        var first = await AddAsync();
        var second = await AddAsync("Newer");
        var page = (await author.GetFromJsonAsync<TaskCommentPage>(Path + "?offset=1&limit=1"))!;
        Assert.Equal(first.Id, Assert.Single(page.Items).Id); Assert.Equal(2, page.Total);
        foreach (var query in new[] { "offset=-1", "limit=0", "limit=101" })
            Assert.Equal(HttpStatusCode.BadRequest, (await author.GetAsync(Path + "?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AuthTests.PostAsync(author, Path, new { content = new string('a', 5001) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(author, HttpMethod.Put, $"{Path}/{second.Id}", new { content = new string('a', 5001) })).StatusCode);
        await AddAsync(new string('a', 5000));
    }

    [Fact]
    public async Task ViewAndEditPermissionsAreEnforcedIndependentlyOfOwnership()
    {
        var comment = await AddAsync();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TaskCommentService>();
        var readOnly = new SessionContextDto(new(authorId, "Author", "author@comments.test", false, null),
            ["Custom"], ["tasks.view"], false, new(labId, sectionId, true, false));
        var page = await service.ListAsync(readOnly, taskId, 0, 20, default);
        Assert.False(page.CanComment); Assert.False(page.Items.Single().CanEdit); Assert.False(page.Items.Single().CanDelete);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(readOnly, taskId, new("Denied"), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateAsync(readOnly, taskId, comment.Id, new("Denied"), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteAsync(readOnly, taskId, comment.Id, default));
        var noView = readOnly with { Permissions = ["tasks.edit"] };
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync(noView, taskId, 0, 20, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CountAsync(noView, taskId, default));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(noView, taskId, new("Denied"), default));
        foreach (var mismatch in new[] { readOnly with { Scope = new(Guid.NewGuid(), sectionId, true, false) }, readOnly with { Scope = new(labId, Guid.NewGuid(), true, false) } })
            Assert.Single((await service.ListAsync(mismatch, taskId, 0, 20, default)).Items);
    }

    [Fact]
    public async Task TaskAndUserDeletionCascadeComments()
    {
        var comment = await AddAsync();
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var peerComment = (await (await AuthTests.PostAsync(peer, Path, new { content = "Peer" })).Content.ReadFromJsonAsync<TaskCommentDto>())!;
        await sp.GetRequiredService<IUserRepository>().DeleteAsync(peerComment.UserId);
        Assert.Equal(1, (await author.GetFromJsonAsync<TaskCommentCount>(Path + "/count"))!.Count);
        await sp.GetRequiredService<ITaskRepository>().DeleteAsync(taskId);
        Assert.Null(await sp.GetRequiredService<ITaskCommentRepository>().GetAsync(taskId, comment.Id, default));
    }
}
