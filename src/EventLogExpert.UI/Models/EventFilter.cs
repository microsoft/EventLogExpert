// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Models;

public readonly record struct EventFilter(FilterDateModel? DateFilter, ImmutableList<FilterModel> Filters)
{
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
}
