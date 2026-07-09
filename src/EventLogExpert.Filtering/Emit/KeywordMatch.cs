// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Emit;

/// <summary>
///     The shared <c>Keywords.Any(…)</c> match cores, factored out of <see cref="Emitter" /> so the row backend and
///     the column-direct backend evaluate an identical keyword list the same way. Each method takes the resolved keyword
///     list (an <see cref="IReadOnlyList{T}" /> of <see cref="string" /> on both backends) so the loop is written once.
/// </summary>
internal static class KeywordMatch
{
    /// <summary><c>Keywords.Any(e =&gt; e.Contains(needle, comparison))</c>.</summary>
    internal static bool AnyContains(IReadOnlyList<string> keywords, string needle, StringComparison comparison)
    {
        for (var index = 0; index < keywords.Count; index++)
        {
            if (keywords[index].Contains(needle, comparison)) { return true; }
        }

        return false;
    }

    /// <summary><c>Keywords.Any(e =&gt; string.Equals(e, needle, comparison))</c>.</summary>
    internal static bool AnyEquals(IReadOnlyList<string> keywords, string needle, StringComparison comparison)
    {
        for (var index = 0; index < keywords.Count; index++)
        {
            if (string.Equals(keywords[index], needle, comparison)) { return true; }
        }

        return false;
    }

    /// <summary><c>(new[] {…}).Contains(e)</c> over any keyword — ordinal equality, matching the row backend.</summary>
    internal static bool MatchAnyOf(IReadOnlyList<string> keywords, ReadOnlySpan<string> needles)
    {
        for (var index = 0; index < keywords.Count; index++)
        {
            var keyword = keywords[index];

            for (var needleIndex = 0; needleIndex < needles.Length; needleIndex++)
            {
                if (string.Equals(keyword, needles[needleIndex], StringComparison.Ordinal)) { return true; }
            }
        }

        return false;
    }
}
