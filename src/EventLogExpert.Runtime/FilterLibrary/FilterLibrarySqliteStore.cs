// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using Microsoft.Data.Sqlite;
using System.Collections.Immutable;
using System.Data;
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
                                              is_excluded     INTEGER NULL,
                                              tags            TEXT NULL
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

    private const string DeleteUnloadableSql =
        "DELETE FROM library_entries WHERE id = $id AND kind = $kind AND payload = $payload;";

    private const string FilterKind = "Filter";

    private const string FilterSetKind = "FilterSet";

    private const string FindAutoTrackedFilterByTupleSql = """
                                                           SELECT id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded, tags
                                                           FROM library_entries
                                                           WHERE kind = 'Filter' AND origin = 'AutoTracked'
                                                               AND lower(comparison_text) = lower($comparison_text)
                                                               AND mode = $mode
                                                               AND is_excluded = $is_excluded
                                                           LIMIT 1;
                                                           """;

    private const string InsertOrIgnoreSql = """
                                             INSERT OR IGNORE INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded, tags)
                                             VALUES ($id, $name, $created, $kind, $payload, $is_favorite, $last_used_utc, $origin, $comparison_text, $mode, $is_excluded, $tags);
                                             """;

    private const string InsertSql = """
                                     INSERT INTO library_entries (id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded, tags)
                                     VALUES ($id, $name, $created, $kind, $payload, $is_favorite, $last_used_utc, $origin, $comparison_text, $mode, $is_excluded, $tags);
                                     """;

    private const string LoadAllSql =
        "SELECT id, name, created_utc, kind, payload, is_favorite, last_used_utc, origin, comparison_text, mode, is_excluded, tags FROM library_entries ORDER BY created_utc;";

    private const int MinSystemicUnloadableRowCount = 2;

    private const string UpdateSql = """
                                     UPDATE library_entries
                                     SET name = $name, created_utc = $created, kind = $kind, payload = $payload,
                                         is_favorite = $is_favorite, last_used_utc = $last_used_utc, origin = $origin,
                                         comparison_text = $comparison_text, mode = $mode, is_excluded = $is_excluded,
                                         tags = $tags
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
        ("tags", "TEXT NULL"),
    ];

    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly IErrorBannerService? _errorBannerService;
    private readonly ITraceLogger _logger;

    private int _systemicUnloadableReported;

    public FilterLibrarySqliteStore(string dbPath, ITraceLogger logger, IErrorBannerService? errorBannerService = null)
    {
        ArgumentNullException.ThrowIfNull(dbPath);
        ArgumentNullException.ThrowIfNull(logger);

        _dbPath = dbPath;
        _logger = logger;
        _errorBannerService = errorBannerService;
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task AddAsync(LibraryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = InsertSql;
        BindEntry(cmd, entry);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<(LibraryEntry Entry, bool WasInserted)> AddOrReturnExistingFilterAsync(
        LibraryEntrySavedFilter candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // The partial UNIQUE INDEX idx_library_autotracked_dedup is scoped to origin='AutoTracked'.
        // A UserSaved candidate would bypass the dedup constraint and silently insert duplicates,
        // violating the method's contract. Reject misuse at the boundary.
        if (candidate.Origin != LibraryEntryOrigin.AutoTracked)
        {
            throw new ArgumentException(
                $"{nameof(AddOrReturnExistingFilterAsync)} requires candidate.Origin == {nameof(LibraryEntryOrigin.AutoTracked)}; got '{candidate.Origin}'.",
                nameof(candidate));
        }

        if (string.IsNullOrWhiteSpace(candidate.Filter.ComparisonText))
        {
            throw new ArgumentException(
                $"{nameof(AddOrReturnExistingFilterAsync)} requires non-empty candidate.Filter.ComparisonText for dedup tuple lookup.",
                nameof(candidate));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        // BEGIN IMMEDIATE acquires a RESERVED write lock for the duration of the transaction so a
        // concurrent prune/delete on another connection cannot land between our INSERT OR IGNORE
        // (collision detection) and the follow-up tuple SELECT. Without it, the row could be
        // deleted in the gap and the SELECT would surface zero rows for a "we just saw a collision"
        // path - a benign concurrent delete becomes an exception.
        await using var tx = (SqliteTransaction)(await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false));

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = InsertOrIgnoreSql;
            BindEntry(cmd, candidate);

            if (await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
            {
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

                return (candidate, true);
            }
        }

        await using (var findCmd = connection.CreateCommand())
        {
            findCmd.Transaction = tx;
            findCmd.CommandText = FindAutoTrackedFilterByTupleSql;
            findCmd.Parameters.AddWithValue("$comparison_text", candidate.Filter.ComparisonText);
            findCmd.Parameters.AddWithValue("$mode", candidate.Filter.Mode.ToString());
            findCmd.Parameters.AddWithValue("$is_excluded", candidate.Filter.IsExcluded ? 1 : 0);

            await using var reader = await findCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
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

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return (existing, false);
        }
    }

    public async Task AddRangeAsync(IEnumerable<LibraryEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var materialized = entries.ToList();

        if (materialized.Count == 0) { return; }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)(await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = InsertSql;

            foreach (var entry in materialized)
            {
                cmd.Parameters.Clear();
                BindEntry(cmd, entry);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Guard Rollback so its exception doesn't mask the original insert failure.
            try { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception rollbackEx)
            {
                _logger.Warning($"FilterLibrary AddRange rollback failed after batch insert error: {rollbackEx.Message}");
            }

            throw;
        }
    }

    public async Task DeleteAsync(LibraryEntryId entryId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = DeleteSql;
        cmd.Parameters.AddWithValue("$id", entryId.Value.ToString("D"));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LibraryEntry>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var entries = ImmutableList.CreateBuilder<LibraryEntry>();
        List<(string Id, string Kind, string Payload)>? unloadable = null;
        var unknownKindCount = 0;

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = LoadAllSql;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
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
                        unknownKindCount++;
                        _logger.Warning($"FilterLibrary skipped row id={id} with unknown Kind (kept as forward-version data).");
                    }
                }
                catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
                {
                    _logger.Warning($"FilterLibrary found unloadable row id={id}: {ex.Message}");

                    if (!reader.IsDBNull(0) && !reader.IsDBNull(3) && !reader.IsDBNull(4))
                    {
                        (unloadable ??= []).Add((reader.GetString(0), reader.GetString(3), reader.GetString(4)));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"FilterLibrary skipped row id={id}: {ex.Message}");
                }
            }
        }

        if (unloadable is null)
        {
            return entries.ToImmutable();
        }

        // Ratio is over rows this version can interpret (known kinds), so kept unknown-kind rows don't
        // dilute it and suppress the breaker; any unknown-kind row is itself forward-version evidence.
        var knownKindRows = entries.Count + unloadable.Count;
        var systemic = unloadable.Count >= MinSystemicUnloadableRowCount && unloadable.Count * 2 >= knownKindRows;
        var forwardVersionEvidence = unknownKindCount > 0;

        if (systemic || forwardVersionEvidence)
        {
            _logger.Warning(
                $"FilterLibrary withholding deletion of {unloadable.Count} of {knownKindRows} unloadable known-kind rows ({unknownKindCount} unknown-kind rows present; possible newer-version library).");
            ReportSystemicUnloadable(unloadable.Count);
        }
        else
        {
            await DeleteUnloadableRowsAsync(connection, unloadable, cancellationToken).ConfigureAwait(false);
        }

        return entries.ToImmutable();
    }

    public async Task<bool> TryBumpLastUsedIfNotFavoriteAsync(
        LibraryEntryId entryId,
        DateTimeOffset lastUsedUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = BumpLastUsedIfNotFavoriteSql;
        cmd.Parameters.AddWithValue("$id", entryId.Value.ToString("D"));
        cmd.Parameters.AddWithValue("$last_used_utc", lastUsedUtc.ToString("O", CultureInfo.InvariantCulture));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<bool> TryDeleteAutoTrackedIfNotFavoriteAsync(
        LibraryEntryId entryId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = DeleteAutoTrackedIfNotFavoriteSql;
        cmd.Parameters.AddWithValue("$id", entryId.Value.ToString("D"));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task UpdateAsync(LibraryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        cmd.CommandText = UpdateSql;
        BindEntry(cmd, entry);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LibraryEntryId>> UpdateRangeAsync(
        IReadOnlyList<LibraryEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0) { return []; }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)(await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));

        try
        {
            var updated = ImmutableList.CreateBuilder<LibraryEntryId>();

            await using var cmd = connection.CreateCommand();

            cmd.Transaction = tx;
            cmd.CommandText = UpdateSql;

            foreach (var entry in entries)
            {
                cmd.Parameters.Clear();
                BindEntry(cmd, entry);

                if (await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1) { updated.Add(entry.Id); }
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

            return updated.ToImmutable();
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception rollbackEx)
            {
                _logger.Warning($"FilterLibrary UpdateRange rollback failed after batch update error: {rollbackEx.Message}");
            }

            throw;
        }
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
        var normalizedTags = LibraryEntryTagNormalizer.Normalize(entry.Tags);
        cmd.Parameters.AddWithValue(
            "$tags",
            normalizedTags.Count > 0
                ? JsonSerializer.Serialize(normalizedTags)
                : DBNull.Value);

        switch (entry)
        {
            case LibraryEntrySavedFilter f:
                cmd.Parameters.AddWithValue("$kind", FilterKind);
                cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(f.Filter));
                cmd.Parameters.AddWithValue("$comparison_text", f.Filter.ComparisonText);
                cmd.Parameters.AddWithValue("$mode", f.Filter.Mode.ToString());
                cmd.Parameters.AddWithValue("$is_excluded", f.Filter.IsExcluded ? 1 : 0);
                break;

            case LibraryEntryFilterSet p:
                cmd.Parameters.AddWithValue("$kind", FilterSetKind);
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
        LibraryEntryOrigin origin,
        ImmutableList<string> tags)
    {
        return kind switch
        {
            FilterKind => new LibraryEntrySavedFilter
            {
                Id = id,
                Name = name,
                CreatedUtc = createdUtc,
                IsFavorite = isFavorite,
                LastUsedUtc = lastUsedUtc,
                Origin = origin,
                Tags = tags,
                Filter = JsonSerializer.Deserialize<SavedFilter>(payload)
                    ?? throw new InvalidOperationException($"LibraryEntrySavedFilter '{id}' payload deserialized to null."),
            },
            FilterSetKind => new LibraryEntryFilterSet
            {
                Id = id,
                Name = name,
                CreatedUtc = createdUtc,
                IsFavorite = isFavorite,
                LastUsedUtc = lastUsedUtc,
                Origin = origin,
                Tags = tags,
                Filters = JsonSerializer.Deserialize<ImmutableList<SavedFilter>>(payload) ?? [],
            },
            _ => null,
        };
    }

    private static async Task EnsureSchemaColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = "PRAGMA table_info(library_entries);";
            await using var reader = await probe.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                existing.Add(reader.GetString(1));
            }
        }

        foreach (var (column, definition) in s_requiredColumns)
        {
            if (existing.Contains(column)) { continue; }

            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE library_entries ADD COLUMN {column} {definition};";
            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var indexCmd = connection.CreateCommand();
        indexCmd.CommandText = CreateUniqueIndexSql;
        await indexCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static LibraryEntryOrigin ParseOrigin(string raw) =>
        Enum.TryParse<LibraryEntryOrigin>(raw, ignoreCase: false, out var parsed)
            ? parsed
            : LibraryEntryOrigin.UserSaved;

    private static LibraryEntry? ReadEntry(SqliteDataReader reader)
    {
        var kind = reader.IsDBNull(3) ? null : reader.GetString(3);

        if (kind is not (FilterKind or FilterSetKind)) { return null; }

        var id = new LibraryEntryId(Guid.Parse(reader.GetString(0)));
        var name = reader.GetString(1);
        var createdUtc = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var payload = reader.GetString(4);
        var isFavorite = !reader.IsDBNull(5) && reader.GetInt64(5) != 0;
        DateTimeOffset? lastUsedUtc = reader.IsDBNull(6)
            ? null
            : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var origin = reader.IsDBNull(7)
            ? LibraryEntryOrigin.UserSaved
            : ParseOrigin(reader.GetString(7));

        ImmutableList<string> tags = [];

        if (reader.IsDBNull(11))
        {
            return DeserializeEntry(id, name, createdUtc, kind, payload, isFavorite, lastUsedUtc, origin, tags);
        }

        try { tags = JsonSerializer.Deserialize<ImmutableList<string>>(reader.GetString(11)) ?? []; }
        catch (JsonException) { tags = []; }

        return DeserializeEntry(id, name, createdUtc, kind, payload, isFavorite, lastUsedUtc, origin, tags);
    }

    private async Task DeleteUnloadableRowsAsync(
        SqliteConnection connection,
        List<(string Id, string Kind, string Payload)> rows,
        CancellationToken cancellationToken)
    {
        foreach (var (id, kind, payload) in rows)
        {
            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = DeleteUnloadableSql;
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$kind", kind);
                cmd.Parameters.AddWithValue("$payload", payload);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning($"FilterLibrary failed to remove unloadable row id={id}: {ex.Message}");
            }
        }
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

            await using (var schemaCmd = connection.CreateCommand())
            {
                schemaCmd.CommandText = CreateTableSql;
                await schemaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await EnsureSchemaColumnsAsync(connection, cancellationToken).ConfigureAwait(false);

            var result = connection;
            connection = null;

            return result;
        }
        finally
        {
            if (connection is not null) { await connection.DisposeAsync().ConfigureAwait(false); }
        }
    }

    private void ReportSystemicUnloadable(int unloadableCount)
    {
        if (_errorBannerService is null) { return; }

        if (Interlocked.Exchange(ref _systemicUnloadableReported, 1) != 0) { return; }

        _errorBannerService.ReportError(
            "Filter library not fully loaded",
            $"{unloadableCount} library entries couldn't be read and were left in place to avoid data loss. This usually means the library was written by a newer version of the app.");
    }
}
