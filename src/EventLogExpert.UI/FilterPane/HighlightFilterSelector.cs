// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterPane;

internal static class HighlightFilterSelector
{
    internal static SavedFilter[] SelectHighlightCandidates(FilterPaneState filterPaneState) =>
        SelectFromFilters(filterPaneState.Filters);

    private static SavedFilter[] SelectFromFilters(ImmutableList<SavedFilter> filters)
    {
        if (filters.IsEmpty) { return []; }

        var candidates = new List<SavedFilter>(filters.Count);

        foreach (var filter in filters)
        {
            if (filter is not { IsEnabled: true, IsExcluded: false }) { continue; }

            if (filter.Compiled is null) { continue; }

            if (!Enum.IsDefined(filter.Color)) { continue; }

            candidates.Add(filter);
        }

        return candidates.Count == 0 ? [] : candidates.ToArray();
    }
}
