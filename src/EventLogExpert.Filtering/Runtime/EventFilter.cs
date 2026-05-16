// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Filtering.Runtime;

public readonly record struct FilterSnapshot(string Value, bool IsExcluded);

public readonly record struct EventFilter
{
    public EventFilter(DateFilter? dateFilter, ImmutableList<SavedFilter> filters)
    {
        DateFilter = dateFilter;
        Filters = filters;
        Snapshots = ComputeSnapshots(Filters);
        RequiresXml = ComputeRequiresXml(Filters);
    }

    public DateFilter? DateFilter { get; }

    public ImmutableList<SavedFilter> Filters { get; }

    public bool IsFilteringEnabled =>
        DateFilter?.IsEnabled is true || Filters.IsEmpty is false;

    public ImmutableArray<FilterSnapshot> Snapshots { get; }

    public bool RequiresXml { get; }

    private static bool ComputeRequiresXml(ImmutableList<SavedFilter> filters)
    {
        if (filters.IsEmpty) { return false; }

        foreach (var filter in filters)
        {
            if (filter.Compiled?.RequiresXml == true) { return true; }
        }

        return false;
    }

    private static ImmutableArray<FilterSnapshot> ComputeSnapshots(ImmutableList<SavedFilter> filters)
    {
        if (filters.Count == 0) { return []; }

        var builder = ImmutableArray.CreateBuilder<FilterSnapshot>(filters.Count);

        foreach (var filter in filters)
        {
            builder.Add(new FilterSnapshot(filter.ComparisonText, filter.IsExcluded));
        }

        return builder.MoveToImmutable();
    }
}
