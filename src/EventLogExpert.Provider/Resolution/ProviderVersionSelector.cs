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

            // Strictly-greater wins; a tie keeps the current best, so the newest-first load order is the final
            // deterministic tiebreak.
            if (score.CompareTo(bestScore) > 0)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
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
