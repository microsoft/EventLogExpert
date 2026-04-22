// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Models;

/// <summary>
/// Snapshot of the properties of a <see cref="FilterModel"/> that affect the filtered event set.
/// Captured at <see cref="EventFilter"/> construction so subsequent in-place mutation of the
/// underlying <see cref="FilterModel"/> (by reducers) cannot invalidate equality comparisons.
/// </summary>
public readonly record struct FilterSignatureEntry(string Value, bool IsExcluded);

public readonly record struct EventFilter
{
    public EventFilter(FilterDateModel? dateFilter, ImmutableList<FilterModel> filters)
    {
        DateFilter = dateFilter;
        Filters = filters;
        Signature = ComputeSignature(Filters);
    }

    public FilterDateModel? DateFilter { get; }

    public ImmutableList<FilterModel> Filters { get; }

    /// <summary>
    /// Construction-time snapshot of the filter set used for equality comparisons by
    /// <c>FilterMethods.HasFilteringChanged</c>. Immune to subsequent in-place mutation
    /// of the source <see cref="FilterModel"/> instances.
    /// </summary>
    public ImmutableArray<FilterSignatureEntry> Signature { get; }

    /// <summary>
    /// Returns <c>true</c> when any active filter (or sub-filter) references
    /// <see cref="Eventing.Models.DisplayEventModel.Xml"/>. Used to decide whether
    /// logs must be opened with pre-rendered XML.
    /// </summary>
    public bool RequiresXml
    {
        get
        {
            if (Filters is null) { return false; }

            foreach (var filter in Filters)
            {
                if (filter.Comparison.RequiresXml) { return true; }

                if (filter.SubFilters.Count == 0) { continue; }

                foreach (var sub in filter.SubFilters)
                {
                    if (sub.Comparison.RequiresXml) { return true; }
                }
            }

            return false;
        }
    }

    private static ImmutableArray<FilterSignatureEntry> ComputeSignature(ImmutableList<FilterModel> filters)
    {
        if (filters.Count == 0) { return ImmutableArray<FilterSignatureEntry>.Empty; }

        var builder = ImmutableArray.CreateBuilder<FilterSignatureEntry>(filters.Count);

        foreach (var filter in filters)
        {
            builder.Add(new FilterSignatureEntry(filter.Comparison.Value, filter.IsExcluded));
        }

        return builder.MoveToImmutable();
    }
}
