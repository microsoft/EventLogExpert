// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Concurrency;
using EventLogExpert.Provider.Database.Maintenance;
using EventLogExpert.Provider.Database.Serialization;
using EventLogExpert.Provider.Lookup;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.Provider.Schema;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;

namespace EventLogExpert.Provider.Database.Context;

public sealed class ProviderDbContext : DbContext, IProviderDetailsLookup
{
    private const string SchemaLockScope = "ProviderDbSchema";

    // Must stay in sync with DatabaseFileOperations.UpgradeBackupSuffix (Runtime layer; not referenceable here).
    private const string UpgradeBackupSuffix = ".upgrade.bak";

    private static readonly TimeSpan s_schemaCreateTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_schemaMutationTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan s_schemaProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly ITraceLogger? _logger;
    private readonly bool _readOnly;
    private readonly InterProcessFileLock _schemaLock;

    public ProviderDbContext(string path, bool readOnly, ITraceLogger? logger = null)
        : this(path, readOnly, true, logger) { }

    public ProviderDbContext(string path, bool readOnly, bool ensureCreated, ITraceLogger? logger = null)
    {
        _logger = logger;

        _logger?.Debug(
            $"Instantiating ProviderDbContext. path: {path} readOnly: {readOnly} ensureCreated: {ensureCreated}");

        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Path = path;
        _readOnly = readOnly;
        _schemaLock = new InterProcessFileLock(SchemaLockScope, path);

        if (readOnly)
        {
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        if (!ensureCreated) { return; }

        // EnsureCreated builds the ProviderDetails table from the current (canonical) model only when the database did
        // not already exist. Guard cross-process (concurrent instances / the elevated helper) so two writers don't
        // create + stamp at once. A .upgrade.bak present here signals a prior upgrade crashed mid-rebuild; creating
        // empty tables over it would discard recoverable data, so refuse and surface recovery.
        RunUnderSchemaLock(s_schemaCreateTimeout, () =>
        {
            if (File.Exists(Path + UpgradeBackupSuffix))
            {
                throw new DatabaseUpgradeException(
                    Path,
                    $"An interrupted-upgrade backup ('{UpgradeBackupSuffix}') is present for {Path}; resolve recovery before the database can be opened.");
            }

            if (Database.EnsureCreated() && !readOnly)
            {
                StampCanonicalUserVersion();
            }
        });
    }

    public string Name { get; }

    public DbSet<ProviderDetails> ProviderDetails { get; set; }

    private string Path { get; }

    // Ordered by VersionKey so the consolidated winner's true ties (same description length) break deterministically on
    // a stable per-database order rather than SQLite's unspecified row order.
    public IReadOnlyList<ProviderDetails> FindAllProviderVersions(string providerName) =>
        ProviderDetails
            .Where(p => p.ProviderName == providerName)
            .OrderBy(p => p.VersionKey)
            .ToList();

    public ProviderDetails? FindProvider(string providerName) =>
        ProviderDetails.FirstOrDefault(p => p.ProviderName == providerName);

    public DatabaseSchemaState IsUpgradeNeeded()
    {
        // Probe under the schema lock so a concurrent cross-process upgrade (mid drop/rebuild) can't be observed as a
        // torn 'Unknown'. A short timeout keeps readers responsive; on timeout the caller treats the result as transient
        // (retry later), never as a definitive classification.
        DatabaseSchemaState? result = null;

        if (!_schemaLock.TryRun(s_schemaProbeTimeout, () => result = IsUpgradeNeededCore()))
        {
            throw new SchemaLockTimeoutException(Path);
        }

        return result!;
    }

    public void PerformUpgradeIfNeeded() => RunUnderSchemaLock(s_schemaMutationTimeout, PerformUpgradeIfNeededCore);

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite($"Data Source={Path};Mode={(_readOnly ? "ReadOnly" : "ReadWriteCreate")}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // INVARIANT: once a build that stamps the canonical user_version has shipped, any change to the persisted
        // ProviderDetails model (columns, key, or converters) MUST bump DatabaseSchemaVersion.Current - detection
        // trusts the stored stamp over the column shape, so a model change without a bump would let a stamped
        // old-shape database read as canonical and crash EF. During the prerelease window (no stamped build has
        // shipped yet) the v4 shape is still being finalized in place WITHOUT bumping: every real database is
        // unstamped (user_version=0) and rebuilds through the upgrade path, and the dev-only (DEBUG) shape self-heal
        // (see IsCanonicalShape) rebuilds stamped developer databases whose columns no longer match the model.
        modelBuilder.Entity<ProviderDetails>()
            .HasKey(e => new { e.ProviderName, e.VersionKey });

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.ProviderName)
            .UseCollation("NOCASE");

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Messages)
            .HasConversion<CompressedJsonValueConverter<IReadOnlyList<MessageModel>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Parameters)
            .HasConversion<CompressedJsonValueConverter<IReadOnlyList<MessageModel>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Events)
            .HasConversion<CompressedJsonValueConverter<IReadOnlyList<EventModel>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Keywords)
            .HasConversion<CompressedJsonValueConverter<IDictionary<long, string>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Opcodes)
            .HasConversion<CompressedJsonValueConverter<IDictionary<int, string>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Tasks)
            .HasConversion<CompressedJsonValueConverter<IDictionary<int, string>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Maps)
            .HasConversion<CompressedJsonValueConverter<IReadOnlyDictionary<string, ValueMapDefinition>>>();
    }

    private static bool IsType(string? actual, string expected) =>
        string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static ProviderDetails ReadCompressedRow(IDataReader reader, string providerName)
    {
        var details = new ProviderDetails
        {
            ProviderName = providerName,
            Messages = CompressedJsonValueConverter<List<MessageModel>>.ConvertFromCompressedJson((byte[])reader["Messages"]),
            Parameters = CompressedJsonValueConverter<List<MessageModel>>.ConvertFromCompressedJson((byte[])reader["Parameters"]),
            Events = CompressedJsonValueConverter<List<EventModel>>.ConvertFromCompressedJson((byte[])reader["Events"]),
            Keywords = CompressedJsonValueConverter<Dictionary<long, string>>.ConvertFromCompressedJson((byte[])reader["Keywords"]),
            Opcodes = CompressedJsonValueConverter<Dictionary<int, string>>.ConvertFromCompressedJson((byte[])reader["Opcodes"]),
            Tasks = CompressedJsonValueConverter<Dictionary<int, string>>.ConvertFromCompressedJson((byte[])reader["Tasks"]),
            Maps = TryReadMaps(reader),
            VersionKey = TryReadVersionKey(reader),
            ResolvedFromOwningPublisher = TryReadResolvedFromOwningPublisher(reader, providerName)
        };

        ReadProvenanceInto(reader, details);

        return details;
    }

    private static void ReadProvenanceInto(IDataReader reader, ProviderDetails details)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.IsDBNull(i)) { continue; }

            switch (reader.GetName(i))
            {
                case nameof(Resolution.ProviderDetails.SourceOsBuild):
                    details.SourceOsBuild = Convert.ToInt32(reader.GetValue(i));
                    break;
                case nameof(Resolution.ProviderDetails.SourceOsRevision):
                    details.SourceOsRevision = Convert.ToInt32(reader.GetValue(i));
                    break;
                case nameof(Resolution.ProviderDetails.SourceOsEdition):
                    details.SourceOsEdition = reader.GetString(i);
                    break;
                case nameof(Resolution.ProviderDetails.SourceOsDisplayVersion):
                    details.SourceOsDisplayVersion = reader.GetString(i);
                    break;
                case nameof(Resolution.ProviderDetails.MessageFileVersion):
                    details.MessageFileVersion = reader.GetString(i);
                    break;
            }
        }
    }

    private static bool TryDetectPrimaryKeyNoCaseCollation(DbConnection connection)
    {
        string? pkIndexName = null;

        using (var indexListCommand = connection.CreateCommand())
        {
            indexListCommand.CommandText = "PRAGMA index_list(\"ProviderDetails\")";

            using var indexListReader = indexListCommand.ExecuteReader();

            while (indexListReader.Read())
            {
                var origin = indexListReader["origin"]?.ToString();

                if (!string.Equals(origin, "pk", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pkIndexName = indexListReader["name"]?.ToString();

                break;
            }
        }

        if (string.IsNullOrEmpty(pkIndexName)) { return false; }

        using var indexInfoCommand = connection.CreateCommand();

        indexInfoCommand.CommandText = $"PRAGMA index_xinfo(\"{pkIndexName}\")";

        using var indexInfoReader = indexInfoCommand.ExecuteReader();

        while (indexInfoReader.Read())
        {
            var columnName = indexInfoReader["name"]?.ToString();

            if (!string.Equals(columnName, nameof(Resolution.ProviderDetails.ProviderName), StringComparison.Ordinal))
            {
                continue;
            }

            var collation = indexInfoReader["coll"]?.ToString();

            return string.Equals(collation, "NOCASE", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static IReadOnlyDictionary<string, ValueMapDefinition> TryReadMaps(IDataReader reader)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (!string.Equals(reader.GetName(i),
                nameof(Resolution.ProviderDetails.Maps),
                StringComparison.Ordinal))
            {
                continue;
            }

            if (reader.IsDBNull(i))
            {
                break;
            }

            return CompressedJsonValueConverter<IReadOnlyDictionary<string, ValueMapDefinition>>
                .ConvertFromCompressedJson((byte[])reader.GetValue(i));
        }

        return ReadOnlyDictionary<string, ValueMapDefinition>.Empty;
    }

    private static string? TryReadResolvedFromOwningPublisher(IDataReader reader, string providerName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (!string.Equals(reader.GetName(i),
                nameof(Resolution.ProviderDetails.ResolvedFromOwningPublisher),
                StringComparison.Ordinal))
            {
                continue;
            }

            return reader.IsDBNull(i) ? null : reader.GetString(i);
        }

        _ = providerName;

        return null;
    }

    private static string TryReadVersionKey(IDataReader reader)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (!string.Equals(reader.GetName(i),
                nameof(Resolution.ProviderDetails.VersionKey),
                StringComparison.Ordinal))
            {
                continue;
            }

            return reader.IsDBNull(i) ? string.Empty : reader.GetString(i);
        }

        return string.Empty;
    }

