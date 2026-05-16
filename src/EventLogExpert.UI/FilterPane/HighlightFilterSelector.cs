// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace EventLogExpert.UI.FilterPane;

internal sealed class HighlightFilterSelector : IHighlightFilterSelector
{
    public int ComputeHighlightKey(ImmutableList<SavedFilter> filters)
    {
        var hash = new HashCode();
        int candidateCount = 0;

        foreach (var filter in filters)
        {
            if (filter is not { IsEnabled: true, IsExcluded: false }) { continue; }

            if (filter.Compiled is null) { continue; }

            if (!Enum.IsDefined(filter.Color)) { continue; }

            hash.Add(RuntimeHelpers.GetHashCode(filter.Compiled));
            hash.Add((int)filter.Color);
            candidateCount++;
        }

        hash.Add(candidateCount);

        return hash.ToHashCode();
    }

    public SavedFilter[] SelectHighlightCandidates(ImmutableList<SavedFilter> filters)
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
