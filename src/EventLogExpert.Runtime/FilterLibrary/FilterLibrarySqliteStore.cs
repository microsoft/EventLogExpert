// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace EventLogExpert.Runtime.FilterLibrary;

public sealed class FilterLibrarySqliteStore : IFilterLibraryStore
{
    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS library_entries (
            id           TEXT PRIMARY KEY,
            name         TEXT NOT NULL,
            created_utc  TEXT NOT NULL,
            kind         TEXT NOT NULL,
            payload      TEXT NOT NULL
        );
        """;

    private const string DeleteSql =
        "DELETE FROM library_entries WHERE id = $id;";

    private const string InsertSql =
        "INSERT INTO library_entries (id, name, created_utc, kind, payload) VALUES ($id, $name, $created, $kind, $payload);";

    private const string LoadAllSql =
        "SELECT id, name, created_utc, kind, payload FROM library_entries ORDER BY created_utc;";

    private const string UpdateSql =
        "UPDATE library_entries SET name = $name, created_utc = $created, kind = $kind, payload = $payload WHERE id = $id;";

    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly ITraceLogger _logger;

    public FilterLibrarySqliteStore(string dbPath, ITraceLogger logger)
    {
        ArgumentNullException.ThrowIfNull(dbPath);
        ArgumentNullException.ThrowIfNull(logger);

        _dbPath = dbPath;
        _logger = logger;
        _connectionString = $"Data Source={dbPath}";
    }

    public void Add(LibraryEntry entry)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = InsertSql;
        BindEntry(cmd, entry);
        cmd.ExecuteNonQuery();
    }

    public void Delete(string entryId)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = DeleteSql;
        cmd.Parameters.AddWithValue("$id", entryId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<LibraryEntry> LoadAll()
    {
        using var connection = OpenConnection();

        var entries = ImmutableList.CreateBuilder<LibraryEntry>();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = LoadAllSql;

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var id = reader.GetString(0);
            try
            {
                var name = reader.GetString(1);
                var createdUtc = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                var kind = reader.GetString(3);
                var payload = reader.GetString(4);

                LibraryEntry? entry = DeserializeEntry(id, name, createdUtc, kind, payload);

                if (entry is not null)
                {
                    entries.Add(entry);
                }
                else
                {
                    _logger.Warning($"FilterLibrary skipped row id={id} with unknown Kind '{kind}'.");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"FilterLibrary skipped row id={id}: {ex.Message}");
            }
        }

        return entries.ToImmutable();
    }

    public void Update(LibraryEntry entry)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = UpdateSql;
        BindEntry(cmd, entry);
        cmd.ExecuteNonQuery();
    }

    private static void BindEntry(SqliteCommand cmd, LibraryEntry entry)
    {
        cmd.Parameters.AddWithValue("$id", entry.Id);
        cmd.Parameters.AddWithValue("$name", entry.Name);
        cmd.Parameters.AddWithValue("$created", entry.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));

        switch (entry)
        {
            case LibraryEntrySavedFilter f:
                cmd.Parameters.AddWithValue("$kind", "Filter");
                cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(f.Filter));
                break;

            case LibraryEntryPreset p:
                cmd.Parameters.AddWithValue("$kind", "Preset");
                cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(p.Filters));
                break;

            default:
                throw new InvalidOperationException($"Unsupported LibraryEntry type '{entry.GetType().FullName}'.");
        }
    }

    private static LibraryEntry? DeserializeEntry(string id, string name, DateTimeOffset createdUtc, string kind, string payload)
    {
        return kind switch
        {
            "Filter" => new LibraryEntrySavedFilter(
                id,
                name,
                createdUtc,
                JsonSerializer.Deserialize<SavedFilter>(payload)
                    ?? throw new InvalidOperationException($"LibraryEntrySavedFilter '{id}' payload deserialized to null.")),
            "Preset" => new LibraryEntryPreset(
                id,
                name,
                createdUtc,
                JsonSerializer.Deserialize<ImmutableList<SavedFilter>>(payload) ?? []),
            _ => null,
        };
    }

    private SqliteConnection OpenConnection()
    {
        var dir = Path.GetDirectoryName(_dbPath);

        if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }

        SqliteConnection? connection = null;

        try
        {
            connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var schemaCmd = connection.CreateCommand();
            schemaCmd.CommandText = CreateTableSql;
            schemaCmd.ExecuteNonQuery();

            var result = connection;
            connection = null;

            return result;
        }
        finally
        {
            connection?.Dispose();
        }
    }
}
