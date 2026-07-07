// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Emit;

/// <summary>
///     Case-insensitive <c>*</c> glob matcher for field names and UserData storage-key paths. <c>*</c> matches any
///     run of characters (including empty); every other character is literal. A pattern is compiled once (at emit time)
///     into a reusable match delegate so the per-event hot path parses nothing and allocates nothing.
/// </summary>
internal static class WildcardMatcher
{
    /// <summary>Compiles a <c>*</c> glob into a case-insensitive matcher. Call once per emitted term and reuse it.</summary>
    internal static Func<string, bool> Compile(string pattern)
    {
        // Literal runs split by the '*' wildcards: the first run anchors the start, the last anchors the end, and the
        // middle runs must appear in order between them. An empty run (a leading, trailing, or doubled '*') is a no-op.
        string[] segments = pattern.Split('*');

        return candidate => IsMatch(candidate, segments);
    }

    /// <summary><c>true</c> when the pattern carries at least one <c>*</c> wildcard (so it is a glob, not an exact name).</summary>
    internal static bool ContainsWildcard(string pattern) => pattern.Contains('*');

    private static bool IsMatch(string candidate, string[] segments)
    {
        const StringComparison OrdinalIgnoreCase = StringComparison.OrdinalIgnoreCase;
        ReadOnlySpan<char> span = candidate;

        // No wildcard: whole-string equality (defensive; callers gate on ContainsWildcard).
        if (segments.Length == 1) { return span.Equals(segments[0], OrdinalIgnoreCase); }

        int start = 0;
        int end = span.Length;
        string prefix = segments[0];

        if (prefix.Length > 0)
        {
            if (!span.StartsWith(prefix, OrdinalIgnoreCase)) { return false; }

            start = prefix.Length;
        }

        string suffix = segments[^1];

        if (suffix.Length > 0)
        {
            if (!span.EndsWith(suffix, OrdinalIgnoreCase)) { return false; }

            end -= suffix.Length;
        }

        for (int index = 1; index < segments.Length - 1; index++)
        {
            string segment = segments[index];

            if (segment.Length == 0) { continue; }

            if (start > end) { return false; }

            int found = span[start..end].IndexOf(segment, OrdinalIgnoreCase);

            if (found < 0) { return false; }

            start += found + segment.Length;
        }

        return start <= end;
    }
}
