// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Filtering.Evaluation;

internal static class ResolvedEventExtensions
{
    extension(ResolvedEvent? @event)
    {
        public bool MatchesFilters(IEnumerable<SavedFilter> filters)
        {
            if (@event is null) { return false; }

            bool isEmpty = true;
            bool isFiltered = false;

            foreach (var filter in filters)
            {
                var compiled = filter.Compiled;

                if (compiled is null) { continue; }

                FilterMatch match = compiled.Evaluate?.Invoke(@event) ??
                    (compiled.Predicate(@event) ? FilterMatch.Match : FilterMatch.NoMatch);

                if (filter.IsExcluded)
                {
                    // Exclude hides only on a decisive Match; Unknown and NoMatch keep the row visible.
                    if (match == FilterMatch.Match) { return false; }

                    continue;
                }

                isEmpty = false;

                // Include keeps the row on a Match OR an Unknown; only a decisive NoMatch fails to satisfy it.
                if (match != FilterMatch.NoMatch) { isFiltered = true; }
            }

            return isEmpty || isFiltered;
        }

        public bool MatchesDateFilter(DateFilter? dateFilter)
        {
            if (@event is null) { return false; }

            if (dateFilter is null || !dateFilter.IsEnabled) { return true; }

            return @event.TimeCreated >= dateFilter.After && @event.TimeCreated <= dateFilter.Before;
        }
    }
}
