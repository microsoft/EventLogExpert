// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace EventLogExpert.Eventing.EventProviderDatabase;

public sealed class EventProviderDbContext : DbContext
{
    private readonly ITraceLogger? _logger;
    private readonly bool _readOnly;

    public EventProviderDbContext(string path, bool readOnly, ITraceLogger? logger = null)
        : this(path, readOnly, true, logger) { }

    public EventProviderDbContext(string path, bool readOnly, bool ensureCreated, ITraceLogger? logger = null)
    {
        _logger = logger;

        _logger?.Debug(
            $"Instantiating EventProviderDbContext. path: {path} readOnly: {readOnly} ensureCreated: {ensureCreated}");

        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Path = path;
        _readOnly = readOnly;

        if (ensureCreated)
        {
            Database.EnsureCreated();
        }
    }

    public string Name { get; }

    public DbSet<ProviderDetails> ProviderDetails { get; set; }

    private string Path { get; }

    public ProviderDatabaseSchemaState IsUpgradeNeeded()
    {
        var connection = Database.GetDbConnection();
        Database.OpenConnection();

        try
        {
            string? messagesType = null;
            string? eventsType = null;
            string? keywordsType = null;
            string? opcodesType = null;
            string? tasksType = null;
            string? parametersType = null;
            var hasAnyColumn = false;
            var hasParametersColumn = false;
            var hasResolvedColumn = false;

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
                        nameof(Providers.ProviderDetails.ResolvedFromOwningPublisher),
                        StringComparison.Ordinal))
                    {
                        hasResolvedColumn = true;
                    }
                }
            }

            int currentVersion;

            if (!hasAnyColumn)
            {
                currentVersion = ProviderDatabaseSchemaVersion.Unknown;
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
                                currentVersion = ProviderDatabaseSchemaVersion.Unknown;
                            }

                            break;
                        }
                }
            }

            var state = new ProviderDatabaseSchemaState(currentVersion);

            _logger?.Debug(
                $"{nameof(EventProviderDbContext)}.{nameof(IsUpgradeNeeded)}() for database {Path}. currentVersion: {currentVersion} needsUpgrade: {state.NeedsUpgrade}");

            return state;
        }
        finally
        {
            Database.CloseConnection();
        }
    }

    public void PerformUpgradeIfNeeded()
    {
        var state = IsUpgradeNeeded();

        if (!state.NeedsUpgrade) { return; }

        if (state.CurrentVersion == ProviderDatabaseSchemaVersion.Unknown)
        {
            throw new DatabaseUpgradeException(
                Path,
                DatabaseSchemaMessages.UnrecognizedSchema(DatabaseSchemaMessages.DefaultLabel, Path));
        }

        if (state.CurrentVersion is 1 or 2)
        {
            throw new DatabaseUpgradeException(
                Path,
                DatabaseSchemaMessages.UnsupportedV1OrV2Schema(Path, state.CurrentVersion));
        }

        var size = new FileInfo(Path).Length;

        _logger?.Info(
            $"EventProviderDbContext upgrading database (current v{state.CurrentVersion} → v{ProviderDatabaseSchemaVersion.Current}). Size: {size} Path: {Path}");

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

        _logger?.Info($"EventProviderDbContext upgrade completed. Size: {size} Path: {Path}");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite($"Data Source={Path};Mode={(_readOnly ? "ReadOnly" : "ReadWriteCreate")}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderDetails>()
            .HasKey(e => e.ProviderName);

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.ProviderName)
            .UseCollation("NOCASE");

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Messages)
            .HasConversion<CompressedJsonValueConverter<IReadOnlyList<MessageModel>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Parameters)
            .HasConversion<CompressedJsonValueConverter<IEnumerable<MessageModel>>>();

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
    }

    private static bool IsType(string? actual, string expected) =>
        string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static ProviderDetails ReadCompressedRow(IDataReader reader, string providerName) =>
        new()
        {
            ProviderName = providerName,
            Messages = CompressedJsonValueConverter<List<MessageModel>>.ConvertFromCompressedJson((byte[])reader["Messages"]),
            Parameters = CompressedJsonValueConverter<List<MessageModel>>.ConvertFromCompressedJson((byte[])reader["Parameters"]),
            Events = CompressedJsonValueConverter<List<EventModel>>.ConvertFromCompressedJson((byte[])reader["Events"]),
            Keywords = CompressedJsonValueConverter<Dictionary<long, string>>.ConvertFromCompressedJson((byte[])reader["Keywords"]),
            Opcodes = CompressedJsonValueConverter<Dictionary<int, string>>.ConvertFromCompressedJson((byte[])reader["Opcodes"]),
            Tasks = CompressedJsonValueConverter<Dictionary<int, string>>.ConvertFromCompressedJson((byte[])reader["Tasks"]),
            ResolvedFromOwningPublisher = TryReadResolvedFromOwningPublisher(reader, providerName)
        };

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

            if (!string.Equals(columnName, nameof(Providers.ProviderDetails.ProviderName), StringComparison.Ordinal))
            {
                continue;
            }

            var collation = indexInfoReader["coll"]?.ToString();

            return string.Equals(collation, "NOCASE", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string? TryReadResolvedFromOwningPublisher(IDataReader reader, string providerName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (!string.Equals(reader.GetName(i),
                nameof(Providers.ProviderDetails.ResolvedFromOwningPublisher),
                StringComparison.Ordinal))
            {
                continue;
            }

            return reader.IsDBNull(i) ? null : reader.GetString(i);
        }

        _ = providerName;

        return null;
    }
}
