// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace EventLogExpert.UI.FilterPane;

internal static class HighlightFilterSelector
{
    /// <summary>
    ///     Probabilistic change-key over the <em>candidate</em> subset of <paramref name="filters" /> — i.e. the same
    ///     gating used by <see cref="SelectFromFilters" />: enabled, non-excluded, compiled, with an enum-defined
    ///     <see cref="HighlightColor" />. Two filter lists that produce identical highlight behavior return the same key; any
    ///     mutation that changes the candidate set, the candidate order, the candidate colors, or any candidate's
    ///     <c>Compiled</c> reference returns a different key. Mutations to non-candidate filters and to fields outside the
    ///     highlight surface (Id, Mode, BasicFilter, ComparisonText where Compiled is unchanged) deliberately do not change
    ///     the key. The 32-bit hash means collisions are theoretically possible; the worst-case fallout is a stale highlight
    ///     cache until the next non-colliding change.
    /// </summary>
    internal static int ComputeHighlightKey(ImmutableList<SavedFilter> filters)
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