#if DEBUG
    private bool ActualColumnsMatchModel(DbConnection connection)
    {
        var actualColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"ProviderDetails\")";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var columnName = reader["name"]?.ToString();

                if (!string.IsNullOrEmpty(columnName)) { actualColumns.Add(columnName); }
            }
        }

        var entityType = Model.FindEntityType(typeof(ProviderDetails));

        if (entityType is null) { return true; }

        var modelColumns = entityType.GetProperties().Select(property => property.GetColumnName());

        return actualColumns.SetEquals(modelColumns);
    }
#endif

    // In RELEASE this compiles to `return true`, so detection is byte-identical to a plain user_version==Current
    // short-circuit - no behavior change for shipped builds, no per-event cost (it runs only when a database opens).
    // In DEBUG it additionally verifies the physical column set still matches the current EF model, so a developer
    // database stamped with the current version but built from an earlier model shape rebuilds itself in place
    // instead of requiring a manual delete. The expected set is derived from the model, so this self-heals EVERY
    // future within-stamp column add/rename, not just the provenance columns. Type-only changes still require a
    // DatabaseSchemaVersion bump (the must-bump invariant) - name-set equivalence deliberately ignores affinity to
    // avoid SQLite type-string false positives.
    private bool IsCanonicalShape(DbConnection connection)
    {
#if DEBUG
        return ActualColumnsMatchModel(connection);
#else
        _ = connection;

        return true;
#endif
    }

    private DatabaseSchemaState IsUpgradeNeededCore()
    {
        var connection = Database.GetDbConnection();
        Database.OpenConnection();

        try
        {
            int userVersion;

            using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.CommandText = "PRAGMA user_version";
                userVersion = Convert.ToInt32(versionCommand.ExecuteScalar());
            }

            if (userVersion == DatabaseSchemaVersion.Current && IsCanonicalShape(connection))
            {
                // Stamped canonical database - current shape, no rebuild. Skip the structural probe.
                return LogResult(new DatabaseSchemaState(DatabaseSchemaVersion.Current));
            }

            if (userVersion > DatabaseSchemaVersion.Current)
            {
                // Stamped by a newer build than this one understands; never rebuild/downgrade it.
                return LogResult(new DatabaseSchemaState(DatabaseSchemaVersion.Unknown) { NeedsUpgrade = true });
            }

            // userVersion below Current (0 for every database created before the stamp existed; also any sub-current
            // stamp): classify structurally below (to distinguish v1/v2-unsupported and Unknown from rebuildable v3/v4),
            // then flag for rebuild unconditionally - an unstamped/stale database is never canonical even when its columns
            // already match the current shape (and a future Current bump correctly rebuilds old stamped databases).
            string? messagesType = null;
            string? eventsType = null;
            string? keywordsType = null;
            string? opcodesType = null;
            string? tasksType = null;
            string? parametersType = null;
            bool hasAnyColumn = false;
            bool hasParametersColumn = false;
            bool hasResolvedColumn = false;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(\"ProviderDetails\")";
                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    hasAnyColumn = true;

                    var columnName = reader["name"]?.ToString();
                    var columnType = reader["type"]?.ToString();

                    if (string.Equals(columnName, "Messages", StringComparison.Ordinal))
                    {
                        messagesType = columnType;
                    }
                    else if (string.Equals(columnName, "Events", StringComparison.Ordinal))
                    {
                        eventsType = columnType;
                    }
                    else if (string.Equals(columnName, "Keywords", StringComparison.Ordinal))
                    {
                        keywordsType = columnType;
                    }
                    else if (string.Equals(columnName, "Opcodes", StringComparison.Ordinal))
                    {
                        opcodesType = columnType;
                    }
                    else if (string.Equals(columnName, "Tasks", StringComparison.Ordinal))
                    {
                        tasksType = columnType;
                    }
                    else if (string.Equals(columnName, "Parameters", StringComparison.Ordinal))
                    {
                        parametersType = columnType;
                        hasParametersColumn = true;
                    }
                    else if (string.Equals(columnName,
                        nameof(Resolution.ProviderDetails.ResolvedFromOwningPublisher),
                        StringComparison.Ordinal))
                    {
                        hasResolvedColumn = true;
                    }
                }
            }

            int currentVersion;

            if (!hasAnyColumn)
            {
                currentVersion = DatabaseSchemaVersion.Unknown;
            }
            else
            {
                var payloadColumnsAllText = IsType(messagesType, "TEXT") &&
                    IsType(eventsType, "TEXT") &&
                    IsType(keywordsType, "TEXT") &&
                    IsType(opcodesType, "TEXT") &&
                    IsType(tasksType, "TEXT");

                var payloadColumnsAllBlob = IsType(messagesType, "BLOB") &&
                    IsType(eventsType, "BLOB") &&
                    IsType(keywordsType, "BLOB") &&
                    IsType(opcodesType, "BLOB") &&
                    IsType(tasksType, "BLOB");

                switch (payloadColumnsAllText)
                {
                    case true when !hasParametersColumn: currentVersion = 1; break;
                    case true when IsType(parametersType, "TEXT"): currentVersion = 2; break;
                    default:
                        {
                            if (payloadColumnsAllBlob && IsType(parametersType, "BLOB"))
                            {
                                var pkIsNoCase = TryDetectPrimaryKeyNoCaseCollation(connection);
                                currentVersion = hasResolvedColumn && pkIsNoCase ? 4 : 3;
                            }
                            else
                            {
                                currentVersion = DatabaseSchemaVersion.Unknown;
                            }

                            break;
                        }
                }
            }

            return LogResult(new DatabaseSchemaState(currentVersion) { NeedsUpgrade = true });

            DatabaseSchemaState LogResult(DatabaseSchemaState result)
            {
                _logger?.Debug(
                    $"{nameof(ProviderDbContext)}.{nameof(IsUpgradeNeeded)}() for database {Path}. currentVersion: {result.CurrentVersion} needsUpgrade: {result.NeedsUpgrade}");

                return result;
            }
        }
        finally
        {
            Database.CloseConnection();
        }
    }

    private void PerformUpgradeIfNeededCore()
    {
        // Lock-free probe: the whole method already runs under the schema lock (held by PerformUpgradeIfNeeded), and the
        // file lock is not re-entrant, so the public lock-taking IsUpgradeNeeded must not be called here.
        var state = IsUpgradeNeededCore();

        if (!state.NeedsUpgrade) { return; }

        if (state.CurrentVersion == DatabaseSchemaVersion.Unknown)
        {
            throw new DatabaseUpgradeException(
                Path,
                SchemaStateMessages.UnrecognizedSchema(SchemaStateMessages.DefaultLabel, Path));
        }

        if (state.CurrentVersion is 1 or 2)
        {
            throw new DatabaseUpgradeException(
                Path,
                SchemaStateMessages.UnsupportedV1OrV2Schema(Path, state.CurrentVersion));
        }

        var size = new FileInfo(Path).Length;

        _logger?.Information(
            $"ProviderDbContext upgrading database (current v{state.CurrentVersion} → v{DatabaseSchemaVersion.Current}). Size: {size} Path: {Path}");

        var connection = Database.GetDbConnection();
        Database.OpenConnection();

        var allProviderDetails = new List<ProviderDetails>();

        try
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM \"ProviderDetails\"";

                using var detailsReader = command.ExecuteReader();

                while (detailsReader.Read())
                {
                    var providerName = (string)detailsReader["ProviderName"];

                    var details = ReadCompressedRow(detailsReader, providerName);

                    allProviderDetails.Add(details);
                }
            }

            var merged = ProviderDetailsMerger.MergeCaseInsensitiveDuplicates(allProviderDetails, Path);

            using (var dropCommand = connection.CreateCommand())
            {
                dropCommand.CommandText = "DROP TABLE \"ProviderDetails\"";
                dropCommand.ExecuteNonQuery();
                dropCommand.CommandText = "VACUUM";
                dropCommand.ExecuteNonQuery();
            }

            allProviderDetails = merged;
        }
        finally
        {
            Database.CloseConnection();
        }

        Database.EnsureCreated();

        foreach (var p in allProviderDetails)
        {
            ProviderDetails.Add(p);
        }

        SaveChanges();

        size = new FileInfo(Path).Length;

        _logger?.Information($"ProviderDbContext upgrade completed. Size: {size} Path: {Path}");

        // The rebuild produced the current model's shape (EnsureCreated above). Stamp the canonical user_version so the
        // database is recognized as current on the next open.
        StampCanonicalUserVersion();
    }

    private void RunUnderSchemaLock(TimeSpan timeout, Action action)
    {
        try
        {
            _schemaLock.Run(timeout, action);
        }
        catch (TimeoutException ex)
        {
            // Surface a lock-acquisition timeout as the domain transient exception so every schema entry point
            // (probe, create, upgrade) fails the same recoverable way instead of leaking a raw TimeoutException.
            throw new SchemaLockTimeoutException(Path, ex);
        }
    }

    private void StampCanonicalUserVersion()
    {
        var connection = Database.GetDbConnection();
        Database.OpenConnection();

        try
        {
            using var command = connection.CreateCommand();
            // PRAGMA user_version does not accept parameters; the value is a compile-time constant integer.
            command.CommandText = $"PRAGMA user_version = {DatabaseSchemaVersion.Current}";
            command.ExecuteNonQuery();
        }
        finally
        {
            Database.CloseConnection();
        }
    }
}
