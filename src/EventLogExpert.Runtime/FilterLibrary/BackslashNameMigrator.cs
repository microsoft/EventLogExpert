// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class BackslashNameMigrator(ILegacyPreferences preferences, ITraceLogger logger) : IBackslashNameMigrator
{
    private const string CompletedValue = "1";
    private const string CompletionKey = "filter-library-backslash-migration-state";

    public BackslashMigrationResult BuildMigrationPlan(IReadOnlyList<LibraryEntry> currentEntries)
    {
        ArgumentNullException.ThrowIfNull(currentEntries);

        var entriesNeedingMigration = currentEntries.Where(e => e.Name.Contains('\\')).ToList();
        if (entriesNeedingMigration.Count == 0) { return new BackslashMigrationResult([], 0); }

        var plan = entriesNeedingMigration
            .Select(original => (Original: original, Migrated: LibraryEntryTagNormalizer.MigrateBackslashName(original)))
            .ToList();

        var reservedNames = new HashSet<string>(
            currentEntries
                .Where(e => !e.Name.Contains('\\'))
                .Select(e => e.Name.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        var disambiguated = ImmutableList.CreateBuilder<LibraryEntry>();
        int collisions = 0;

        foreach (var grouped in plan.GroupBy(p => p.Migrated.Name, StringComparer.OrdinalIgnoreCase))
        {
            var groupList = grouped.ToList();

            if (groupList.Count == 1 && !reservedNames.Contains(grouped.Key.ToLowerInvariant()))
            {
                var (_, migrated) = groupList[0];
                disambiguated.Add(migrated);
                reservedNames.Add(migrated.Name.ToLowerInvariant());
                continue;
            }

            int suffix = 1;

            foreach (var (_, migrated) in groupList)
            {
                string candidate;

                do
                {
                    candidate = suffix == 1
                        ? migrated.Name
                        : $"{migrated.Name} ({suffix})";
                    suffix++;
                }
                while (reservedNames.Contains(candidate.ToLowerInvariant()));

                var renamed = migrated switch
                {
                    LibraryEntrySavedFilter f => f with { Name = candidate },
                    LibraryEntryFilterSet fs => fs with { Name = candidate },
                    _ => migrated,
                };

                disambiguated.Add(renamed);
                reservedNames.Add(candidate.ToLowerInvariant());

                if (suffix > 2) { collisions++; }
            }
        }

        if (collisions > 0)
        {
            logger.Information($"Backslash migration disambiguated {collisions} name collision(s) via suffix.");
        }

        return new BackslashMigrationResult(disambiguated.ToImmutable(), collisions);
    }

    public void MarkMigrationCompleted() => preferences.SetString(CompletionKey, CompletedValue);

    public bool ShouldRunMigration()
    {
        var raw = preferences.GetString(CompletionKey);

        return !string.Equals(raw, CompletedValue, StringComparison.Ordinal);
    }
}
