// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.LogTable;

/// <summary>
///     Differential parity for the column-side group keys: the live
///     <see cref="ResolvedEventGroupKey.For(IEventColumnReader, EventLocator, ColumnName)" /> and
///     <see cref="IEventColumnView.GroupKeyAt" /> must match the relocated array-of-structs group key
///     <see cref="AosReferenceGroupKey.For(ColumnName, ResolvedEvent)" /> for every column and event, and their contiguous
///     group runs must match over the grouped-sort order produced by the reference comparer
///     <see cref="AosReferenceOrdering.Reference" />. A <see cref="LegacyEventColumnReader" /> reads the same
///     <see cref="ResolvedEvent" /> corpus, so any divergence isolates the column-side group-key logic rather than the
///     store.
/// </summary>
public sealed class ColumnDirectOrderingParityTests
{
    private static readonly ColumnName[] s_allColumns = Enum.GetValues<ColumnName>();
    private static readonly bool[] s_bools = [false, true];
    private static readonly IReadOnlyList<ResolvedEvent> s_edgeCorpus = BuildEdgeCorpus();
    private static readonly LegacyEventColumnView s_edgeView = NewView(s_edgeCorpus);
    private static readonly IReadOnlyList<SortConfig> s_allConfigs = BuildAllConfigs();

    [Fact]
    public void GroupKeyAt_MatchesArrayOfStructsGroupKey_ForEveryColumnAndEvent()
    {
        var failures = new List<string>();

        for (int index = 0; index < s_edgeCorpus.Count; index++)
        {
            EventLocator locator = s_edgeView.LocatorAt(index);

            foreach (ColumnName column in s_allColumns)
            {
                string arrayOfStructsKey = AosReferenceGroupKey.For(column, s_edgeCorpus[index]);
                string viewKey = s_edgeView.GroupKeyAt(locator, column);
                string readerKey = ResolvedEventGroupKey.For(s_edgeView.Reader, locator, column);

                if (!string.Equals(arrayOfStructsKey, viewKey, StringComparison.Ordinal) ||
                    !string.Equals(arrayOfStructsKey, readerKey, StringComparison.Ordinal))
                {
                    failures.Add($"event {index} column {column}: aos='{arrayOfStructsKey}' view='{viewKey}' reader='{readerKey}'");
                }
            }
        }

        Assert.True(failures.Count == 0, Describe(failures));
    }

    [Fact]
    public void GroupKeyRuns_MatchArrayOfStructs_ForEveryGroupColumn()
    {
        var failures = new List<string>();

        foreach (ColumnName column in s_allColumns)
        {
            Comparison<ResolvedEvent> comparer = AosReferenceOrdering.Reference(null, false, column, false);
            int[] order = Enumerable.Range(0, s_edgeCorpus.Count).ToArray();
            Array.Sort(order, (left, right) => comparer(s_edgeCorpus[left], s_edgeCorpus[right]));

            List<string> arrayOfStructsKeys = order.Select(index => AosReferenceGroupKey.For(column, s_edgeCorpus[index])).ToList();
            List<string> columnKeys = order.Select(index => s_edgeView.GroupKeyAt(s_edgeView.LocatorAt(index), column)).ToList();

            if (!arrayOfStructsKeys.SequenceEqual(columnKeys, StringComparer.Ordinal))
            {
                failures.Add($"column {column}: group-key sequences differ over the grouped-sort order");
            }

            if (!RunLengths(arrayOfStructsKeys).SequenceEqual(RunLengths(columnKeys)))
            {
                failures.Add($"column {column}: contiguous run partitions differ");
            }
        }

        Assert.True(failures.Count == 0, Describe(failures));
    }

    [Fact]
    public void MatrixCovers756Configs_IncludingNullOrderByDescendingInBothArms()
    {
        Assert.Equal(756, s_allConfigs.Count);
        Assert.Contains(s_allConfigs, config => config.OrderBy is null && config.IsDescending && config.GroupBy is null);
        Assert.Contains(s_allConfigs, config => config.OrderBy is null && config.IsDescending && config.GroupBy is not null);
    }

    private static IReadOnlyList<SortConfig> BuildAllConfigs()
    {
        var configs = new List<SortConfig>();

        foreach (ColumnName? orderBy in OrderByOptions())
        {
            foreach (bool isDescending in s_bools)
            {
                configs.Add(new SortConfig(orderBy, isDescending, null, false));
            }
        }

        foreach (ColumnName groupBy in s_allColumns)
        {
            foreach (bool isGroupDescending in s_bools)
            {
                foreach (ColumnName? orderBy in OrderByOptions())
                {
                    foreach (bool isDescending in s_bools)
                    {
                        configs.Add(new SortConfig(orderBy, isDescending, groupBy, isGroupDescending));
                    }
                }
            }
        }

        return configs;
    }

