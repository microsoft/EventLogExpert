// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class FilterLibraryExportService : IFilterLibraryExportService
{
    internal const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    public ImportPreflight Deserialize(string json, IReadOnlyList<LibraryEntry> existingEntries)
    {
        ArgumentNullException.ThrowIfNull(existingEntries);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new ImportPreflight([], [], [], error: "Import file is empty.");
        }

        IReadOnlyList<LibraryEntry> incoming;

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("schemaVersion", out var versionElement))
            {
                if (!versionElement.TryGetInt32(out var version))
                {
                    return new ImportPreflight([], [], [], error: "schemaVersion property is not a valid integer.");
                }

                if (version > CurrentSchemaVersion)
                {
                    return new ImportPreflight([], [], [], error:
                        $"Unsupported schema version {version}. Please upgrade the application.");
                }

                if (version < 1)
                {
                    return new ImportPreflight([], [], [], error:
                        $"Invalid schema version {version}. Expected 1.");
                }

                if (!document.RootElement.TryGetProperty("entries", out var entriesElement))
                {
                    return new ImportPreflight([], [], [], error: "Missing 'entries' property.");
                }

                incoming = entriesElement.Deserialize<List<LibraryEntry>>() ?? [];
            }
            else
            {
                incoming = ReadBareArrayWithLegacyFallback(document.RootElement)
                    ?? throw new JsonException("Unsupported import file shape.");
            }
        }
        catch (JsonException ex)
        {
            return new ImportPreflight([], [], [], error: ex.Message);
        }

        return ComputePreflight(incoming, existingEntries);
    }

    public string Serialize(IReadOnlyList<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var envelope = new ExportEnvelope(CurrentSchemaVersion, entries);

        return JsonSerializer.Serialize(envelope, s_writeOptions);
    }

    private static ImportPreflight ComputePreflight(
        IReadOnlyList<LibraryEntry> incoming,
        IReadOnlyList<LibraryEntry> existing)
    {
        var existingFilterSetKeys = new HashSet<string>(
            existing.OfType<LibraryEntryFilterSet>().Select(FilterLibraryDedupKeys.ForFilterSet));

        var existingSavedFilterKeys = new HashSet<(string, FilterMode, bool)>(
            existing.OfType<LibraryEntrySavedFilter>().Select(FilterLibraryDedupKeys.ForSavedFilter));

        var existingByNameLower = new Dictionary<string, LibraryEntry>();
        foreach (var entry in existing)
        {
            var key = entry.Name.ToLowerInvariant();
            existingByNameLower.TryAdd(key, entry);
        }

        var existingIds = new HashSet<LibraryEntryId>(existing.Select(e => e.Id));
        var toAdd = new List<LibraryEntry>();
        var toReplace = new List<(LibraryEntry Existing, LibraryEntry Incoming)>();
        var skipped = new List<LibraryEntry>();
        var incomingNamesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in incoming)
        {
            if (IsExactDuplicate(entry, existingFilterSetKeys, existingSavedFilterKeys) || !incomingNamesSeen.Add(entry.Name))
            {
                skipped.Add(entry);

                continue;
            }

            if (existingByNameLower.TryGetValue(entry.Name.ToLowerInvariant(), out var nameMatch))
            {
                toReplace.Add((nameMatch, entry));

                continue;
            }

            var addEntry = existingIds.Contains(entry.Id) ? entry with { Id = LibraryEntryId.Create() } : entry;
            toAdd.Add(addEntry);
        }

        return new ImportPreflight(toAdd, toReplace, skipped);
    }

    private static bool IsExactDuplicate(
        LibraryEntry entry,
        IReadOnlySet<string> existingFilterSetKeys,
        IReadOnlySet<(string, FilterMode, bool)> existingSavedFilterKeys)
    {
        return entry switch
        {
            LibraryEntryFilterSet fs => existingFilterSetKeys.Contains(
                FilterLibraryDedupKeys.ForFilterSet(fs)),
            LibraryEntrySavedFilter sf => existingSavedFilterKeys.Contains(
                FilterLibraryDedupKeys.ForSavedFilter(sf)),
            _ => false,
        };
    }

    private static IReadOnlyList<LibraryEntry>? ReadBareArrayWithLegacyFallback(JsonElement root)
    {
        try
        {
            return root.Deserialize<List<LibraryEntry>>() ?? [];
        }
        catch (NotSupportedException)
        {
            // The bare-array shape is missing the polymorphic discriminator ("Kind") that LibraryEntry
            // requires. Fall back to the legacy SavedFilterGroup[] shape produced by the pre-FilterLibrary
            // FilterGroupModal export (this is the only legacy export shape we promise to read).
            var legacyGroups = root.Deserialize<List<SavedFilterGroup>>();
            if (legacyGroups is null) { return null; }

            var now = DateTimeOffset.UtcNow;
            var converted = new List<LibraryEntry>(legacyGroups.Count);

            foreach (var group in legacyGroups.Where(g => g.Filters.Count > 0))
            {
                converted.Add(new LibraryEntryFilterSet
                {
                    Id = LibraryEntryId.Create(),
                    Name = string.IsNullOrWhiteSpace(group.Name) ? "(unnamed)" : group.Name,
                    CreatedUtc = now,
                    Filters = [.. group.Filters.Select(f => f with { Id = FilterId.Create(), IsEnabled = false })],
                    IsFavorite = false,
                    LastUsedUtc = null,
                    Origin = LibraryEntryOrigin.UserSaved,
                });
            }

            return converted;
        }
    }

    private sealed record ExportEnvelope(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("entries")] IReadOnlyList<LibraryEntry> Entries);
}
