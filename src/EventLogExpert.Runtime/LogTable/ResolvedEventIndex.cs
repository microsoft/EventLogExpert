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

        if (sortedEvents is CombinedEventView combined) { return combined.Rank(target); }

        if (sortedEvents is SegmentedSortedList segmented) { return segmented.Rank(target); }

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

        // Scan the comparer-equal window for the exact null-RecordId instance.
        for (int i = lowerBound; i < sortedEvents.Count && comparer(sortedEvents[i], target) == 0; i++)
        {
            if (ReferenceEquals(sortedEvents[i], target)) { return i; }
        }

        return -1;
    }

    public static ResolvedEvent? ResolveByKey(IReadOnlyList<ResolvedEvent> events, ResolvedEvent? candidate)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (candidate is null) { return null; }

        if (events is CombinedEventView combined) { return combined.ResolveByKey(candidate); }

        if (events is SegmentedSortedList segmented) { return segmented.ResolveByKey(candidate); }

        for (int i = 0; i < events.Count; i++)
        {
            var current = events[i];

            if (ReferenceEquals(current, candidate)) { return current; }

            // Skip null RecordId: null == null would merge distinct error-read events.
            if (current.RecordId is null || candidate.RecordId is null) { continue; }

            if (current.RecordId == candidate.RecordId &&
                current.TimeCreated == candidate.TimeCreated &&
                string.Equals(current.OwningLog, candidate.OwningLog, StringComparison.Ordinal) &&
                string.Equals(current.LogName, candidate.LogName, StringComparison.Ordinal))
            {
                return current;
            }
        }

        return null;
    }

    public static IReadOnlyList<ResolvedEvent> Slice(IReadOnlyList<ResolvedEvent> events, int start, int count)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events is CombinedEventView combined) { return combined.Slice(start, count); }

        if (start < 0) { throw new ArgumentOutOfRangeException(nameof(start)); }

        if (count < 0) { throw new ArgumentOutOfRangeException(nameof(count)); }

        int end = (int)Math.Min((long)start + count, events.Count);

        if (start >= end) { return []; }

        var result = new ResolvedEvent[end - start];

        for (int i = 0; i < result.Length; i++) { result[i] = events[start + i]; }

        return result;
    }
}
