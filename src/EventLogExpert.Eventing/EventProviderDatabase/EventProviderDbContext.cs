// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace EventLogExpert.Eventing.EventProviderDatabase;

public sealed class EventProviderDbContext : DbContext
{
    private readonly ITraceLogger? _logger;
    private readonly bool _readOnly;

    public EventProviderDbContext(string path, bool readOnly, ITraceLogger? logger = null)
    {
        _logger = logger;

        _logger?.Debug($"Instantiating EventProviderDbContext. path: {path} readOnly: {readOnly}");

        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Path = path;
        _readOnly = readOnly;

        Database.EnsureCreated();
    }

    public string Name { get; }

    public DbSet<ProviderDetails> ProviderDetails { get; set; }

    private string Path { get; }

    public (bool needsV2Upgrade, bool needsV3Upgrade) IsUpgradeNeeded()
    {
        // Use PRAGMA table_info instead of substring-matching sqlite_schema.sql. The previous
        // approach matched the literal text `"Parameters" BLOB NOT NULL`, which is brittle across
        // EF Core / Microsoft.Data.Sqlite versions (whitespace, quoting, constraint ordering, case)
        // and would silently flip a fresh V3 database into the upgrade-needed path, causing an
        // unnecessary drop/vacuum/recreate cycle.
        var connection = Database.GetDbConnection();
        Database.OpenConnection();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(\"ProviderDetails\")";
            using var reader = command.ExecuteReader();

            string? messagesType = null;
            string? parametersType = null;
            var hasAnyColumn = false;

            while (reader.Read())
            {
                hasAnyColumn = true;

                var name = reader["name"]?.ToString();
                var type = reader["type"]?.ToString();

                if (string.Equals(name, "Messages", StringComparison.Ordinal))
                {
                    messagesType = type;
                }
                else if (string.Equals(name, "Parameters", StringComparison.Ordinal))
                {
                    parametersType = type;
                }
            }

            reader.Close();

            // V2 schema stored payload columns as JSON-encoded TEXT. V3 stores them as compressed BLOB.
            // V1 had no Parameters column at all; that case naturally falls into needsV3Upgrade=true
            // because parametersType remains null below.
            var messagesIsText = string.Equals(messagesType?.Trim(), "TEXT", StringComparison.OrdinalIgnoreCase);
            var parametersIsBlob = string.Equals(parametersType?.Trim(), "BLOB", StringComparison.OrdinalIgnoreCase);

            // Only flag upgrades when the ProviderDetails table actually exists. If it does not, the
            // database is either freshly created (EnsureCreated already ran in the constructor) or
            // unrelated to this tool — neither case warrants the destructive drop/vacuum/recreate path.
            var needsV2Upgrade = hasAnyColumn && messagesIsText;
            var needsV3Upgrade = hasAnyColumn && !parametersIsBlob;

            _logger?.Debug($"{nameof(EventProviderDbContext)}.{nameof(IsUpgradeNeeded)}() for database {Path}. needsV2Upgrade: {needsV2Upgrade} needsV3Upgrade: {needsV3Upgrade}");

            return (needsV2Upgrade, needsV3Upgrade);
        }
        finally
        {
            // Always close the connection we opened explicitly — leaving it open keeps the SQLite
            // file locked, blocking other operations (including FileInfo.Length reads on some
            // platforms) and undermining EF's normal short-lived-connection lifecycle.
            Database.CloseConnection();
        }
    }

