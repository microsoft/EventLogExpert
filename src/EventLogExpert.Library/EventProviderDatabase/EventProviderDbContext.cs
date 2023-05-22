// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Library.Providers;
using Microsoft.EntityFrameworkCore;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using System.Text.Json;

namespace EventLogExpert.Library.EventProviderDatabase;

public class EventProviderDbContext : DbContext
{
    private readonly bool _readOnly;

    private readonly Action<string> _tracer;

    public EventProviderDbContext(string path, bool readOnly, Action<string>? tracer = null)
    {
        if (tracer != null)
        {
            _tracer = tracer;
        }
        else
        {
            _tracer = s => { };
        }

        _tracer($"Instantiating EventProviderDbContext. path: {path} readOnly: {readOnly}");

        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Path = path;
        _readOnly = readOnly;

        Database.EnsureCreated();
    }

    public string Name { get; }

    public string Path { get; }

    public DbSet<ProviderDetails> ProviderDetails { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={Path};Mode={(_readOnly ? "ReadOnly" : "ReadWriteCreate")}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderDetails>()
            .HasKey(e => e.ProviderName);

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Messages)
            .HasConversion<CompressedJsonValueConverter<List<MessageModel>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Events)
            .HasConversion<CompressedJsonValueConverter<List<EventModel>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Keywords)
            .HasConversion<CompressedJsonValueConverter<Dictionary<long, string>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Opcodes)
            .HasConversion<CompressedJsonValueConverter<Dictionary<int, string>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Tasks)
            .HasConversion<CompressedJsonValueConverter<Dictionary<int, string>>>();
    }

    public bool IsUpgradeNeeded()
    {
        var connection = Database.GetDbConnection();
        Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM sqlite_schema";
        using var reader = command.ExecuteReader();
        var needsUpgrade = false;
        while (reader.Read())
        {
            var val = reader["sql"]?.ToString();
            if (val?.Contains("\"Messages\" TEXT NOT NULL") ?? false)
            {
                needsUpgrade = true;
                break;
            }
        }

        reader.Close();

        _tracer($"EventProviderDbContext.IsUpgradeNeeded() returning {needsUpgrade} for database {Path}");

        return needsUpgrade;
    }

    public void PerformUpgradeIfNeeded()
    {
        if (!IsUpgradeNeeded())
        {
            return;
        }

        var size = new FileInfo(Path).Length;

        _tracer($"EventProviderDbContext upgrading database. Size: {size} Path: {Path}");

        var connection = Database.GetDbConnection();
        Database.OpenConnection();
        using var command = connection.CreateCommand();

        var allProviderDetails = new List<ProviderDetails>();

        command.CommandText = "SELECT * FROM \"ProviderDetails\"";
        var detailsReader = command.ExecuteReader();
        while (detailsReader.Read())
        {
            var p = new ProviderDetails
            {
                ProviderName = (string)detailsReader["ProviderName"],
                Messages = JsonSerializer.Deserialize<List<MessageModel>>((string)detailsReader["Messages"]),
                Events = JsonSerializer.Deserialize<List<EventModel>>((string)detailsReader["Events"]),
                Keywords = JsonSerializer.Deserialize<Dictionary<long, string>>((string)detailsReader["Keywords"]),
                Opcodes = JsonSerializer.Deserialize<Dictionary<int, string>>((string)detailsReader["Opcodes"]),
                Tasks = JsonSerializer.Deserialize<Dictionary<int, string>>((string)detailsReader["Tasks"])
            };
            allProviderDetails.Add(p);
        }

        detailsReader.Close();

        command.CommandText = "DROP TABLE \"ProviderDetails\"";
        command.ExecuteNonQuery();
        command.CommandText = "VACUUM";
        command.ExecuteNonQuery();

        Database.EnsureCreated();

        foreach (var p in allProviderDetails)
        {
            ProviderDetails.Add(p);
        }

        SaveChanges();

        size = new FileInfo(Path).Length;

        _tracer($"EventProviderDbContext upgrade completed. Size: {size} Path: {Path}");
    }
}
