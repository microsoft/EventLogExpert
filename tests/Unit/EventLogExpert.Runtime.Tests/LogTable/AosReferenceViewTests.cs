// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using System.Security.Principal;
using static EventLogExpert.Runtime.Tests.LogTable.TestSupport.DivergenceReport;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class AosReferenceViewTests
{
    private static readonly IReadOnlyList<SortConfig> s_allConfigs = SortConfigMatrix.All();
    private static readonly EventLogId s_logId = EventLogId.Create();
    private static readonly IReadOnlyList<ResolvedEvent> s_sample = BuildSample();
    private static readonly IEventColumnReader s_reader =
        EventColumnStore.Build(s_sample, generation: 0, contentVersion: 0).CreateReader(s_logId);

    [Fact]
    public void Create_CountMatchesSample_ForEveryConfig()
    {
        int[] survivors = AllIndices();

        foreach (SortConfig config in s_allConfigs)
        {
            AosReferenceView view = CreateView(survivors, config);

            Assert.Equal(s_sample.Count, view.Count);
        }
    }

    [Fact]
    public void Create_FilteredSurvivors_DisplaysExactlyThatSubset()
    {
        int[] survivors = FilteredSurvivors();
        var survivorSet = survivors.ToHashSet();
        var config = new SortConfig(ColumnName.DateAndTime, IsDescending: false, GroupBy: null, IsGroupDescending: false);

        AosReferenceView view = CreateView(survivors, config);
        IReadOnlyList<DisplayRow> displayed = view.Slice(0, view.Count);

        Assert.Equal(survivors.Length, view.Count);
        Assert.Equal(survivors.Length, displayed.Count);

        Assert.Equal(survivorSet, displayed.Select(row => row.Loc.Index).ToHashSet());

        IReadOnlyList<ResolvedEvent> oracle = AosReferenceOrdering.OrderedEvents(survivors.Select(index => s_sample[index]), config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);

        for (int displayIndex = 0; displayIndex < oracle.Count; displayIndex++)
        {
            Assert.True(SameValueIdentity(oracle[displayIndex], displayed[displayIndex].Lean));
        }

        for (int physical = 0; physical < s_sample.Count; physical++)
        {
            int rank = view.Rank(s_reader.LocatorAt(physical));

            if (survivorSet.Contains(physical))
            {
                Assert.InRange(rank, 0, view.Count - 1);
                Assert.Equal(physical, view.LocatorAt(rank).Index);
            }
            else
            {
                Assert.Equal(-1, rank);
            }
        }
    }

    [Fact]
    public void MatrixIsFullCrossProduct()
    {
        Assert.Equal(870, s_allConfigs.Count);
    }

    [Fact]
    public void Rank_LocatorOutsideView_ReturnsMinusOne()
    {
        AosReferenceView view = CreateView(AllIndices(), new SortConfig(null, IsDescending: false, GroupBy: null, IsGroupDescending: false));

        Assert.Equal(-1, view.Rank(new EventLocator(EventLogId.Create(), 0, 0)));
        Assert.Equal(-1, view.Rank(new EventLocator(s_logId, 999, 0)));
        Assert.Equal(-1, view.Rank(new EventLocator(s_logId, 0, s_sample.Count)));
        Assert.Equal(-1, view.Rank(new EventLocator(s_logId, 0, -1)));
    }

    [Fact]
    public void Slice_MatchesAoSOracleOrder_ForEveryConfig()
    {
        int[] survivors = AllIndices();
        var failures = new List<string>();

        foreach (SortConfig config in s_allConfigs)
        {
            AosReferenceView view = CreateView(survivors, config);
            IReadOnlyList<DisplayRow> displayed = view.Slice(0, view.Count);
            IReadOnlyList<ResolvedEvent> oracle = AosReferenceOrdering.OrderedEvents(s_sample, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);

            if (displayed.Count != oracle.Count)
            {
                failures.Add($"{config}: displayed {displayed.Count} rows, oracle {oracle.Count}");
                continue;
            }

            for (int index = 0; index < oracle.Count; index++)
            {
                if (!SameValueIdentity(oracle[index], displayed[index].Lean))
                {
                    failures.Add($"{config} at {index}: oracle RecordId {oracle[index].RecordId} != displayed {displayed[index].Lean.RecordId}");
                    break;
                }
            }
        }

        Assert.True(failures.Count == 0, Describe(failures));
    }

    [Fact]
    public void Slice_RowLocatorsRoundTrip_ForEveryConfig()
    {
        int[] survivors = AllIndices();
        var failures = new List<string>();

        foreach (SortConfig config in s_allConfigs)
        {
            AosReferenceView view = CreateView(survivors, config);
            IReadOnlyList<DisplayRow> displayed = view.Slice(0, view.Count);

            for (int displayIndex = 0; displayIndex < displayed.Count; displayIndex++)
            {
                DisplayRow row = displayed[displayIndex];

                if (view.Rank(row.Loc) != displayIndex)
                {
                    failures.Add($"{config} at {displayIndex}: Rank returned {view.Rank(row.Loc)}");
                    break;
                }

                if (!SameValueIdentity(s_reader.GetDetail(row.Loc), row.Lean))
                {
                    failures.Add($"{config} at {displayIndex}: GetDetail(row.Loc) disagrees with the lean row");
                    break;
                }
            }
        }

        Assert.True(failures.Count == 0, Describe(failures));
    }

    [Fact]
    public void Slice_WithStartOffset_MapsWindowToDisplayPositions()
    {
        const int start = 3;
        const int count = 4;
        var config = new SortConfig(ColumnName.RecordId, IsDescending: false, GroupBy: null, IsGroupDescending: false);
        AosReferenceView view = CreateView(AllIndices(), config);
        IReadOnlyList<ResolvedEvent> oracle = AosReferenceOrdering.OrderedEvents(s_sample, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);

        IReadOnlyList<DisplayRow> window = view.Slice(start, count);

        Assert.Equal(count, window.Count);

        for (int offset = 0; offset < window.Count; offset++)
        {
            int displayIndex = start + offset;
            Assert.True(SameValueIdentity(oracle[displayIndex], window[offset].Lean));
            Assert.Equal(displayIndex, view.Rank(window[offset].Loc));
            Assert.Equal(view.LocatorAt(displayIndex), window[offset].Loc);
        }
    }

    private static int[] AllIndices() => Enumerable.Range(0, s_sample.Count).ToArray();

    private static IReadOnlyList<ResolvedEvent> BuildSample()
    {
        var guidA = new Guid("00000001-0000-0000-0000-000000000000");
        var guidB = new Guid("00000002-0000-0000-0000-000000000000");
        var guidC = new Guid("ffffffff-0000-0000-0000-000000000000");
        var sidLow = new SecurityIdentifier("S-1-5-18");
        var sidMid = new SecurityIdentifier("S-1-5-19");
        var sidHigh = new SecurityIdentifier("S-1-5-21-1-2-3-1001");
        var early = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var middle = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var late = new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        return
        [
            FilterEventBuilder.CreateTestEvent(id: 5, recordId: 1, source: "Alpha", level: "Information", computerName: "Host-A", logName: "AppLog", taskCategory: "Cat-A", keywords: ["K1"], processId: 30, threadId: 9, activityId: guidA, userId: sidLow, timeCreated: middle, opcode: "Start"),
            FilterEventBuilder.CreateTestEvent(id: 5, recordId: 2, source: "Beta", level: "Error", computerName: "Host-B", logName: "SysLog", taskCategory: "Cat-B", keywords: ["K2"], processId: 10, threadId: 1, activityId: guidC, userId: sidHigh, timeCreated: early, opcode: "Stop"),
            FilterEventBuilder.CreateTestEvent(id: 1, recordId: 3, source: "Gamma", level: "Warning", computerName: "Host-C", logName: "SecLog", taskCategory: "Cat-C", keywords: [], processId: null, threadId: 4, activityId: null, userId: sidMid, timeCreated: late, opcode: "Info"),
            FilterEventBuilder.CreateTestEvent(id: 3, recordId: 4, source: "Alpha", level: "Critical", computerName: "Host-A", logName: "AppLog", taskCategory: "Cat-B", keywords: ["K1", "K3"], processId: 30, threadId: null, activityId: guidB, userId: sidLow, timeCreated: early),
            FilterEventBuilder.CreateTestEvent(id: 3, recordId: 5, source: "Delta", level: "Information", computerName: "Host-B", logName: "SysLog", taskCategory: "Cat-A", keywords: ["K2"], processId: 22, threadId: 9, activityId: guidA, userId: sidHigh, timeCreated: middle),
            FilterEventBuilder.CreateTestEvent(id: 8, recordId: 6, source: "Beta", level: "Warning", computerName: "Host-C", logName: "AppLog", taskCategory: "Cat-C", keywords: ["K1"], processId: null, threadId: 1, activityId: guidC, userId: sidMid, timeCreated: late),
            FilterEventBuilder.CreateTestEvent(id: 2, recordId: 7, source: "Gamma", level: "Error", computerName: "Host-A", logName: "SecLog", taskCategory: "Cat-A", keywords: [], processId: 15, threadId: 4, activityId: null, userId: sidLow, timeCreated: early, owningLog: "OtherLog"),
            FilterEventBuilder.CreateTestEvent(id: 8, recordId: 8, source: "Epsilon", level: "Critical", computerName: "Host-B", logName: "SysLog", taskCategory: "Cat-B", keywords: ["K3"], processId: 30, threadId: 7, activityId: guidB, userId: sidHigh, timeCreated: middle),
            FilterEventBuilder.CreateTestEvent(id: 1, recordId: 9, source: "Alpha", level: "Information", computerName: "Host-C", logName: "AppLog", taskCategory: "Cat-C", keywords: ["K1"], processId: 22, threadId: null, activityId: guidA, userId: sidMid, timeCreated: late),
            FilterEventBuilder.CreateTestEvent(id: 6, recordId: 10, source: "Delta", level: "Warning", computerName: "Host-A", logName: "SecLog", taskCategory: "Cat-A", keywords: ["K2"], processId: null, threadId: 9, activityId: null, userId: sidLow, timeCreated: early),
            FilterEventBuilder.CreateTestEvent(id: 6, recordId: 11, source: "Zeta", level: "Error", computerName: "Host-B", logName: "AppLog", taskCategory: "Cat-B", keywords: [], processId: 10, threadId: 1, activityId: guidC, userId: sidHigh, timeCreated: middle, owningLog: "OtherLog"),
            FilterEventBuilder.CreateTestEvent(id: 4, recordId: 12, source: "Beta", level: "Critical", computerName: "Host-C", logName: "SysLog", taskCategory: "Cat-C", keywords: ["K1", "K2"], processId: 30, threadId: 4, activityId: guidB, userId: sidMid, timeCreated: late),
            FilterEventBuilder.CreateTestEvent(id: 4, recordId: 13, source: "Gamma", level: "Information", computerName: "Host-A", logName: "AppLog", taskCategory: "Cat-A", keywords: ["K3"], processId: 22, threadId: 7, activityId: guidA, userId: sidLow, timeCreated: early),
            FilterEventBuilder.CreateTestEvent(id: 9, recordId: 14, source: "Alpha", level: "Warning", computerName: "Host-B", logName: "SecLog", taskCategory: "Cat-B", keywords: ["K2"], processId: null, threadId: null, activityId: null, userId: sidHigh, timeCreated: middle),
            FilterEventBuilder.CreateTestEvent(id: 2, recordId: 15, source: "Delta", level: "Error", computerName: "Host-C", logName: "SysLog", taskCategory: "Cat-C", keywords: ["K1"], processId: 15, threadId: 9, activityId: guidC, userId: sidMid, timeCreated: late),
            FilterEventBuilder.CreateTestEvent(id: 7, recordId: 16, source: "Epsilon", level: "Information", computerName: "Host-A", logName: "AppLog", taskCategory: "Cat-A", keywords: [], processId: 30, threadId: 1, activityId: guidB, userId: sidLow, timeCreated: early)
        ];
    }

    private static AosReferenceView CreateView(ReadOnlySpan<int> survivors, SortConfig config) =>
        AosReferenceView.Create(s_reader, survivors, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);

    private static int[] FilteredSurvivors()
    {
        List<int> survivors = Enumerable.Range(0, s_sample.Count).Where(index => index != 2 && index != 11).ToList();
        survivors.Reverse();

        return survivors.ToArray();
    }

    private static bool SameValueIdentity(ResolvedEvent expected, ResolvedEvent actual) =>
        expected.RecordId == actual.RecordId
        && expected.Id == actual.Id
        && expected.TimeCreated == actual.TimeCreated
        && string.Equals(expected.Source, actual.Source, StringComparison.Ordinal)
        && string.Equals(expected.Level, actual.Level, StringComparison.Ordinal);
}