    public void PerformUpgradeIfNeeded()
    {
        var (needsV2Upgrade, needsV3Upgrade) = IsUpgradeNeeded();

        if (!needsV2Upgrade && !needsV3Upgrade)
        {
            return;
        }

        var size = new FileInfo(Path).Length;

        _logger?.Info($"EventProviderDbContext upgrading database. Size: {size} Path: {Path}");

        var connection = Database.GetDbConnection();
        Database.OpenConnection();

        var allProviderDetails = new List<ProviderDetails>();

        try
        {
            using var command = connection.CreateCommand();

            command.CommandText = "SELECT * FROM \"ProviderDetails\"";

            using (var detailsReader = command.ExecuteReader())
            {
                if (needsV2Upgrade)
                {
                    while (detailsReader.Read())
                    {
                        var providerName = (string)detailsReader["ProviderName"];
                        var p = new ProviderDetails
                        {
                            ProviderName = providerName,
                            Messages = JsonSerializer.Deserialize<List<MessageModel>>((string)detailsReader["Messages"], ProviderJsonSerializerOptions.Default) ?? new List<MessageModel>(),
                            Parameters = TryReadParametersJson(detailsReader, providerName),
                            Events = JsonSerializer.Deserialize<List<EventModel>>((string)detailsReader["Events"], ProviderJsonSerializerOptions.Default) ?? new List<EventModel>(),
                            Keywords = JsonSerializer.Deserialize<Dictionary<long, string>>((string)detailsReader["Keywords"], ProviderJsonSerializerOptions.Default) ?? new Dictionary<long, string>(),
                            Opcodes = JsonSerializer.Deserialize<Dictionary<int, string>>((string)detailsReader["Opcodes"], ProviderJsonSerializerOptions.Default) ?? new Dictionary<int, string>(),
                            Tasks = JsonSerializer.Deserialize<Dictionary<int, string>>((string)detailsReader["Tasks"], ProviderJsonSerializerOptions.Default) ?? new Dictionary<int, string>()
                        };
                        allProviderDetails.Add(p);
                    }
                }
                else
                {
                    while (detailsReader.Read())
                    {
                        var providerName = (string)detailsReader["ProviderName"];
                        var p = new ProviderDetails
                        {
                            ProviderName = providerName,
                            Messages = CompressedJsonValueConverter<List<MessageModel>>.ConvertFromCompressedJson((byte[])detailsReader["Messages"]) ?? new List<MessageModel>(),
                            Parameters = TryReadParametersJson(detailsReader, providerName),
                            Events = CompressedJsonValueConverter<List<EventModel>>.ConvertFromCompressedJson((byte[])detailsReader["Events"]) ?? new List<EventModel>(),
                            Keywords = CompressedJsonValueConverter<Dictionary<long, string>>.ConvertFromCompressedJson((byte[])detailsReader["Keywords"]) ?? new Dictionary<long, string>(),
                            Opcodes = CompressedJsonValueConverter<Dictionary<int, string>>.ConvertFromCompressedJson((byte[])detailsReader["Opcodes"]) ?? new Dictionary<int, string>(),
                            Tasks = CompressedJsonValueConverter<Dictionary<int, string>>.ConvertFromCompressedJson((byte[])detailsReader["Tasks"]) ?? new Dictionary<int, string>()
                        };
                        allProviderDetails.Add(p);
                    }
                }
            }

            command.CommandText = "DROP TABLE \"ProviderDetails\"";
            command.ExecuteNonQuery();
            command.CommandText = "VACUUM";
            command.ExecuteNonQuery();
        }
        finally
        {
            // SaveChanges() below will reopen the connection on demand; releasing it here keeps the
            // explicit open/close balanced and prevents leaking the lock if SaveChanges throws.
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

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={Path};Mode={(_readOnly ? "ReadOnly" : "ReadWriteCreate")}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderDetails>()
            .HasKey(e => e.ProviderName);

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

    /// <summary>
    ///     Reads the optional <c>Parameters</c> column from the pre-upgrade row, if present, and
    ///     deserializes the JSON payload. V1 schemas have no Parameters column at all (returns empty
    ///     list); V2 stored it as JSON-encoded TEXT (preserved here). Any failure to parse logs a
    ///     warning so silent data loss is diagnosable instead of opaque.
    /// </summary>
    private List<MessageModel> TryReadParametersJson(IDataReader reader, string providerName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (!string.Equals(reader.GetName(i), "Parameters", StringComparison.Ordinal))
            {
                continue;
            }

            if (reader.IsDBNull(i))
            {
                return [];
            }

            var raw = reader.GetValue(i);

            if (raw is string s)
            {
                if (string.IsNullOrEmpty(s)) { return []; }

                try
                {
                    return JsonSerializer.Deserialize<List<MessageModel>>(s, ProviderJsonSerializerOptions.Default) ?? [];
                }
                catch (JsonException ex)
                {
                    _logger?.Warn($"EventProviderDbContext upgrade: failed to deserialize Parameters JSON for provider '{providerName}' in {Path}: {ex.Message}. Parameters will be empty after upgrade.");
                    return [];
                }
            }

            _logger?.Warn($"EventProviderDbContext upgrade: Parameters column for provider '{providerName}' in {Path} is of unexpected type '{raw.GetType().Name}'. Parameters will be empty after upgrade.");
            return [];
        }

        return [];
    }
}
