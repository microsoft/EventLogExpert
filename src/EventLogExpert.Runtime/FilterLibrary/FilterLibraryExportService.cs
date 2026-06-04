// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
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
                incoming = document.RootElement.Deserialize<List<LibraryEntry>>() ?? [];
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
        var existingByFilterSetKey = existing
            .OfType<LibraryEntryFilterSet>()
            .ToDictionary(FilterLibraryDedupKeys.ForFilterSet, LibraryEntry (e) => e);

        var existingBySavedFilterKey = existing
            .OfType<LibraryEntrySavedFilter>()
            .ToDictionary(FilterLibraryDedupKeys.ForSavedFilter, LibraryEntry (e) => e);

        var existingByNameLower = existing
            .GroupBy(e => e.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var toAdd = new List<LibraryEntry>();
        var toReplace = new List<(LibraryEntry Existing, LibraryEntry Incoming)>();
        var skipped = new List<LibraryEntry>();

        foreach (var entry in incoming)
        {
            if (IsExactDuplicate(entry, existingByFilterSetKey, existingBySavedFilterKey))
            {
                skipped.Add(entry);

                continue;
            }

            if (existingByNameLower.TryGetValue(entry.Name.ToLowerInvariant(), out var nameMatch))
            {
                toReplace.Add((nameMatch, entry));

                continue;
            }

            toAdd.Add(entry);
        }

        return new ImportPreflight(toAdd, toReplace, skipped);
    }

    private static bool IsExactDuplicate(
        LibraryEntry entry,
        IReadOnlyDictionary<string, LibraryEntry> existingByFilterSetKey,
        IReadOnlyDictionary<(string, FilterMode, bool), LibraryEntry> existingBySavedFilterKey)
    {
        return entry switch
        {
            LibraryEntryFilterSet fs => existingByFilterSetKey.ContainsKey(
                FilterLibraryDedupKeys.ForFilterSet(fs)),
            LibraryEntrySavedFilter sf => existingBySavedFilterKey.ContainsKey(
                FilterLibraryDedupKeys.ForSavedFilter(sf)),
            _ => false,
        };
    }

    private sealed record ExportEnvelope(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("entries")] IReadOnlyList<LibraryEntry> Entries);
}
