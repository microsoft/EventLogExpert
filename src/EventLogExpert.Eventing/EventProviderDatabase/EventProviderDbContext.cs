﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EventLogExpert.Eventing.EventProviderDatabase;

public class EventProviderDbContext : DbContext
{
    private readonly bool _readOnly;

    private readonly ITraceLogger? _logger;

    public EventProviderDbContext(string path, bool readOnly, ITraceLogger? logger = null)
    {
        _logger = logger;

        _logger?.Trace($"Instantiating EventProviderDbContext. path: {path} readOnly: {readOnly}");

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
            .Property(e => e.Parameters)
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

    public (bool needsV2Upgrade, bool needsV3Upgrade) IsUpgradeNeeded()
    {
        var connection = Database.GetDbConnection();
        Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM sqlite_schema";
        using var reader = command.ExecuteReader();
        var needsV2Upgrade = false;
        var needsV3Upgrade = true;
        while (reader.Read())
        {
            var val = reader["sql"]?.ToString();
            if (val?.Contains("\"Messages\" TEXT NOT NULL") ?? false)
            {
                needsV2Upgrade = true;
            }

            if (val?.Contains("\"Parameters\" BLOB NOT NULL") ?? false)
            {
                needsV3Upgrade = false;
            }
        }

        reader.Close();

        var needsUpgrade = needsV2Upgrade || needsV3Upgrade;

        _logger?.Trace($"{nameof(EventProviderDbContext)}.{nameof(IsUpgradeNeeded)}() for database {Path}. needsV2Upgrade: {needsV2Upgrade} needsV3Upgrade: {needsV3Upgrade}");

        return (needsV2Upgrade, needsV3Upgrade);
    }

    public void PerformUpgradeIfNeeded()
    {
        var (needsV2Upgrade, needsV3Upgrade) = IsUpgradeNeeded();

        if (!needsV2Upgrade && !needsV3Upgrade)
        {
            return;
        }

        var size = new FileInfo(Path).Length;

        _logger?.Trace($"EventProviderDbContext upgrading database. Size: {size} Path: {Path}");

        var connection = Database.GetDbConnection();
        Database.OpenConnection();
        using var command = connection.CreateCommand();

        var allProviderDetails = new List<ProviderDetails>();

        command.CommandText = "SELECT * FROM \"ProviderDetails\"";
        var detailsReader = command.ExecuteReader();

        if (needsV2Upgrade)
        {
            while (detailsReader.Read())
            {
                var p = new ProviderDetails
                {
                    ProviderName = (string)detailsReader["ProviderName"],
                    Messages = JsonSerializer.Deserialize<List<MessageModel>>((string)detailsReader["Messages"]) ?? new List<MessageModel>(),
                    Parameters = new List<MessageModel>(),
                    Events = JsonSerializer.Deserialize<List<EventModel>>((string)detailsReader["Events"]) ?? new List<EventModel>(),
                    Keywords = JsonSerializer.Deserialize<Dictionary<long, string>>((string)detailsReader["Keywords"]) ?? new Dictionary<long, string>(),
                    Opcodes = JsonSerializer.Deserialize<Dictionary<int, string>>((string)detailsReader["Opcodes"]) ?? new Dictionary<int, string>(),
                    Tasks = JsonSerializer.Deserialize<Dictionary<int, string>>((string)detailsReader["Tasks"]) ?? new Dictionary<int, string>()
                };
                allProviderDetails.Add(p);
            }

            detailsReader.Close();
        }
        else
        {
            while (detailsReader.Read())
            {
                var p = new ProviderDetails
                {
                    ProviderName = (string)detailsReader["ProviderName"],
                    Messages = CompressedJsonValueConverter<List<MessageModel>>.ConvertFromCompressedJson((byte[])detailsReader["Messages"]) ?? new List<MessageModel>(),
                    Parameters = new List<MessageModel>(),
                    Events = CompressedJsonValueConverter<List<EventModel>>.ConvertFromCompressedJson((byte[])detailsReader["Events"]) ?? new List<EventModel>(),
                    Keywords = CompressedJsonValueConverter<Dictionary<long, string>>.ConvertFromCompressedJson((byte[])detailsReader["Keywords"]) ?? new Dictionary<long, string>(),
                    Opcodes = CompressedJsonValueConverter<Dictionary<int, string>>.ConvertFromCompressedJson((byte[])detailsReader["Opcodes"]) ?? new Dictionary<int, string>(),
                    Tasks = CompressedJsonValueConverter<Dictionary<int, string>>.ConvertFromCompressedJson((byte[])detailsReader["Tasks"]) ?? new Dictionary<int, string>()
                };
                allProviderDetails.Add(p);
            }

            detailsReader.Close();
        }

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

        _logger?.Trace($"EventProviderDbContext upgrade completed. Size: {size} Path: {Path}");
    }
}
