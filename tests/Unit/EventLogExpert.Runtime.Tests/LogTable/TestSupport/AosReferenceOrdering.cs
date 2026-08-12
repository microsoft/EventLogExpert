// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.LogTable.TestSupport;

internal static class AosReferenceOrdering
{
    private static readonly Comparison<ResolvedEvent> s_ascByLevel =
        (a, b) => WithTieBreaker(CompareText(a.Level, b.Level), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByDateAndTime =
        (a, b) => WithTieBreaker(a.TimeCreated.CompareTo(b.TimeCreated), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByActivityId =
        (a, b) => WithTieBreaker(Nullable.Compare(a.ActivityId, b.ActivityId), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByLog =
        (a, b) => WithTieBreaker(CompareText(a.LogName, b.LogName), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByComputerName =
        (a, b) => WithTieBreaker(CompareText(a.ComputerName, b.ComputerName), a, b);

    private static readonly Comparison<ResolvedEvent> s_ascBySource =
        (a, b) => WithTieBreaker(CompareText(a.Source, b.Source), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByEventId =
        (a, b) => WithTieBreaker(a.Id.CompareTo(b.Id), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByTaskCategory =
        (a, b) => WithTieBreaker(CompareText(a.TaskCategory, b.TaskCategory), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByKeywords =
        (a, b) => WithTieBreaker(CompareText(a.KeywordsDisplayName, b.KeywordsDisplayName), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByProcessId =
        (a, b) => WithTieBreaker(Nullable.Compare(a.ProcessId, b.ProcessId), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByThreadId =
        (a, b) => WithTieBreaker(Nullable.Compare(a.ThreadId, b.ThreadId), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByUser =
        (a, b) => WithTieBreaker(CompareText(a.UserDisplayName, b.UserDisplayName), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByRecordId =
        (a, b) => WithTieBreaker(Nullable.Compare(a.RecordId, b.RecordId), a, b);
    private static readonly Comparison<ResolvedEvent> s_ascByDefault =
        (a, b) =>
        {
            int byRecordId = Nullable.Compare(a.RecordId, b.RecordId);

            if (byRecordId != 0) { return byRecordId; }

            int byTime = a.TimeCreated.CompareTo(b.TimeCreated);

            return byTime != 0 ? byTime : CompareText(a.OwningLog, b.OwningLog);
        };

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

    internal static int[] Order(
        IReadOnlyList<ResolvedEvent> source,
        ColumnName? orderBy = null,
        bool isDescending = false,
        ColumnName? groupBy = null,
        bool isGroupDescending = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        Comparison<ResolvedEvent> reference = Reference(orderBy, isDescending, groupBy, isGroupDescending);
        int[] order = new int[source.Count];

        for (int index = 0; index < order.Length; index++) { order[index] = index; }

        Array.Sort(order, (a, b) =>
        {
            int compared = reference(source[a], source[b]);

            return compared != 0 ? compared : a.CompareTo(b);
        });

        return order;
    }

    internal static IReadOnlyList<ResolvedEvent> OrderedEvents(
        IEnumerable<ResolvedEvent> source,
        ColumnName? orderBy = null,
        bool isDescending = false,
        ColumnName? groupBy = null,
        bool isGroupDescending = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        IReadOnlyList<ResolvedEvent> materialized = source as IReadOnlyList<ResolvedEvent> ?? source.ToList();
        int[] order = Order(materialized, orderBy, isDescending, groupBy, isGroupDescending);
        var ordered = new ResolvedEvent[order.Length];

        for (int index = 0; index < order.Length; index++) { ordered[index] = materialized[order[index]]; }

        return ordered;
    }

    internal static Comparison<ResolvedEvent> Reference(
        ColumnName? orderBy,
        bool isDescending,
        ColumnName? groupBy,
        bool isGroupDescending) =>
        groupBy is null
            ? GetComparer(orderBy, isDescending)
            : GetGroupedComparer(groupBy.Value, isGroupDescending, orderBy, isDescending);

    private static int CompareColumn(ResolvedEvent a, ResolvedEvent b, ColumnName column) =>
        column switch
        {
            ColumnName.RecordId => Nullable.Compare(a.RecordId, b.RecordId),
            ColumnName.Level => CompareText(a.Level, b.Level),
            ColumnName.DateAndTime => a.TimeCreated.CompareTo(b.TimeCreated),
            ColumnName.ActivityId => Nullable.Compare(a.ActivityId, b.ActivityId),
            ColumnName.Log => CompareText(a.LogName, b.LogName),
            ColumnName.ComputerName => CompareText(a.ComputerName, b.ComputerName),
            ColumnName.Source => CompareText(a.Source, b.Source),
            ColumnName.EventId => a.Id.CompareTo(b.Id),
            ColumnName.TaskCategory => CompareText(a.TaskCategory, b.TaskCategory),
            ColumnName.Keywords => CompareText(a.KeywordsDisplayName, b.KeywordsDisplayName),
            ColumnName.ProcessId => Nullable.Compare(a.ProcessId, b.ProcessId),
            ColumnName.ThreadId => Nullable.Compare(a.ThreadId, b.ThreadId),
            ColumnName.User => CompareText(a.UserDisplayName, b.UserDisplayName),
            _ => 0
        };

    private static int CompareText(string? a, string? b) =>
        string.Compare(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);

    private static int FallbackTieBreaker(int recordIdResult, ResolvedEvent a, ResolvedEvent b) =>
        recordIdResult != 0 ? recordIdResult : CompareText(a.OwningLog, b.OwningLog);

    private static Comparison<ResolvedEvent> GetComparer(ColumnName? orderBy, bool isDescending) =>
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

    private static Comparison<ResolvedEvent> GetGroupedComparer(
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

    private static int WithTieBreaker(int primaryResult, ResolvedEvent a, ResolvedEvent b) =>
        primaryResult != 0 ? primaryResult : FallbackTieBreaker(Nullable.Compare(a.RecordId, b.RecordId), a, b);
}
