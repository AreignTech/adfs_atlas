namespace Atlas.Application.Interfaces;

/// <summary>
/// Creates open SQL Server connections. Every repository receives this factory
/// instead of a connection string, so connection lifetime stays explicit.
/// </summary>
public interface IDbConnectionFactory
{
    Task<System.Data.Common.DbConnection> OpenAsync(CancellationToken cancellationToken = default);
}
