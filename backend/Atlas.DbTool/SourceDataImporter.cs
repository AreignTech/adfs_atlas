using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atlas.Application.Interfaces;
using Dapper;

internal static class SourceDataImporter
{
    private static readonly string[] Tables = ["lab_code", "section_code", "user", "member", "account", "verification", "session", "project", "task", "project_member", "user_org_assignment", "app_role", "app_permission", "app_role_permission", "app_user_role", "ldap_config", "ldap_identity", "ldap_group_mapping", "org_audit", "rbac_audit", "ldap_audit"];
    private sealed record Column(string Name, string Type, short MaxLength, bool Nullable);

    public static async Task ImportAsync(IDbConnectionFactory factory, bool verifyOnly = false, bool repairTimestampPrecision = false)
    {
        using var input = await JsonDocument.ParseAsync(Console.OpenStandardInput());
        var root = input.RootElement;
        if (root.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(Tables.Order()) is false)
            throw new InvalidOperationException("Source must contain exactly the 21 expected tables.");
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        if (!verifyOnly)
        {
            foreach (var table in Tables.Except(["app_role", "app_permission", "app_role_permission"]))
                if (await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{table}]", transaction: transaction) != 0)
                    throw new InvalidOperationException("Data import requires a fresh migrated destination; existing application data is never overwritten.");
            await connection.ExecuteAsync("DELETE FROM dbo.app_role_permission; DELETE FROM dbo.app_permission; DELETE FROM dbo.app_role;", transaction: transaction);
        }
        foreach (var table in Tables)
        {
            var columns = (await connection.QueryAsync<Column>("""
                SELECT c.name AS Name, t.name AS Type, c.max_length AS MaxLength, c.is_nullable AS Nullable
                FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
                WHERE c.object_id=OBJECT_ID(@Table) AND c.is_computed=0
                ORDER BY c.column_id
                """, new { Table = $"dbo.{table}" }, transaction)).AsList();
            var rows = root.GetProperty(table);
            var primaryKeys = (await connection.QueryAsync<string>("SELECT c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(@Table) AND i.is_primary_key=1 ORDER BY ic.key_ordinal", new { Table = $"dbo.{table}" }, transaction)).AsList();
            if (repairTimestampPrecision && primaryKeys.Count == 0) throw new InvalidOperationException($"Missing primary key for timestamp precision repair: {table}.");
            foreach (var row in rows.EnumerateArray())
            {
                if (!row.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(columns.Select(c => c.Name).Order()))
                    throw new InvalidOperationException($"Source/target column mismatch in {table}.");
                var parameters = new DynamicParameters();
                for (var i = 0; i < columns.Count; i++)
                {
                    var column = columns[i];
                    if (!Regex.IsMatch(column.Name, "^[a-z_]+$")) throw new InvalidOperationException("Unexpected column identifier.");
                    var value = row.GetProperty(column.Name);
                    object? converted = value.ValueKind == JsonValueKind.Null ? null : column.Type switch
                    {
                        "uniqueidentifier" => Guid.Parse(value.GetString()!),
                        "bit" => value.GetBoolean(),
                        "int" => value.GetInt32(),
                        "bigint" => value.GetInt64(),
                        "date" => DateTime.Parse(value.GetString()!, CultureInfo.InvariantCulture).Date,
                        "datetimeoffset" => DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture),
                        "nvarchar" => value.GetString(),
                        _ => throw new InvalidOperationException($"Unsupported target type {column.Type}.")
                    };
                    if (converted is null && !column.Nullable) throw new InvalidOperationException($"Required source value missing: {table}.{column.Name}.");
                    if (converted is string text && column.MaxLength > 0 && text.Length * 2 > column.MaxLength)
                        throw new InvalidOperationException($"Source text exceeds destination width: {table}.{column.Name}. No data was committed.");
                    // Better Auth sessions are deliberately revoked during cutover.
                    if (table == "session" && column.Name == "expires_at" && converted is DateTimeOffset expiry)
                        converted = expiry < DateTimeOffset.UtcNow ? expiry : DateTimeOffset.UtcNow;
                    parameters.Add($"p{i}", converted);
                }
                var names = string.Join(",", columns.Select(c => $"[{c.Name}]"));
                var values = string.Join(",", columns.Select((_, i) => $"@p{i}"));
                if (!verifyOnly) await connection.ExecuteAsync($"INSERT INTO dbo.[{table}] ({names}) VALUES ({values})", parameters, transaction);
                else
                {
                    if (repairTimestampPrecision)
                    {
                        var keys = primaryKeys.Select(key => $"[{key}]=@p{columns.FindIndex(c => c.Name == key)}");
                        for (var i = 0; i < columns.Count; i++)
                        {
                            if (columns[i].Type != "datetimeoffset" || (table == "session" && columns[i].Name == "expires_at") || row.GetProperty(columns[i].Name).ValueKind == JsonValueKind.Null) continue;
                            var precise = DateTimeOffset.Parse(row.GetProperty(columns[i].Name).GetString()!, CultureInfo.InvariantCulture);
                            var truncated = new DateTimeOffset(precise.Ticks - precise.Ticks % TimeSpan.TicksPerMillisecond, precise.Offset);
                            if (precise == truncated) continue;
                            var repair = new DynamicParameters(parameters); repair.Add("Truncated", truncated); repair.Add("Precise", precise);
                            // Compare-and-set only timestamps which still exactly match
                            // the original millisecond import; never overwrite later edits.
                            await connection.ExecuteAsync($"UPDATE dbo.[{table}] SET [{columns[i].Name}]=@Precise WHERE {string.Join(" AND ", keys)} AND [{columns[i].Name}]=@Truncated", repair, transaction);
                        }
                    }
                    var conditions = columns.Select((column, i) => table == "session" && column.Name == "expires_at"
                        ? "[expires_at] <= SYSUTCDATETIME()"
                        : column.Type == "nvarchar"
                            ? $"(([{column.Name}] COLLATE Latin1_General_100_BIN2 = @p{i} AND DATALENGTH([{column.Name}])=DATALENGTH(@p{i})) OR ([{column.Name}] IS NULL AND @p{i} IS NULL))"
                            : $"([{column.Name}]=@p{i} OR ([{column.Name}] IS NULL AND @p{i} IS NULL))");
                    if (await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{table}] WHERE {string.Join(" AND ", conditions)}", parameters, transaction) < 1)
                        throw new InvalidOperationException($"Source record values differ in {table}; values omitted from diagnostics.");
                }
            }
            var count = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{table}]", transaction: transaction);
            if (verifyOnly ? count < rows.GetArrayLength() : count != rows.GetArrayLength()) throw new InvalidOperationException($"Record count mismatch for {table}.");
            Console.WriteLine($"Validated {table}: {count} records.");
        }
        if (!verifyOnly)
        {
            var nextReference = await connection.ExecuteScalarAsync<long>("SELECT COALESCE(MAX(TRY_CONVERT(bigint, SUBSTRING(reference,5,20))),0)+1 FROM dbo.task", transaction: transaction);
            await connection.ExecuteAsync($"ALTER SEQUENCE dbo.task_reference_seq RESTART WITH {nextReference}", transaction: transaction);
        }
        await transaction.CommitAsync();
        Console.WriteLine(verifyOnly ? "PASS: every original source record and value, including timestamp microseconds, is present in SQL Server; original sessions are expired." : "PASS: source IDs, relationships, timestamp microseconds and record counts imported transactionally. Prior sessions revoked; sign in again.");
    }
}
