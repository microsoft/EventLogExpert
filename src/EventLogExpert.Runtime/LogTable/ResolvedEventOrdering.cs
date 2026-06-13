// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable;

internal static class ResolvedEventOrdering
{
    private static readonly Comparison<ResolvedEvent> s_ascByLevel =
        (a, b) => WithTieBreaker(string.Compare(a.Level, b.Level, StringComparison.Ordinal), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByDateAndTime =
        (a, b) => WithTieBreaker(a.TimeCreated.CompareTo(b.TimeCreated), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByActivityId =
        (a, b) => WithTieBreaker(Nullable.Compare(a.ActivityId, b.ActivityId), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByLog =
        (a, b) => WithTieBreaker(string.Compare(a.LogName, b.LogName, StringComparison.Ordinal), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByComputerName =
        (a, b) => WithTieBreaker(string.Compare(a.ComputerName, b.ComputerName, StringComparison.Ordinal), a, b);

    private static readonly Comparison<ResolvedEvent> s_ascBySource =
        (a, b) => WithTieBreaker(string.Compare(a.Source, b.Source, StringComparison.Ordinal), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByEventId =
        (a, b) => WithTieBreaker(a.Id.CompareTo(b.Id), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByTaskCategory =
        (a, b) => WithTieBreaker(string.Compare(a.TaskCategory, b.TaskCategory, StringComparison.Ordinal), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByKeywords =
        (a, b) => WithTieBreaker(string.Compare(a.KeywordsDisplayName, b.KeywordsDisplayName, StringComparison.Ordinal), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByProcessId =
        (a, b) => WithTieBreaker(Nullable.Compare(a.ProcessId, b.ProcessId), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByThreadId =
        (a, b) => WithTieBreaker(Nullable.Compare(a.ThreadId, b.ThreadId), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByUser =
        (a, b) => WithTieBreaker(string.Compare(a.UserId?.Value, b.UserId?.Value, StringComparison.Ordinal), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByRecordId =
        (a, b) => WithTieBreaker(Nullable.Compare(a.RecordId, b.RecordId), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByDefault =
        (a, b) => FallbackTieBreaker(Nullable.Compare(a.RecordId, b.RecordId), a, b);

    private static readonly Comparison<ResolvedEvent> s_descByActivityId = (a, b) => s_ascByActivityId(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByComputerName = (a, b) => s_ascByComputerName(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByDateAndTime = (a, b) => s_ascByDateAndTime(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByDefault = (a, b) => s_ascByDefault(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByEventId = (a, b) => s_ascByEventId(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByKeywords = (a, b) => s_ascByKeywords(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByLevel = (a, b) => s_ascByLevel(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByLog = (a, b) => s_ascByLog(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByProcessId = (a, b) => s_ascByProcessId(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByRecordId = (a, b) => s_ascByRecordId(b, a);
    private static readonly Comparison<ResolvedEvent> s_descBySource = (a, b) => s_ascBySource(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByTaskCategory = (a, b) => s_ascByTaskCategory(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByThreadId = (a, b) => s_ascByThreadId(b, a);
    private static readonly Comparison<ResolvedEvent> s_descByUser = (a, b) => s_ascByUser(b, a);

    /// <summary>Sorts events by RecordId if no order is specified. Always returns a new list.</summary>
    public static IReadOnlyList<ResolvedEvent> SortEvents(
        this IEnumerable<ResolvedEvent> events,
        ColumnName? orderBy = null,
        bool isDescending = false,
        ColumnName? groupBy = null,
        bool isGroupDescending = false)
    {
        var sorted = new List<ResolvedEvent>(events);
        sorted.Sort(SelectComparer(orderBy, isDescending, groupBy, isGroupDescending));

        return sorted.AsReadOnly();
    }

    internal static int CompareColumn(ResolvedEvent a, ResolvedEvent b, ColumnName column) =>
        column switch
        {
            ColumnName.RecordId => Nullable.Compare(a.RecordId, b.RecordId),
            ColumnName.Level => string.Compare(a.Level ?? string.Empty, b.Level ?? string.Empty, StringComparison.Ordinal),
            ColumnName.DateAndTime => a.TimeCreated.CompareTo(b.TimeCreated),
            ColumnName.ActivityId => Nullable.Compare(a.ActivityId, b.ActivityId),
            ColumnName.Log => string.Compare(a.LogName ?? string.Empty, b.LogName ?? string.Empty, StringComparison.Ordinal),
            ColumnName.ComputerName => string.Compare(a.ComputerName ?? string.Empty, b.ComputerName ?? string.Empty, StringComparison.Ordinal),
            ColumnName.Source => string.Compare(a.Source ?? string.Empty, b.Source ?? string.Empty, StringComparison.Ordinal),
            ColumnName.EventId => a.Id.CompareTo(b.Id),
            ColumnName.TaskCategory => string.Compare(a.TaskCategory ?? string.Empty, b.TaskCategory ?? string.Empty, StringComparison.Ordinal),
            ColumnName.Keywords => string.Compare(a.KeywordsDisplayName ?? string.Empty, b.KeywordsDisplayName ?? string.Empty, StringComparison.Ordinal),
            ColumnName.ProcessId => Nullable.Compare(a.ProcessId, b.ProcessId),
            ColumnName.ThreadId => Nullable.Compare(a.ThreadId, b.ThreadId),
            ColumnName.User => string.Compare(a.UserId?.Value ?? string.Empty, b.UserId?.Value ?? string.Empty, StringComparison.Ordinal),
            _ => 0
        };

    internal static Comparison<ResolvedEvent> GetComparer(ColumnName? orderBy, bool isDescending) =>
        isDescending
            ? orderBy switch
            {
                ColumnName.RecordId => s_descByRecordId,
                ColumnName.Level => s_descByLevel,
                ColumnName.DateAndTime => s_descByDateAndTime,
                ColumnName.ActivityId => s_descByActivityId,
                ColumnName.Log => s_descByLog,
                ColumnName.ComputerName => s_descByComputerName,
                ColumnName.Source => s_descBySource,
                ColumnName.EventId => s_descByEventId,
                ColumnName.TaskCategory => s_descByTaskCategory,
                ColumnName.Keywords => s_descByKeywords,
                ColumnName.ProcessId => s_descByProcessId,
                ColumnName.ThreadId => s_descByThreadId,
                ColumnName.User => s_descByUser,
                _ => s_descByDefault
            }
            : orderBy switch
            {
                ColumnName.RecordId => s_ascByRecordId,
                ColumnName.Level => s_ascByLevel,
                ColumnName.DateAndTime => s_ascByDateAndTime,
                ColumnName.ActivityId => s_ascByActivityId,
                ColumnName.Log => s_ascByLog,
                ColumnName.ComputerName => s_ascByComputerName,
                ColumnName.Source => s_ascBySource,
                ColumnName.EventId => s_ascByEventId,
                ColumnName.TaskCategory => s_ascByTaskCategory,
                ColumnName.Keywords => s_ascByKeywords,
                ColumnName.ProcessId => s_ascByProcessId,
                ColumnName.ThreadId => s_ascByThreadId,
                ColumnName.User => s_ascByUser,
                _ => s_ascByDefault
            };

    internal static ColumnName GetEffectiveOrderBy(ColumnName? orderBy) => orderBy ?? ColumnName.DateAndTime;

    internal static Comparison<ResolvedEvent> GetGroupedComparer(
        ColumnName groupBy,
        bool isGroupDescending,
        ColumnName? orderBy,
        bool isDescending)
    {
        var withinGroup = orderBy ?? ColumnName.DateAndTime;

        return (a, b) =>
        {
            int group = CompareColumn(a, b, groupBy);

            if (group != 0) { return isGroupDescending ? -Math.Sign(group) : group; }

            int within = CompareColumn(a, b, withinGroup);

            if (within == 0 && withinGroup != ColumnName.DateAndTime)
            {
                within = CompareColumn(a, b, ColumnName.DateAndTime);
            }

            if (within == 0)
            {
                within = FallbackTieBreaker(Nullable.Compare(a.RecordId, b.RecordId), a, b);
            }

            return isDescending ? -Math.Sign(within) : within;
        };
    }

    internal static IReadOnlyList<ResolvedEvent> MergeSorted(
        IReadOnlyList<ResolvedEvent> existing,
        IReadOnlyList<ResolvedEvent> batch,
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy = null,
        bool isGroupDescending = false)
    {
        if (batch.Count == 0) { return existing; }

        if (existing.Count == 0) { return batch.SortEvents(orderBy, isDescending, groupBy, isGroupDescending); }

        var comparer = SelectComparer(orderBy, isDescending, groupBy, isGroupDescending);

        var sortedBatch = new List<ResolvedEvent>(batch);
        sortedBatch.Sort(comparer);

        var result = new List<ResolvedEvent>(existing.Count + sortedBatch.Count);
        int i = 0, j = 0;

        while (i < existing.Count && j < sortedBatch.Count)
        {
            result.Add(comparer(existing[i], sortedBatch[j]) <= 0 ? existing[i++] : sortedBatch[j++]);
        }

        while (i < existing.Count) { result.Add(existing[i++]); }

        while (j < sortedBatch.Count) { result.Add(sortedBatch[j++]); }

        return result.AsReadOnly();
    }

    internal static Comparison<ResolvedEvent> SelectComparer(
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending) =>
        groupBy is null
            ? GetComparer(orderBy, isDescending)
            : GetGroupedComparer(groupBy.Value, isGroupDescending, orderBy, isDescending);

    /// <summary>Falls back to RecordId, then OwningLog (for combined logs) to guarantee a total order for List.Sort stability.</summary>
    private static int FallbackTieBreaker(int recordIdResult, ResolvedEvent a, ResolvedEvent b) =>
        recordIdResult != 0 ? recordIdResult : string.Compare(a.OwningLog, b.OwningLog, StringComparison.Ordinal);

    private static int WithTieBreaker(int primaryResult, ResolvedEvent a, ResolvedEvent b) =>
        primaryResult != 0 ? primaryResult : FallbackTieBreaker(Nullable.Compare(a.RecordId, b.RecordId), a, b);
}
