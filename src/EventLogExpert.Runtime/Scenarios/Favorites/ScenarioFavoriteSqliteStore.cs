// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Data.Sqlite;
using System.Globalization;

namespace EventLogExpert.Runtime.Scenarios.Favorites;

internal sealed class ScenarioFavoriteSqliteStore : IScenarioFavoriteStore
{
    private const string CreateTableSql = """
                                          CREATE TABLE IF NOT EXISTS favorite_scenarios (
                                              scenario_id TEXT PRIMARY KEY,
                                              created_utc TEXT NOT NULL
                                          );
                                          """;

    private const string DeleteSql = "DELETE FROM favorite_scenarios WHERE scenario_id = $id;";

    private const string InsertOrIgnoreSql = """
                                             INSERT OR IGNORE INTO favorite_scenarios (scenario_id, created_utc)
                                             VALUES ($id, $created);
                                             """;

    private const string LoadAllSql = "SELECT scenario_id FROM favorite_scenarios ORDER BY created_utc;";

    private readonly string _connectionString;
    private readonly string _dbPath;

    public ScenarioFavoriteSqliteStore(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        _dbPath = dbPath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public async Task AddAsync(string scenarioId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(scenarioId);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = InsertOrIgnoreSql;
        cmd.Parameters.AddWithValue("$id", scenarioId);
        cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string scenarioId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(scenarioId);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = DeleteSql;
        cmd.Parameters.AddWithValue("$id", scenarioId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = LoadAllSql;

        var ids = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(_dbPath);

        if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }

        SqliteConnection? connection = null;

        try
        {
            connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA busy_timeout=5000;";
                await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var schemaCmd = connection.CreateCommand())
            {
                schemaCmd.CommandText = CreateTableSql;
                await schemaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var result = connection;
            connection = null;

            return result;
        }
        finally
        {
            if (connection is not null) { await connection.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
