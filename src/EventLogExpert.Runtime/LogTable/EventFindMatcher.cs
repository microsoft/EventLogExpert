// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

public static class EventFindMatcher
{
    public static StringComparison ComparisonFor(bool caseSensitive) =>
        caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static int IndexOfMatch(string text, string query, int startIndex, StringComparison comparison, bool wholeWord)
    {
        if (string.IsNullOrEmpty(query)) { return -1; }

        if (!wholeWord) { return text.IndexOf(query, startIndex, comparison); }

        int from = startIndex;

        while (from <= text.Length)
        {
            int hit = text.IndexOf(query, from, comparison);

            if (hit < 0) { return -1; }

            if (IsWordBounded(text, hit, hit + query.Length)) { return hit; }

            from = hit + 1;
        }

        return -1;
    }

    public static bool RowMatches(
        ResolvedEvent @event,
        IReadOnlyList<ColumnName> columns,
        TimeZoneInfo timeZone,
        string query,
        bool caseSensitive,
        bool wholeWord)
    {
        if (string.IsNullOrEmpty(query)) { return false; }

        StringComparison comparison = ComparisonFor(caseSensitive);

        for (int i = 0; i < columns.Count; i++)
        {
            if (IndexOfMatch(EventTableColumnFormatter.GetCellText(@event, columns[i], timeZone), query, 0, comparison, wholeWord) >= 0)
            {
                return true;
            }
        }

        return IndexOfMatch(@event.Description ?? string.Empty, query, 0, comparison, wholeWord) >= 0;
    }

    // Word boundary = string edge, a non-word neighbor, or the match's own edge char is a separator (VS Code wordSeparators rule; word char = letter/digit/underscore).
    private static bool IsWordBounded(string text, int start, int end) =>
        (start == 0 || !IsWordChar(text[start - 1]) || !IsWordChar(text[start])) &&
        (end == text.Length || !IsWordChar(text[end]) || !IsWordChar(text[end - 1]));

    private static bool IsWordChar(char value) => char.IsLetterOrDigit(value) || value == '_';
}
