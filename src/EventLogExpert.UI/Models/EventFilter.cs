// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Models;

/// <summary>Snapshot of <see cref="FilterModel" /> fields that affect the filtered event set.</summary>
public readonly record struct FilterSignatureEntry(string Value, bool IsExcluded);

public readonly record struct EventFilter
{
    public EventFilter(FilterDateModel? dateFilter, ImmutableList<FilterModel> filters)
    {
        DateFilter = dateFilter;
        Filters = filters;
        Signature = ComputeSignature(Filters);
        RequiresXml = ComputeRequiresXml(Filters);
    }

    public FilterDateModel? DateFilter { get; }

    public ImmutableList<FilterModel> Filters { get; }

    /// <summary>Construction-time snapshot used by <see cref="FilterMethods.HasFilteringChanged" />.</summary>
    public ImmutableArray<FilterSignatureEntry> Signature { get; }

    /// <summary>True when any filter or sub-filter references <see cref="Eventing.Models.DisplayEventModel.Xml" />.</summary>
    public bool RequiresXml { get; }

    private static bool ComputeRequiresXml(ImmutableList<FilterModel> filters)
    {
        if (filters.IsEmpty) { return false; }

        foreach (var filter in filters)
        {
            if (filter.Compiled?.RequiresXml == true) { return true; }
        }

        return false;
    }

    private static ImmutableArray<FilterSignatureEntry> ComputeSignature(ImmutableList<FilterModel> filters)
    {
        if (filters.Count == 0) { return ImmutableArray<FilterSignatureEntry>.Empty; }

        var builder = ImmutableArray.CreateBuilder<FilterSignatureEntry>(filters.Count);

        foreach (var filter in filters)
        {
            builder.Add(new FilterSignatureEntry(filter.ComparisonText, filter.IsExcluded));
        }

        return builder.MoveToImmutable();
    }
}