    private static IReadOnlyList<ResolvedEvent> BuildEdgeCorpus()
    {
        var guidLow = new Guid("00000001-0000-0000-0000-000000000000");
        var guidHigh = new Guid("ffffffff-0000-0000-0000-000000000000");
        var sidLow = new SecurityIdentifier("S-1-5-18");
        var sidHigh = new SecurityIdentifier("S-1-5-21-1-2-3-1001");
        var early = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var middle = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var late = new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        return
        [
            // Numeric 2-vs-10: numeric order (2 < 10) is the opposite of lexical ("10" < "2"); Guid low < high; equal time.
            FilterEventBuilder.CreateTestEvent(id: 2, recordId: 2, processId: 2, threadId: 2, source: "Alpha", level: "Information", timeCreated: early, activityId: guidLow, userId: sidLow),
            FilterEventBuilder.CreateTestEvent(id: 10, recordId: 10, processId: 10, threadId: 10, source: "Alpha", level: "Information", timeCreated: early, activityId: guidHigh, userId: sidHigh),

            // Equal primary on Source/Level/EventId/time with distinct RecordId → exercises the RecordId tie-break.
            FilterEventBuilder.CreateTestEvent(id: 1, recordId: 5, source: "Beta", level: "Error", timeCreated: middle),
            FilterEventBuilder.CreateTestEvent(id: 1, recordId: 7, source: "Beta", level: "Error", timeCreated: middle),

            // Absent nullable columns vs an all-present twin (Absent sorts first).
            FilterEventBuilder.CreateTestEvent(id: 3, source: "Gamma", level: "Warning", timeCreated: late),
            FilterEventBuilder.CreateTestEvent(id: 3, recordId: 100, processId: 100, threadId: 100, source: "Gamma", level: "Warning", timeCreated: late, activityId: guidLow, userId: sidLow),

            // Grouped-secondary triple: same Source + Level, distinct time → within-tie falls through to DateAndTime.
            FilterEventBuilder.CreateTestEvent(id: 50, recordId: 201, source: "Delta", level: "Info2", timeCreated: early),
            FilterEventBuilder.CreateTestEvent(id: 50, recordId: 202, source: "Delta", level: "Info2", timeCreated: middle),
            FilterEventBuilder.CreateTestEvent(id: 50, recordId: 203, source: "Delta", level: "Info2", timeCreated: late),

            // Distinct values across the string columns (Log / ComputerName / TaskCategory / Keywords / User).
            FilterEventBuilder.CreateTestEvent(id: 4, recordId: 300, source: "Epsilon", computerName: "Host-A", logName: "AppLog", taskCategory: "Cat-A", keywords: ["K1"], userId: sidLow, timeCreated: early),
            FilterEventBuilder.CreateTestEvent(id: 4, recordId: 301, source: "Epsilon", computerName: "Host-B", logName: "SysLog", taskCategory: "Cat-B", keywords: ["K2"], userId: sidHigh, timeCreated: middle),

            // ActivityId high/low pair (typed Guid.CompareTo: low < high on .NET, same as the "D" string order).
            FilterEventBuilder.CreateTestEvent(id: 6, recordId: 400, source: "Zeta", timeCreated: early, activityId: guidHigh),
            FilterEventBuilder.CreateTestEvent(id: 6, recordId: 401, source: "Zeta", timeCreated: early, activityId: guidLow),

            // Equal on everything with null RecordId, distinct OwningLog → exercises the final OwningLog tie-break.
            FilterEventBuilder.CreateTestEvent(id: 7, source: "Eta", level: "Ledger", timeCreated: early, owningLog: "LogX"),
            FilterEventBuilder.CreateTestEvent(id: 7, source: "Eta", level: "Ledger", timeCreated: early, owningLog: "LogY")
        ];
    }

    private static string Describe(IReadOnlyList<string> failures) =>
        $"{failures.Count} divergence(s):{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(25))}";

    private static LegacyEventColumnView NewView(IReadOnlyList<ResolvedEvent> corpus) =>
        new(EventLogId.Create(), generation: 1, contentVersion: 1, corpus);

    private static IEnumerable<ColumnName?> OrderByOptions()
    {
        yield return null;

        foreach (ColumnName column in s_allColumns)
        {
            yield return column;
        }
    }

    private static List<int> RunLengths(IReadOnlyList<string> keys)
    {
        var lengths = new List<int>();
        int index = 0;

        while (index < keys.Count)
        {
            int next = index + 1;

            while (next < keys.Count && string.Equals(keys[next], keys[index], StringComparison.Ordinal))
            {
                next++;
            }

            lengths.Add(next - index);
            index = next;
        }

        return lengths;
    }

    private readonly record struct SortConfig(ColumnName? OrderBy, bool IsDescending, ColumnName? GroupBy, bool IsGroupDescending);
}
