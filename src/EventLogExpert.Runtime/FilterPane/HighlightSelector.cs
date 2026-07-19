// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Runtime.FilterPane;

internal sealed class HighlightSelector : IHighlightSelector
{
    public int ComputeHighlightKey(ImmutableList<SavedFilter> filters)
    {
        var hash = new HashCode();
        int eligibleCount = 0;

        foreach (var filter in filters)
        {
            if (filter is not { IsEnabled: true, IsExcluded: false }) { continue; }

            if (filter.Compiled is null) { continue; }

            if (!Enum.IsDefined(filter.Color)) { continue; }

            hash.Add(RuntimeHelpers.GetHashCode(filter.Compiled));
            hash.Add((int)filter.Color);
            eligibleCount++;
        }

        hash.Add(eligibleCount);

        return hash.ToHashCode();
    }

    public int ComputePredicatePlanKey(ImmutableList<SavedFilter> filters)
    {
        var hash = new HashCode();
        int eligibleCount = 0;

        for (int position = 0; position < filters.Count; position++)
        {
            var filter = filters[position];

            if (filter is not { IsEnabled: true, IsExcluded: false }) { continue; }

            if (filter.Compiled is null) { continue; }

            if (!Enum.IsDefined(filter.Color)) { continue; }

            hash.Add(filter.ComparisonText, StringComparer.Ordinal);
            hash.Add(filter.IsExcluded);
            hash.Add(filter.IsEnabled);
            hash.Add(position);
            eligibleCount++;
        }

        hash.Add(eligibleCount);

        return hash.ToHashCode();
    }

    public SavedFilter[] Select(ImmutableList<SavedFilter> filters)
    {
        if (filters.IsEmpty) { return []; }

        var selected = new List<SavedFilter>(filters.Count);

        foreach (var filter in filters)
        {
            if (filter is not { IsEnabled: true, IsExcluded: false }) { continue; }

            if (filter.Compiled is null) { continue; }

            if (!Enum.IsDefined(filter.Color)) { continue; }

            selected.Add(filter);
        }

        return selected.Count == 0 ? [] : [.. selected];
    }
}
