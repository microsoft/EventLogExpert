// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using System.Security.Principal;
using static EventLogExpert.Runtime.Tests.LogTable.TestSupport.DivergenceReport;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class ColumnDirectOrderingParityTests
{
    private static readonly ColumnName[] s_allColumns = Enum.GetValues<ColumnName>();
    private static readonly IReadOnlyList<ResolvedEvent> s_edgeSample = BuildEdgeSample();
    private static readonly IReadOnlyList<SortConfig> s_allConfigs = SortConfigMatrix.All();
    private static readonly IEventColumnReader s_edgeReader = ColumnReaderTestFactory.ReaderOver(s_edgeSample);

    [Fact]
    public void GroupKeyAt_MatchesArrayOfStructsGroupKey_ForEveryColumnAndEvent()
    {
        var failures = new List<string>();

        for (int index = 0; index < s_edgeSample.Count; index++)
        {
            EventLocator locator = s_edgeReader.LocatorAt(index);

            foreach (ColumnName column in s_allColumns)
            {
                string arrayOfStructsKey = AosReferenceGroupKey.For(column, s_edgeSample[index]);
                string readerKey = ResolvedEventGroupKey.For(s_edgeReader, locator, column);

                if (!string.Equals(arrayOfStructsKey, readerKey, StringComparison.Ordinal))
                {
                    failures.Add($"event {index} column {column}: aos='{arrayOfStructsKey}' reader='{readerKey}'");
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
            int[] order = Enumerable.Range(0, s_edgeSample.Count).ToArray();
            Array.Sort(order, (left, right) => comparer(s_edgeSample[left], s_edgeSample[right]));

            List<string> arrayOfStructsKeys = order.Select(index => AosReferenceGroupKey.For(column, s_edgeSample[index])).ToList();
            List<string> columnKeys = order.Select(index => ResolvedEventGroupKey.For(s_edgeReader, s_edgeReader.LocatorAt(index), column)).ToList();

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
    public void MatrixCovers870Configs_IncludingNullOrderByDescendingInBothArms()
    {
        Assert.Equal(870, s_allConfigs.Count);
        Assert.Contains(s_allConfigs, config => config.OrderBy is null && config.IsDescending && config.GroupBy is null);
        Assert.Contains(s_allConfigs, config => config.OrderBy is null && config.IsDescending && config.GroupBy is not null);
    }

    private static IReadOnlyList<ResolvedEvent> BuildEdgeSample()
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
            FilterEventBuilder.CreateTestEvent(id: 2, recordId: 2, processId: 2, threadId: 2, source: "Alpha", level: "Information", timeCreated: early, activityId: guidLow, userId: sidLow, opcode: "Start"),
            FilterEventBuilder.CreateTestEvent(id: 10, recordId: 10, processId: 10, threadId: 10, source: "Alpha", level: "Information", timeCreated: early, activityId: guidHigh, userId: sidHigh, opcode: "Stop"),

            FilterEventBuilder.CreateTestEvent(id: 1, recordId: 5, source: "Beta", level: "Error", timeCreated: middle),
            FilterEventBuilder.CreateTestEvent(id: 1, recordId: 7, source: "Beta", level: "Error", timeCreated: middle),

            FilterEventBuilder.CreateTestEvent(id: 3, source: "Gamma", level: "Warning", timeCreated: late),
            FilterEventBuilder.CreateTestEvent(id: 3, recordId: 100, processId: 100, threadId: 100, source: "Gamma", level: "Warning", timeCreated: late, activityId: guidLow, userId: sidLow),

            FilterEventBuilder.CreateTestEvent(id: 50, recordId: 201, source: "Delta", level: "Info2", timeCreated: early),
            FilterEventBuilder.CreateTestEvent(id: 50, recordId: 202, source: "Delta", level: "Info2", timeCreated: middle),
            FilterEventBuilder.CreateTestEvent(id: 50, recordId: 203, source: "Delta", level: "Info2", timeCreated: late),

            FilterEventBuilder.CreateTestEvent(id: 4, recordId: 300, source: "Epsilon", computerName: "Host-A", logName: "AppLog", taskCategory: "Cat-A", keywords: ["K1"], userId: sidLow, timeCreated: early),
            FilterEventBuilder.CreateTestEvent(id: 4, recordId: 301, source: "Epsilon", computerName: "Host-B", logName: "SysLog", taskCategory: "Cat-B", keywords: ["K2"], userId: sidHigh, timeCreated: middle),

            FilterEventBuilder.CreateTestEvent(id: 6, recordId: 400, source: "Zeta", timeCreated: early, activityId: guidHigh),
            FilterEventBuilder.CreateTestEvent(id: 6, recordId: 401, source: "Zeta", timeCreated: early, activityId: guidLow),

            FilterEventBuilder.CreateTestEvent(id: 7, source: "Eta", level: "Ledger", timeCreated: early, owningLog: "LogX"),
            FilterEventBuilder.CreateTestEvent(id: 7, source: "Eta", level: "Ledger", timeCreated: early, owningLog: "LogY")
        ];
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
}
