// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Microsoft.EntityFrameworkCore;

namespace EventLogExpert.Library.EventDatabase;

public class EventDbContext : DbContext
{
    private readonly bool _readOnly;

    public EventDbContext(string path, bool readOnly)
    {
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Path = path;
        _readOnly = readOnly;

        Database.EnsureCreated();
    }

    public string Name { get; }

    public string Path { get; }

    public DbSet<MessageModel> Messages { get; set; }

    public DbSet<DbEventModel> Events { get; set; }

    public DbSet<DbValueName> Keywords { get; set; }

    public DbSet<DbValueName> Opcodes { get; set; }

    public DbSet<DbValueName> Tasks { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={Path};Mode={(_readOnly ? "ReadOnly" : "ReadWriteCreate")}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbEventModel>()
            .Property(e => e.Keywords)
            .HasConversion<ArrayOfLongConverter>();

        modelBuilder.Entity<DbEventModel>()
            .HasKey(e => e.DbId);

        modelBuilder.Entity<DbEventModel>()
            .HasIndex(e => new { e.ProviderName, e.Id });

        modelBuilder.Entity<MessageModel>()
            .HasKey(e => new { e.ProviderName, e.RawId });

        modelBuilder.Entity<MessageModel>()
            .HasIndex(e => new { e.ProviderName, e.ShortId });

        modelBuilder.Entity<DbValueName>()
            .HasKey(e => e.DbId);

        modelBuilder.Entity<DbValueName>()
            .HasIndex(e => new { e.ProviderName, e.Value });
    }
}
