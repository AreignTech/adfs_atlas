using Atlas.Application.Interfaces;
using Atlas.Application.Interfaces.Repositories;
using Atlas.Domain.Entities;
using Dapper;

namespace Atlas.Infrastructure.Repositories;

public sealed class SearchRepository(IDbConnectionFactory factory) : ISearchRepository
{
    public Task<PageResult<Project>> ProjectsAsync(OrgScopeFilter scope, SearchQuery query, CancellationToken cancellationToken = default) => QueryAsync<Project>("project", "name", scope, query, cancellationToken);
    public Task<PageResult<TaskItem>> TasksAsync(OrgScopeFilter scope, SearchQuery query, CancellationToken cancellationToken = default) => QueryAsync<TaskItem>("task", "title", scope, query, cancellationToken);

    private async Task<PageResult<T>> QueryAsync<T>(string table, string textColumn, OrgScopeFilter scope, SearchQuery query, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 200 || query.Page > 1000000) throw new ArgumentException("Invalid pagination.");
        var sort = query.Sort switch { "due_date" => "due_date", "created_at" => "created_at", "status" => "status", "name" when table == "project" => "name", "title" when table == "task" => "title", _ => throw new ArgumentException("Invalid sort field.") };
        var direction = query.Descending ? "DESC" : "ASC";
        var where = $"(@Admin=1 OR (lab_id=@LabId AND section_id=@SectionId)) AND (@Status IS NULL OR status=@Status) AND (@Search IS NULL OR [{textColumn}] LIKE @Search ESCAPE '~')";
        var search = query.Search?.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");
        var parameters = new { Admin = scope.IsAdministrator, scope.LabId, scope.SectionId, query.Status, Search = search is null ? null : $"%{search}%", Offset = (query.Page - 1) * query.PageSize, Size = query.PageSize };
        await using var connection = await factory.OpenAsync(ct);
        using var result = await connection.QueryMultipleAsync(new CommandDefinition($"SELECT COUNT(*) FROM dbo.[{table}] WHERE {where}; SELECT * FROM dbo.[{table}] WHERE {where} ORDER BY [{sort}] {direction}, id OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY;", parameters, cancellationToken: ct));
        var total = await result.ReadSingleAsync<int>();
        return new((await result.ReadAsync<T>()).AsList(), total, query.Page, query.PageSize);
    }
}
