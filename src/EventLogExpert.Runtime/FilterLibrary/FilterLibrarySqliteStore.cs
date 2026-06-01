// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class FilterLibrarySqliteStore : IFilterLibraryStore
{
    private const string BumpLastUsedIfNotFavoriteSql = """
                                                        UPDATE library_entries
                                                        SET last_used_utc = $last_used_utc
                                                        WHERE id = $id AND is_favorite = 0;
                                                        """;

    private const string CreateTableSql = """
                                          CREATE TABLE IF NOT EXISTS library_entries (
                                              id              TEXT PRIMARY KEY,
                                              name            TEXT NOT NULL,
                                              created_utc     TEXT NOT NULL,
                                              kind            TEXT NOT NULL,
                                              payload         TEXT NOT NULL,
                                              is_favorite     INTEGER NOT NULL DEFAULT 0,
                                              last_used_utc   TEXT NULL,
                                              origin          TEXT NOT NULL DEFAULT 'UserSaved',
                                              comparison_text TEXT NULL,
                                              mode            TEXT NULL,
                                              is_excluded     INTEGER NULL
                                          );
                                          """;

    private const string CreateUniqueIndexSql = """
                                                CREATE UNIQUE INDEX IF NOT EXISTS idx_library_autotracked_dedup
                                                ON library_entries(lower(comparison_text), mode, is_excluded)
                                                WHERE kind = 'Filter' AND origin = 'AutoTracked';
                                                """;

    private const string DeleteAutoTrackedIfNotFavoriteSql = """
                                                             DELETE FROM library_entries
                                                             WHERE id = $id AND kind = 'Filter' AND origin = 'AutoTracked' AND is_favorite = 0
                                                                   AND last_used_utc IS NOT NULL;
                                                             """;

    private const string DeleteSql =
        "DELETE FROM library_entries WHERE id = $id;";

    private const string FindAutoTrackedFilterByTupleSql = """
                                                           SELECT id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded
                                                           FROM library_entries
                                                           WHERE kind = 'Filter' AND origin = 'AutoTracked'
                                                               AND lower(comparison_text) = lower($comparison_text)
                                                               AND mode = $mode
                                                               AND is_excluded = $is_excluded
                                                           LIMIT 1;
                                                           """;

    private const string InsertOrIgnoreSql = """
                                             INSERT OR IGNORE INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded)
                                             VALUES ($id, $name, $created, $kind, $payload, $is_favorite, $last_used_utc, $origin, $comparison_text, $mode, $is_excluded);
                                             """;

    private const string InsertSql = """
                                     INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded)
                                     VALUES ($id, $name, $created, $kind, $payload, $is_favorite, $last_used_utc, $origin, $comparison_text, $mode, $is_excluded);
                                     """;

    private const string LoadAllSql =
        "SELECT id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded FROM library_entries ORDER BY created_utc;";

    private const string UpdateSql = """
                                     UPDATE library_entries
                                     SET name = $name, created_utc = $created, kind = $kind, payload = $payload,
                                         is_favorite = $is_favorite, last_used_utc = $last_used_utc, origin = $origin,
                                         comparison_text = $comparison_text, mode = $mode, is_excluded = $is_excluded
                                     WHERE id = $id;
                                     """;

    private static readonly (string Column, string Definition)[] s_requiredColumns =
    [
        ("is_favorite", "INTEGER NOT NULL DEFAULT 0"),
        ("last_used_utc", "TEXT NULL"),
        ("origin", "TEXT NOT NULL DEFAULT 'UserSaved'"),
        ("comparison_text", "TEXT NULL"),
        ("mode", "TEXT NULL"),
        ("is_excluded", "INTEGER NULL"),
    ];

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

    public (LibraryEntry Entry, bool WasInserted) AddOrReturnExistingFilter(LibraryEntrySavedFilter candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // The partial UNIQUE INDEX idx_library_autotracked_dedup is scoped to origin='AutoTracked'.
        // A UserSaved candidate would bypass the dedup constraint and silently insert duplicates,
        // violating the method's contract. Reject misuse at the boundary.
        if (candidate.Origin != LibraryEntryOrigin.AutoTracked)
        {
            throw new ArgumentException(
                $"{nameof(AddOrReturnExistingFilter)} requires candidate.Origin == {nameof(LibraryEntryOrigin.AutoTracked)}; got '{candidate.Origin}'.",
                nameof(candidate));
        }

        if (string.IsNullOrWhiteSpace(candidate.Filter.ComparisonText))
        {
            throw new ArgumentException(
                $"{nameof(AddOrReturnExistingFilter)} requires non-empty candidate.Filter.ComparisonText for dedup tuple lookup.",
                nameof(candidate));
        }

        using var connection = OpenConnection();
        // BEGIN IMMEDIATE acquires a RESERVED write lock for the duration of the transaction so a
        // concurrent prune/delete on another connection cannot land between our INSERT OR IGNORE
        // (collision detection) and the follow-up tuple SELECT. Without it, the row could be
        // deleted in the gap and the SELECT would surface zero rows for a "we just saw a collision"
        // path — a benign concurrent delete becomes an exception.
        using var tx = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = InsertOrIgnoreSql;
            BindEntry(cmd, candidate);

            if (cmd.ExecuteNonQuery() == 1)
            {
                tx.Commit();

                return (candidate, true);
            }
        }

        using (var findCmd = connection.CreateCommand())
        {
            findCmd.Transaction = tx;
            findCmd.CommandText = FindAutoTrackedFilterByTupleSql;
            findCmd.Parameters.AddWithValue("$comparison_text", candidate.Filter.ComparisonText);
            findCmd.Parameters.AddWithValue("$mode", candidate.Filter.Mode.ToString());
            findCmd.Parameters.AddWithValue("$is_excluded", candidate.Filter.IsExcluded ? 1 : 0);

            using var reader = findCmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException(
                    $"FilterLibrary AddOrReturnExistingFilter: INSERT OR IGNORE for '{candidate.Filter.ComparisonText}' collided but no existing row matched the tuple inside the transaction.");
            }

            var existing = ReadEntry(reader);
            if (existing is null)
            {
                throw new InvalidOperationException(
                    $"FilterLibrary AddOrReturnExistingFilter: collision row had unknown Kind for '{candidate.Filter.ComparisonText}'.");
            }

            tx.Commit();
            return (existing, false);
        }
    }

    public void AddRange(IEnumerable<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var materialized = entries.ToList();

        if (materialized.Count == 0) { return; }

        using var connection = OpenConnection();
        using var tx = connection.BeginTransaction();

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = InsertSql;

            foreach (var entry in materialized)
            {
                cmd.Parameters.Clear();
                BindEntry(cmd, entry);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            // Guard Rollback so its exception doesn't mask the original insert failure.
            try { tx.Rollback(); }
            catch (Exception rollbackEx)
            {
                _logger.Warning($"FilterLibrary AddRange rollback failed after batch insert error: {rollbackEx.Message}");
            }

            throw;
        }
    }

    public void Delete(LibraryEntryId entryId)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = DeleteSql;
        cmd.Parameters.AddWithValue("$id", entryId.Value.ToString("D"));
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
            var id = reader.IsDBNull(0) ? "<unknown>" : reader.GetString(0);
            try
            {
                var entry = ReadEntry(reader);

                if (entry is not null)
                {
                    entries.Add(entry);
                }
                else
                {
                    _logger.Warning($"FilterLibrary skipped row id={id} with unknown Kind.");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"FilterLibrary skipped row id={id}: {ex.Message}");
            }
        }

        return entries.ToImmutable();
    }

    public bool TryBumpLastUsedIfNotFavorite(LibraryEntryId entryId, DateTimeOffset lastUsedUtc)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = BumpLastUsedIfNotFavoriteSql;
        cmd.Parameters.AddWithValue("$id", entryId.Value.ToString("D"));
        cmd.Parameters.AddWithValue("$last_used_utc", lastUsedUtc.ToString("O", CultureInfo.InvariantCulture));
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool TryDeleteAutoTrackedIfNotFavorite(LibraryEntryId entryId)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = DeleteAutoTrackedIfNotFavoriteSql;
        cmd.Parameters.AddWithValue("$id", entryId.Value.ToString("D"));
        return cmd.ExecuteNonQuery() > 0;
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
        cmd.Parameters.AddWithValue("$id", entry.Id.Value.ToString("D"));
        cmd.Parameters.AddWithValue("$name", entry.Name);
        cmd.Parameters.AddWithValue("$created", entry.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$is_favorite", entry.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue(
            "$last_used_utc",
            entry.LastUsedUtc is { } lastUsed
                ? lastUsed.ToString("O", CultureInfo.InvariantCulture)
                : DBNull.Value);
        cmd.Parameters.AddWithValue("$origin", entry.Origin.ToString());

        switch (entry)
        {
            case LibraryEntrySavedFilter f:
                cmd.Parameters.AddWithValue("$kind", "Filter");
                cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(f.Filter));
                cmd.Parameters.AddWithValue("$comparison_text", f.Filter.ComparisonText);
                cmd.Parameters.AddWithValue("$mode", f.Filter.Mode.ToString());
                cmd.Parameters.AddWithValue("$is_excluded", f.Filter.IsExcluded ? 1 : 0);
                break;

            case LibraryEntryPreset p:
                cmd.Parameters.AddWithValue("$kind", "Preset");
                cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(p.Filters));
                cmd.Parameters.AddWithValue("$comparison_text", DBNull.Value);
                cmd.Parameters.AddWithValue("$mode", DBNull.Value);
                cmd.Parameters.AddWithValue("$is_excluded", DBNull.Value);
                break;

            default:
                throw new InvalidOperationException($"Unsupported LibraryEntry type '{entry.GetType().FullName}'.");
        }
    }

    private static LibraryEntry? DeserializeEntry(
        LibraryEntryId id,
        string name,
        DateTimeOffset createdUtc,
        string kind,
        string payload,
        bool isFavorite,
        DateTimeOffset? lastUsedUtc,
        LibraryEntryOrigin origin)
    {
        return kind switch
        {
            "Filter" => new LibraryEntrySavedFilter
            {
                Id = id,
                Name = name,
                CreatedUtc = createdUtc,
                IsFavorite = isFavorite,
                LastUsedUtc = lastUsedUtc,
                Origin = origin,
                Filter = JsonSerializer.Deserialize<SavedFilter>(payload)
                    ?? throw new InvalidOperationException($"LibraryEntrySavedFilter '{id}' payload deserialized to null."),
            },
            "Preset" => new LibraryEntryPreset
            {
                Id = id,
                Name = name,
                CreatedUtc = createdUtc,
                IsFavorite = isFavorite,
                LastUsedUtc = lastUsedUtc,
                Origin = origin,
                Filters = JsonSerializer.Deserialize<ImmutableList<SavedFilter>>(payload) ?? [],
            },
            _ => null,
        };
    }

    private static void EnsureSchemaColumns(SqliteConnection connection)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "PRAGMA table_info(library_entries);";
            using var reader = probe.ExecuteReader();

            while (reader.Read())
            {
                existing.Add(reader.GetString(1));
            }
        }

        foreach (var (column, definition) in s_requiredColumns)
        {
            if (existing.Contains(column)) { continue; }

            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE library_entries ADD COLUMN {column} {definition};";
            alter.ExecuteNonQuery();
        }

        using var indexCmd = connection.CreateCommand();
        indexCmd.CommandText = CreateUniqueIndexSql;
        indexCmd.ExecuteNonQuery();
    }

    private static LibraryEntryOrigin ParseOrigin(string raw) =>
        Enum.TryParse<LibraryEntryOrigin>(raw, ignoreCase: false, out var parsed)
            ? parsed
            : LibraryEntryOrigin.UserSaved;

    private static LibraryEntry? ReadEntry(SqliteDataReader reader)
    {
        var id = new LibraryEntryId(Guid.Parse(reader.GetString(0)));
        var name = reader.GetString(1);
        var createdUtc = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var kind = reader.GetString(3);
        var payload = reader.GetString(4);
        var isFavorite = !reader.IsDBNull(5) && reader.GetInt64(5) != 0;
        DateTimeOffset? lastUsedUtc = reader.IsDBNull(6)
            ? null
            : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var origin = reader.IsDBNull(7)
            ? LibraryEntryOrigin.UserSaved
            : ParseOrigin(reader.GetString(7));

        return DeserializeEntry(id, name, createdUtc, kind, payload, isFavorite, lastUsedUtc, origin);
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

            using (var schemaCmd = connection.CreateCommand())
            {
                schemaCmd.CommandText = CreateTableSql;
                schemaCmd.ExecuteNonQuery();
            }

            EnsureSchemaColumns(connection);

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
