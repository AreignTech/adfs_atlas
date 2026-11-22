using System.Data.Common;
using Atlas.Application.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Atlas.Infrastructure.Data;

/// <summary>
/// Opens Microsoft.Data.SqlClient connections from the configured
/// <c>ConnectionStrings:Atlas</c> value. Credentials are never hardcoded: the
/// development default uses Windows integrated authentication and any
/// production deployment supplies the value through environment variables or
/// user secrets.
/// </summary>
public sealed class SqlServerConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    static SqlServerConnectionFactory()
    {
        // The SQL Server schema keeps the original snake_case column names so
        // the PostgreSQL data migration is a straight copy. Enabling Dapper's
        // underscore mapping lets entities use idiomatic PascalCase properties.
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public SqlServerConnectionFactory(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("Atlas")
            ?? configuration["ATLAS_DB_CONNECTION"]
            ?? throw new InvalidOperationException(
                "Connection string 'Atlas' is not configured. Set ConnectionStrings__Atlas or ATLAS_DB_CONNECTION.");

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("Connection string 'Atlas' is empty.");
        }
    }

    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
