// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Filter;

/// <summary>Snapshot of <see cref="SavedFilter" /> fields that affect the filtered event set.</summary>
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

    /// <summary>Construction-time snapshot used by <see cref="EventFilterExtensions.HasFilteringChangedFrom" />.</summary>
    public ImmutableArray<FilterSnapshot> Snapshots { get; }

    /// <summary>
    ///     True when any filter or sub-filter references
    ///     <see cref="EventLogExpert.Eventing.Common.Events.ResolvedEvent.Xml" />.
    /// </summary>
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
        if (filters.Count == 0) { return ImmutableArray<FilterSnapshot>.Empty; }

        var builder = ImmutableArray.CreateBuilder<FilterSnapshot>(filters.Count);

        foreach (var filter in filters)
        {
            builder.Add(new FilterSnapshot(filter.ComparisonText, filter.IsExcluded));
        }

        return builder.MoveToImmutable();
    }
}
