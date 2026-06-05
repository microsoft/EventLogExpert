// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;
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

        try
        {
            using var document = JsonDocument.Parse(json);

            IReadOnlyList<LibraryEntry> incoming;

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

            return incoming.Any(e => e?.Name is null) ?
                new ImportPreflight([], [], [], error: "Import file contains an entry with a missing name.") :
                ComputePreflight(incoming, existingEntries);
        }
        catch (Exception ex)
        {
            return new ImportPreflight([], [], [], error: ex.Message);
        }
    }

    public string Serialize(IReadOnlyList<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var envelope = new ExportEnvelope(CurrentSchemaVersion, entries);

        return JsonSerializer.Serialize(envelope, s_writeOptions);
    }

    private static LibraryEntry CloneWithFreshId(LibraryEntry entry) => entry switch
    {
        LibraryEntrySavedFilter f => f with { Id = LibraryEntryId.Create() },
        LibraryEntryFilterSet fs => fs with { Id = LibraryEntryId.Create() },
        _ => entry,
    };

    private static ImportPreflight ComputePreflight(
        IReadOnlyList<LibraryEntry> incoming,
        IReadOnlyList<LibraryEntry> existing)
    {
        List<LibraryEntry> migratedIncoming;

        if (BackslashMigrationFeature.IsEnabled)
        {
            migratedIncoming = incoming.Select(LibraryEntryTagNormalizer.MigrateBackslashName).ToList();
        }
        else
        {
            var invalidNames = incoming
                .Where(e => e.Name.Contains('\\'))
                .Select(e => e.Name)
                .ToList();

            if (invalidNames.Count > 0)
            {
                return ImportPreflight.Blocked(invalidNames);
            }

            migratedIncoming = incoming.ToList();
        }

        var existingByStrictKey = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
        var existingByRelaxedKey = new Dictionary<string, List<LibraryEntry>>(StringComparer.Ordinal);
        var existingByNameLower = new Dictionary<string, List<LibraryEntry>>(StringComparer.OrdinalIgnoreCase);
        var existingIds = new HashSet<LibraryEntryId>(existing.Select(e => e.Id));

        foreach (var entry in existing)
        {
            var migratedView = LibraryEntryTagNormalizer.MigrateBackslashName(entry);

            var strictKey = DedupKeyStrict(migratedView);
            existingByStrictKey.TryAdd(strictKey, entry);

            var relaxedKey = DedupKeyRelaxed(migratedView);

            if (!existingByRelaxedKey.TryGetValue(relaxedKey, out var relaxedBucket))
            {
                relaxedBucket = [];
                existingByRelaxedKey[relaxedKey] = relaxedBucket;
            }

            relaxedBucket.Add(entry);

            var nameKey = migratedView.Name.ToLowerInvariant();

            if (!existingByNameLower.TryGetValue(nameKey, out var nameBucket))
            {
                nameBucket = [];
                existingByNameLower[nameKey] = nameBucket;
            }

            nameBucket.Add(entry);
        }

        var toAdd = new List<LibraryEntry>();
        var toReplace = new List<(LibraryEntry Existing, LibraryEntry Incoming)>();
        var skipped = new List<LibraryEntry>();
        var toUpdate = new List<(LibraryEntry Existing, LibraryEntry Incoming)>();
        var ambiguous = new List<(IReadOnlyList<LibraryEntry> Candidates, LibraryEntry Incoming)>();
        var incomingFingerprintsSeen = new HashSet<string>(StringComparer.Ordinal);
        var incomingNamesClaimedForReplace = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in migratedIncoming)
        {
            var strictKey = DedupKeyStrict(entry);

            if (!incomingFingerprintsSeen.Add(strictKey) || existingByStrictKey.ContainsKey(strictKey))
            {
                skipped.Add(entry);

                continue;
            }

            var relaxedKey = DedupKeyRelaxed(entry);

            if (existingByRelaxedKey.TryGetValue(relaxedKey, out var relaxedMatches))
            {
                if (relaxedMatches.Count == 1)
                {
                    toUpdate.Add((relaxedMatches[0], entry));
                }
                else
                {
                    ambiguous.Add((relaxedMatches.ToImmutableList(), entry));
                }
                continue;
            }

            if (existingByNameLower.TryGetValue(entry.Name.ToLowerInvariant(), out var nameMatches))
            {
                if (!incomingNamesClaimedForReplace.Add(entry.Name))
                {
                    skipped.Add(entry);

                    continue;
                }

                if (nameMatches.Count == 1)
                {
                    toReplace.Add((nameMatches[0], entry));
                }
                else
                {
                    ambiguous.Add((nameMatches.ToImmutableList(), entry));
                }

                continue;
            }

            var finalEntry = existingIds.Contains(entry.Id)
                ? CloneWithFreshId(entry)
                : entry;
            existingIds.Add(finalEntry.Id);
            toAdd.Add(finalEntry);
        }

        return new ImportPreflight(toAdd, toReplace, skipped, toUpdate, ambiguous);
    }

    private static string DedupKeyRelaxed(LibraryEntry entry) => entry switch
    {
        LibraryEntryFilterSet fs => FilterLibraryDedupKeys.ForFilterSetTagRelaxed(fs),
        LibraryEntrySavedFilter sf => FilterLibraryDedupKeys.ForSavedFilterTagRelaxed(sf),
        _ => throw new NotSupportedException($"Unknown LibraryEntry kind: {entry.GetType().Name}"),
    };

    private static string DedupKeyStrict(LibraryEntry entry) => entry switch
    {
        LibraryEntryFilterSet fs => FilterLibraryDedupKeys.ForFilterSet(fs),
        LibraryEntrySavedFilter sf => FilterLibraryDedupKeys.ForSavedFilter(sf),
        _ => throw new NotSupportedException($"Unknown LibraryEntry kind: {entry.GetType().Name}"),
    };

    private static IReadOnlyList<LibraryEntry>? ReadBareArrayWithLegacyFallback(JsonElement root)
    {
        try
        {
            return root.Deserialize<List<LibraryEntry>>() ?? [];
        }
        catch (NotSupportedException)
        {
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
