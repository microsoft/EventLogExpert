// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.Filter;

internal static class ResolvedEventExtensions
{
    extension(ResolvedEvent? @event)
    {
        public bool Filter(IEnumerable<SavedFilter> filters)
        {
            if (@event is null) { return false; }

            bool isEmpty = true;
            bool isFiltered = false;

            foreach (var filter in filters)
            {
                if (filter.Compiled is null) { continue; }

                if (filter.IsExcluded && filter.Compiled.Predicate(@event)) { return false; }

                if (!filter.IsExcluded) { isEmpty = false; }

                if (!filter.IsExcluded && filter.Compiled.Predicate(@event)) { isFiltered = true; }
            }

            return isEmpty || isFiltered;
        }

        public ResolvedEvent? FilterByDate(DateFilter? dateFilter)
        {
            if (@event is null) { return null; }

            if (dateFilter is null) { return @event; }

            return @event.TimeCreated >= dateFilter.After && @event.TimeCreated <= dateFilter.Before ? @event : null;
        }
    }
}
