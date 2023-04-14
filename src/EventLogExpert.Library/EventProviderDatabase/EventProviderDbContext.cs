// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using EventLogExpert.Library.Providers;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

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
            .HasConversion<JsonValueConverter<List<MessageModel>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Events)
            .HasConversion<JsonValueConverter<List<EventModel>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Keywords)
            .HasConversion<JsonValueConverter<Dictionary<long, string>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Opcodes)
            .HasConversion<JsonValueConverter<Dictionary<int, string>>>();

        modelBuilder.Entity<ProviderDetails>()
            .Property(e => e.Tasks)
            .HasConversion<JsonValueConverter<Dictionary<int, string>>>();
    }
}
