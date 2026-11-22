using Atlas.Domain.Entities;

namespace Atlas.Application.Interfaces.Repositories;

public sealed record SearchQuery(int Page = 1, int PageSize = 25, string? Search = null, string? Status = null, string Sort = "due_date", bool Descending = false);
public sealed record PageResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public interface ISearchRepository
{
    Task<PageResult<Project>> ProjectsAsync(OrgScopeFilter scope, SearchQuery query, CancellationToken cancellationToken = default);
    Task<PageResult<TaskItem>> TasksAsync(OrgScopeFilter scope, SearchQuery query, CancellationToken cancellationToken = default);
}
