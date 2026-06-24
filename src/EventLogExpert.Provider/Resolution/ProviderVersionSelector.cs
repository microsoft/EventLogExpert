// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Resolution;

public static class ProviderVersionSelector
{
    /// <summary>
    ///     Returns null when <paramref name="candidates" /> is empty, the only candidate when there is one (unchanged,
    ///     same reference), or the most-complete version when several coexist.
    /// </summary>
    public static ProviderDetails? SelectMostComplete(IReadOnlyList<ProviderDetails> candidates)
    {
        if (candidates.Count == 0) { return null; }

        if (candidates.Count == 1) { return candidates[0]; }

        var best = candidates[0];
        var bestScore = ScoreOf(best);

        for (var index = 1; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var score = ScoreOf(candidate);

            var comparison = score.CompareTo(bestScore);

            // Completeness is primary; recency (newest source) breaks completeness ties so a newer-but-empty capture
            // never beats an older-but-populated one. A full tie keeps the current best, so the newest-first load
            // order remains the final deterministic tiebreak.
            if (comparison == 0) { comparison = CompareRecency(candidate, best); }

            if (comparison > 0)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static int CompareFileVersion(string? candidate, string? current)
    {
        var candidateVersion = TryParseVersion(candidate);
        var currentVersion = TryParseVersion(current);

        if (candidateVersion is not null && currentVersion is not null)
        {
            return candidateVersion.CompareTo(currentVersion);
        }

        if (candidateVersion is not null) { return 1; }

        return currentVersion is not null ? -1 : 0;
    }

    private static int CompareNullableInt(int? candidate, int? current)
    {
        if (candidate.HasValue && current.HasValue) { return candidate.Value.CompareTo(current.Value); }

        if (candidate.HasValue) { return 1; }

        return current.HasValue ? -1 : 0;
    }

    private static int CompareRecency(ProviderDetails candidate, ProviderDetails current)
    {
        var byBuild = CompareNullableInt(candidate.SourceOsBuild, current.SourceOsBuild);

        if (byBuild != 0) { return byBuild; }

        var byRevision = CompareNullableInt(candidate.SourceOsRevision, current.SourceOsRevision);

        return byRevision != 0 ?
            byRevision :
            CompareFileVersion(candidate.MessageFileVersion, current.MessageFileVersion);
    }

    private static CompletenessScore ScoreOf(ProviderDetails provider)
    {
        var nonEmptyDescriptions = 0;
        long totalDescriptionLength = 0;

        foreach (var @event in provider.Events)
        {
            var description = @event.Description;

            if (string.IsNullOrEmpty(description)) { continue; }

            nonEmptyDescriptions++;
            totalDescriptionLength += description.Length;
        }

        return new CompletenessScore(nonEmptyDescriptions, totalDescriptionLength, provider.MessageSource?.Count ?? 0);
    }

    private static Version? TryParseVersion(string? value) => Version.TryParse(value, out var version) ? version : null;

    private readonly record struct CompletenessScore(int NonEmptyDescriptions, long TotalDescriptionLength, int MessageCount)
        : IComparable<CompletenessScore>
    {
        public int CompareTo(CompletenessScore other)
        {
            var byDescriptions = NonEmptyDescriptions.CompareTo(other.NonEmptyDescriptions);

            if (byDescriptions != 0) { return byDescriptions; }

            var byLength = TotalDescriptionLength.CompareTo(other.TotalDescriptionLength);

            return byLength != 0 ? byLength : MessageCount.CompareTo(other.MessageCount);
        }
    }
}
