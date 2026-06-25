// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;
using EventLogExpert.Provider.Schema;

namespace EventLogExpert.ProviderDatabase.Maintenance;

internal static class ProviderDetailsMerger
{
    public static List<ProviderDetails> MergeCaseInsensitiveDuplicates(
        IReadOnlyList<ProviderDetails> rows,
        string databasePath)
    {
        var merged = new List<ProviderDetails>(rows.Count);
        var firstIndexByGroup = new Dictionary<ProviderIdentity, int>();
        var groupedRows = new Dictionary<ProviderIdentity, List<ProviderDetails>>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var key = ProviderIdentity.Of(row);

            if (firstIndexByGroup.TryAdd(key, i))
            {
                groupedRows[key] = [row];
            }
            else
            {
                groupedRows[key].Add(row);
            }
        }

        foreach (var (_, firstIndex) in firstIndexByGroup.OrderBy(kvp => kvp.Value))
        {
            var first = rows[firstIndex];
            var group = groupedRows[ProviderIdentity.Of(first)];

            if (group.Count == 1)
            {
                merged.Add(group[0]);

                continue;
            }

            merged.Add(MergeGroup(group, databasePath));
        }

        return merged;
    }

    private static int CompareOrdinalNullLowest(string? candidate, string? current)
    {
        if (candidate is null && current is null) { return 0; }

        if (candidate is null) { return -1; }

        return current is null ? 1 : string.CompareOrdinal(candidate, current);
    }

    // A total order over provenance for a deterministic merge pick: recency first (the shared order), then the
    // non-ordinal fields by ordinal string comparison (null lowest), so two rows that tie on recency but differ on
    // edition / display version / raw file-version string still resolve to the same winner regardless of row order.
    private static int CompareProvenanceForMerge(ProviderDetails candidate, ProviderDetails current)
    {
        var byRecency = ProviderSourceRecency.Compare(candidate, current);

        if (byRecency != 0) { return byRecency; }

        var byEdition = CompareOrdinalNullLowest(candidate.SourceOsEdition, current.SourceOsEdition);

        if (byEdition != 0) { return byEdition; }

        var byDisplayVersion = CompareOrdinalNullLowest(candidate.SourceOsDisplayVersion, current.SourceOsDisplayVersion);

        return byDisplayVersion != 0 ?
            byDisplayVersion :
            CompareOrdinalNullLowest(candidate.MessageFileVersion, current.MessageFileVersion);
    }

    // Rows in a group share the same (ProviderName, VersionKey) identity (the grouping key), so the merged row carries
    // that VersionKey forward; only name case-folding differences and duplicate content are collapsed. The identity keys
    // and equivalence rules come from ProviderContentMerge so the database-merge path and the resolve-time multi-version
    // union cannot drift. Here every non-equivalent collision is corruption: the onConflict callbacks throw, which
    // short-circuits the merge exactly as the previous inline loops did.
    private static ProviderDetails MergeGroup(List<ProviderDetails> group, string databasePath)
    {
        var canonicalName = group[0].ProviderName;

        var newestProvenance = SelectNewestProvenanceRow(group);

        return new ProviderDetails
        {
            ProviderName = canonicalName,
            VersionKey = group[0].VersionKey,
            Messages = ProviderContentMerge.Deduplicate(
                group.SelectMany(static row => row.Messages),
                ProviderContentMerge.IdentityOf,
                ProviderContentMerge.MessagesAreEquivalent,
                (_, duplicate) => throw new DatabaseUpgradeException(
                    databasePath,
                    $"Provider '{canonicalName}' has conflicting Message rows for ShortId={duplicate.ShortId}, " +
                    $"RawId={duplicate.RawId}, LogLink='{duplicate.LogLink}', Tag='{duplicate.Tag}'. " +
                    $"Cannot merge case-insensitive duplicates with different Text or Template values.")),
            Parameters = ProviderContentMerge.Deduplicate(
                group.SelectMany(static row => row.Parameters),
                ProviderContentMerge.IdentityOf,
                ProviderContentMerge.MessagesAreEquivalent,
                (_, duplicate) => throw new DatabaseUpgradeException(
                    databasePath,
                    $"Provider '{canonicalName}' has conflicting Message rows for ShortId={duplicate.ShortId}, " +
                    $"RawId={duplicate.RawId}, LogLink='{duplicate.LogLink}', Tag='{duplicate.Tag}'. " +
                    $"Cannot merge case-insensitive duplicates with different Text or Template values.")),
            Events = ProviderContentMerge.Deduplicate(
                group.SelectMany(static row => row.Events),
                ProviderContentMerge.IdentityOf,
                ProviderContentMerge.EventsAreEquivalent,
                (_, duplicate) => throw new DatabaseUpgradeException(
                    databasePath,
                    $"Provider '{canonicalName}' has conflicting Event rows for Id={duplicate.Id}, " +
                    $"Version={duplicate.Version}, LogName='{duplicate.LogName}'. " +
                    $"Cannot merge case-insensitive duplicates with different Level/Opcode/Task/Keywords/Template/Description values.")),
            Keywords = ProviderContentMerge.MergeStringDictionary(
                group.Select(static row => row.Keywords),
                (key, existing, value) => throw new DatabaseUpgradeException(
                    databasePath,
                    $"Provider '{canonicalName}' has conflicting Keywords entries for key {key}: " +
                    $"'{existing}' vs '{value}'.")),
            Opcodes = ProviderContentMerge.MergeStringDictionary(
                group.Select(static row => row.Opcodes),
                (key, existing, value) => throw new DatabaseUpgradeException(
                    databasePath,
                    $"Provider '{canonicalName}' has conflicting Opcodes entries for key {key}: " +
                    $"'{existing}' vs '{value}'.")),
            Tasks = ProviderContentMerge.MergeStringDictionary(
                group.Select(static row => row.Tasks),
                (key, existing, value) => throw new DatabaseUpgradeException(
                    databasePath,
                    $"Provider '{canonicalName}' has conflicting Tasks entries for key {key}: " +
                    $"'{existing}' vs '{value}'.")),
            Maps = ProviderContentMerge.MergeMaps(
                group.Select(static row => row.Maps),
                (name, _, _) => throw new DatabaseUpgradeException(
                    databasePath,
                    $"Provider '{canonicalName}' has conflicting Map entries for '{name}'. " +
                    $"Cannot merge case-insensitive duplicates with different value-map definitions.")),
            ResolvedFromOwningPublisher = ProviderContentMerge.MergeScalarFirstNonEmpty(
                group.Select(static row => row.ResolvedFromOwningPublisher),
                (winner, value) => throw new DatabaseUpgradeException(
                    databasePath,
                    $"Provider '{canonicalName}' has conflicting ResolvedFromOwningPublisher values: " +
                    $"'{winner}' vs '{value}'.")),

            SourceOsBuild = newestProvenance.SourceOsBuild,
            SourceOsRevision = newestProvenance.SourceOsRevision,
            SourceOsEdition = newestProvenance.SourceOsEdition,
            SourceOsDisplayVersion = newestProvenance.SourceOsDisplayVersion,
            MessageFileVersion = newestProvenance.MessageFileVersion
        };
    }

    private static ProviderDetails SelectNewestProvenanceRow(List<ProviderDetails> group)
    {
        var newest = group[0];

        for (var index = 1; index < group.Count; index++)
        {
            if (CompareProvenanceForMerge(group[index], newest) > 0) { newest = group[index]; }
        }

        return newest;
    }
}
