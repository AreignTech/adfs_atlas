using System.Reflection;
using Atlas.Application.Interfaces;
using Dapper;

namespace Atlas.Infrastructure.Data.Migrations;

/// <summary>
/// Applies embedded, ordered SQL scripts exactly once and records each in
/// dbo.__AtlasMigrations. Each script runs inside its own transaction, so a
/// failure leaves the history table and schema consistent.
/// </summary>
public sealed class DatabaseMigrator
{
    private const string HistoryTable = "dbo.__AtlasMigrations";
    private readonly IDbConnectionFactory _connectionFactory;

    public DatabaseMigrator(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public static IReadOnlyList<string> Scripts()
    {
        var assembly = typeof(DatabaseMigrator).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Where(name => name.Contains(".Migrations.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ReadScript(string resourceName)
    {
        var assembly = typeof(DatabaseMigrator).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded migration '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public async Task<IReadOnlyList<string>> GetAppliedScriptsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await EnsureHistoryTableAsync(connection, cancellationToken);
        var rows = await connection.QueryAsync<string>(
            new CommandDefinition($"SELECT script_name FROM {HistoryTable}", cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await EnsureHistoryTableAsync(connection, cancellationToken);

        var applied = (await connection.QueryAsync<string>(
            new CommandDefinition($"SELECT script_name FROM {HistoryTable}", cancellationToken: cancellationToken)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var executed = new List<string>();

        foreach (var resourceName in Scripts())
        {
            var scriptName = resourceName[(resourceName.LastIndexOf(".Migrations.", StringComparison.OrdinalIgnoreCase) + ".Migrations.".Length)..];
            if (applied.Contains(scriptName))
            {
                continue;
            }

            var sql = ReadScript(resourceName);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    transaction: transaction,
                    commandTimeout: 180,
                    cancellationToken: cancellationToken));

                await connection.ExecuteAsync(new CommandDefinition(
                    $"INSERT INTO {HistoryTable} (script_name) VALUES (@ScriptName)",
                    new { ScriptName = scriptName },
                    transaction,
                    cancellationToken: cancellationToken));

                await transaction.CommitAsync(cancellationToken);
                executed.Add(scriptName);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return executed;
    }

    private static async Task EnsureHistoryTableAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID('dbo.__AtlasMigrations', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.__AtlasMigrations (
                    script_name NVARCHAR(255) NOT NULL CONSTRAINT PK___AtlasMigrations PRIMARY KEY,
                    applied_at  DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF___AtlasMigrations_applied_at DEFAULT (SYSUTCDATETIME())
                );
            END;
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }
}
