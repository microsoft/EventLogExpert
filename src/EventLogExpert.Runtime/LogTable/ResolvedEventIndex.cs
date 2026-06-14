// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

public static class ResolvedEventIndex
{
    public static int IndexOf(
        IReadOnlyList<ResolvedEvent> sortedEvents,
        ResolvedEvent target,
        ColumnName? orderBy = null,
        bool isDescending = false,
        ColumnName? groupBy = null,
        bool isGroupDescending = false)
    {
        ArgumentNullException.ThrowIfNull(sortedEvents);
        ArgumentNullException.ThrowIfNull(target);

        var comparer = ResolvedEventOrdering.SelectComparer(
            ResolvedEventOrdering.GetEffectiveOrderBy(orderBy),
            isDescending,
            groupBy,
            isGroupDescending);

        int low = 0;
        int high = sortedEvents.Count - 1;
        int lowerBound = -1;

        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            int comparison = comparer(sortedEvents[mid], target);

            switch (comparison)
            {
                case < 0:
                    low = mid + 1;
                    break;
                case > 0:
                    high = mid - 1;
                    break;
                default:
                    lowerBound = mid;
                    high = mid - 1;
                    break;
            }
        }

        if (lowerBound < 0) { return -1; }

        // The order is total except for error-read events with a null RecordId in the same log, which compare equal;
        // scan the comparer-equal window for the exact instance so a tie can't resolve to the wrong row.
        for (int i = lowerBound; i < sortedEvents.Count && comparer(sortedEvents[i], target) == 0; i++)
        {
            if (ReferenceEquals(sortedEvents[i], target)) { return i; }
        }

        return -1;
    }
}
