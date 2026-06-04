// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using System.Text;

namespace EventLogExpert.Runtime.FilterLibrary;

internal static class FilterLibraryDedupKeys
{
    public static string ForFilterSet(LibraryEntryFilterSet filterSet)
    {
        ArgumentNullException.ThrowIfNull(filterSet);

        var sb = new StringBuilder();
        AppendLengthPrefixed(sb, filterSet.Name.ToLowerInvariant());

        foreach (var f in filterSet.Filters)
        {
            AppendLengthPrefixed(sb, f.ComparisonText.ToLowerInvariant());
            sb.Append('|').Append(f.Mode).Append('|').Append(f.IsExcluded);
        }

        AppendSortedTags(sb, filterSet.Tags);

        return sb.ToString();
    }

    public static string ForFilterSetTagRelaxed(LibraryEntryFilterSet filterSet)
    {
        ArgumentNullException.ThrowIfNull(filterSet);

        var sb = new StringBuilder();
        AppendLengthPrefixed(sb, filterSet.Name.ToLowerInvariant());

        foreach (var f in filterSet.Filters)
        {
            AppendLengthPrefixed(sb, f.ComparisonText.ToLowerInvariant());
            sb.Append('|').Append(f.Mode).Append('|').Append(f.IsExcluded);
        }

        return sb.ToString();
    }

    public static (string ComparisonText, FilterMode Mode, bool IsExcluded) ForSavedFilter(
        LibraryEntrySavedFilter entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return (entry.Filter.ComparisonText.ToLowerInvariant(), entry.Filter.Mode, entry.Filter.IsExcluded);
    }

    public static string ForSavedFilterTagRelaxed(LibraryEntrySavedFilter entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var sb = new StringBuilder();
        AppendLengthPrefixed(sb, entry.Name.ToLowerInvariant());
        AppendLengthPrefixed(sb, entry.Filter.ComparisonText.ToLowerInvariant());
        sb.Append('|').Append(entry.Filter.Mode).Append('|').Append(entry.Filter.IsExcluded);

        return sb.ToString();
    }

    private static void AppendLengthPrefixed(StringBuilder sb, string s) =>
        sb.Append(s.Length).Append(':').Append(s);

    private static void AppendSortedTags(StringBuilder sb, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0) { return; }

        sb.Append("|t:");

        foreach (var tag in tags.OrderBy(t => t, StringComparer.Ordinal))
        {
            AppendLengthPrefixed(sb, tag);
        }
    }
}
