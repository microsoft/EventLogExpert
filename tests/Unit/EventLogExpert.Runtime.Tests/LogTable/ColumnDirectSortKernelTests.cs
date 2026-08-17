// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using System.Diagnostics;
using System.Security.Principal;
using static EventLogExpert.Runtime.Tests.LogTable.TestSupport.DivergenceReport;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class ColumnDirectSortKernelTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<SortConfig> s_allConfigs = SortConfigMatrix.All();
    private static readonly IReadOnlyList<ResolvedEvent> s_edgeSample = BuildEdgeSample();
    private static readonly IReadOnlyList<ResolvedEvent> s_tieBurstSample = BuildTieBurstSample();

    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void MatrixCovers870Configs_AcrossOrderGroupAscendingDescending()
    {
        Assert.Equal(870, s_allConfigs.Count);
        Assert.Contains(s_allConfigs, config => config.OrderBy is null && config is { IsDescending: true, GroupBy: null });
        Assert.Contains(s_allConfigs, config => config.OrderBy is null && config is { IsDescending: true, GroupBy: not null });
    }

    [Fact]
    public void SortColumnDirect_CompletesWithinBudget_OnLargeSyntheticSample()
    {
        const int EventCount = 200_000;
        const int BudgetMilliseconds = 20_000;
        IReadOnlyList<ResolvedEvent> sample = BuildPerfSample(EventCount);
        var reader = ColumnReaderTestFactory.ReaderOver(sample);
        int[] survivors = AllIndices(EventCount);

        SortConfig[] configs =
        [
            new SortConfig(null, false, null, false),
            new SortConfig(ColumnName.DateAndTime, true, null, false),
            new SortConfig(ColumnName.Level, false, ColumnName.Source, true)
        ];

        foreach (SortConfig config in configs)
        {
            var stopwatch = Stopwatch.StartNew();
            int[] sorted = ColumnDirectSort.SortColumnDirect(
                reader, survivors, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending, TestContext.Current.CancellationToken);
            stopwatch.Stop();

            Assert.Equal(EventCount, sorted.Length);
            _output.WriteLine($"SortColumnDirect {config}: {stopwatch.ElapsedMilliseconds} ms for {EventCount:N0} events");

            Assert.True(
                stopwatch.ElapsedMilliseconds < BudgetMilliseconds,
                $"{config} took {stopwatch.ElapsedMilliseconds} ms, over the {BudgetMilliseconds} ms budget");
        }

        var baseline = Stopwatch.StartNew();
        _ = AosReferenceOrdering.Order(sample, ColumnName.DateAndTime, isDescending: true).Length;
        baseline.Stop();
        _output.WriteLine($"AosReferenceOrdering.Order (reference baseline, DateAndTime desc): {baseline.ElapsedMilliseconds} ms for {EventCount:N0} events");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SortColumnDirect_FullTieSample_ReordersToAscendingIndexTieBreak_RegardlessOfDescending(bool isDescending)
    {
        var when = new DateTime(2024, 3, 3, 3, 3, 3, DateTimeKind.Utc);
        IReadOnlyList<ResolvedEvent> sample =
        [
            FilterEventBuilder.CreateTestEvent(id: 9, source: "Same", level: "Same", timeCreated: when, owningLog: "L"),
            FilterEventBuilder.CreateTestEvent(id: 9, source: "Same", level: "Same", timeCreated: when, owningLog: "L"),
            FilterEventBuilder.CreateTestEvent(id: 9, source: "Same", level: "Same", timeCreated: when, owningLog: "L"),
            FilterEventBuilder.CreateTestEvent(id: 9, source: "Same", level: "Same", timeCreated: when, owningLog: "L")
        ];
        var reader = ColumnReaderTestFactory.ReaderOver(sample);
        int[] survivors = [3, 2, 1, 0];

        int[] order = ColumnDirectSort.SortColumnDirect(
            reader, survivors, ColumnName.DateAndTime, isDescending, groupBy: null, isGroupDescending: false, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { 0, 1, 2, 3 }, order);
    }

    [Theory]
    [InlineData(false, false, new[] { 0, 1, 2, 3 })]
    [InlineData(true, false, new[] { 1, 0, 3, 2 })]
    [InlineData(false, true, new[] { 2, 3, 0, 1 })]
    [InlineData(true, true, new[] { 3, 2, 1, 0 })]
    public void SortColumnDirect_GroupedChain_NegatesGroupAndWithinOrderIndependently(
        bool isWithinDescending, bool isGroupDescending, int[] expectedOrder)
    {
        var when = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        IReadOnlyList<ResolvedEvent> sample =
        [
            FilterEventBuilder.CreateTestEvent(id: 1, recordId: 1, source: "Alpha", timeCreated: when, owningLog: "L"),
            FilterEventBuilder.CreateTestEvent(id: 1, recordId: 2, source: "Alpha", timeCreated: when, owningLog: "L"),
            FilterEventBuilder.CreateTestEvent(id: 1, recordId: 3, source: "Beta", timeCreated: when, owningLog: "L"),
            FilterEventBuilder.CreateTestEvent(id: 1, recordId: 4, source: "Beta", timeCreated: when, owningLog: "L")
        ];
        var reader = ColumnReaderTestFactory.ReaderOver(sample);

        int[] order = ColumnDirectSort.SortColumnDirect(
            reader, AllIndices(sample.Count), ColumnName.RecordId, isWithinDescending, ColumnName.Source, isGroupDescending, TestContext.Current.CancellationToken);

        Assert.Equal(expectedOrder, order);
    }

    [Fact]
    public void SortColumnDirect_MatchesOracleOrder_ForEveryConfig_OverEdgeSample()
    {
        AssertParityForEveryConfig(s_edgeSample, AllIndices(s_edgeSample.Count), "edge");
    }

    [Fact]
    public void SortColumnDirect_MatchesOracleOrder_ForEveryConfig_OverFilteredSurvivors()
    {
        int[] survivors = FilteredSurvivors(s_edgeSample.Count);

        AssertParityForEveryConfig(s_edgeSample, survivors, "filtered-survivors");
    }

    [Fact]
    public void SortColumnDirect_MatchesOracleOrder_ForEveryConfig_OverNullRecordIdTieBursts()
    {
        AssertParityForEveryConfig(s_tieBurstSample, AllIndices(s_tieBurstSample.Count), "tie-burst");
    }

    [Fact]
    public void SortColumnDirect_ReturnsPermutationOfSurvivors_ForEveryConfig()
    {
        var reader = ColumnReaderTestFactory.ReaderOver(s_edgeSample);
        int[] survivors = FilteredSurvivors(s_edgeSample.Count);

        foreach (SortConfig config in s_allConfigs)
        {
            int[] sorted = ColumnDirectSort.SortColumnDirect(
                reader, survivors, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending, TestContext.Current.CancellationToken);

            Assert.Equal(survivors.Length, sorted.Length);
            Assert.Equal(survivors.OrderBy(index => index), sorted.OrderBy(index => index));
        }
    }

    private static int[] AllIndices(int count) => Enumerable.Range(0, count).ToArray();

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
            FilterEventBuilder.CreateTestEvent(id: 7, source: "Eta", level: "Ledger", timeCreated: early, owningLog: "LogY"),

            FilterEventBuilder.CreateTestEvent(id: 8, recordId: 500, source: "Theta", level: "", timeCreated: early),
            FilterEventBuilder.CreateTestEvent(id: 8, recordId: 501, source: "Theta", level: "", timeCreated: early)
        ];
    }

    private static IReadOnlyList<ResolvedEvent> BuildPerfSample(int count)
    {
        var guids = new[]
        {
            new Guid("00000001-0000-0000-0000-000000000000"),
            new Guid("00000002-0000-0000-0000-000000000000"),
            new Guid("00000003-0000-0000-0000-000000000000")
        };
        var sids = new[] { new SecurityIdentifier("S-1-5-18"), new SecurityIdentifier("S-1-5-19") };
        var levels = new[] { "Information", "Warning", "Error", "Critical" };
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>(count);

        for (int index = 0; index < count; index++)
        {
            events.Add(FilterEventBuilder.CreateTestEvent(
                id: index % 500,
                recordId: index % 7 == 0 ? null : index,
                source: "Src" + (index % 16),
                level: levels[index % 4],
                computerName: "Host" + (index % 8),
                logName: "Log" + (index % 5),
                taskCategory: "Task" + (index % 6),
                keywords: ["KW" + (index % 3)],
                processId: index % 11 == 0 ? null : index % 64,
                threadId: index % 13 == 0 ? null : index % 32,
                activityId: index % 9 == 0 ? null : guids[index % 3],
                userId: sids[index % 2],
                timeCreated: baseTime.AddSeconds(index % 9973)));
        }

        return events;
    }

    private static IReadOnlyList<ResolvedEvent> BuildTieBurstSample()
    {
        var when = new DateTime(2024, 3, 3, 3, 3, 3, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>();

        for (int index = 0; index < 6; index++)
        {
            events.Add(FilterEventBuilder.CreateTestEvent(
                id: 9, source: "Same", level: "Same", timeCreated: when, owningLog: "SameLog"));
        }

        events.Add(FilterEventBuilder.CreateTestEvent(id: 9, source: "Same", level: "Same", timeCreated: when, owningLog: "LogB"));
        events.Add(FilterEventBuilder.CreateTestEvent(id: 9, source: "Same", level: "Same", timeCreated: when, owningLog: "LogA"));

        events.Add(FilterEventBuilder.CreateTestEvent(id: 9, recordId: 20, source: "Same", level: "Same", timeCreated: when, owningLog: "SameLog"));
        events.Add(FilterEventBuilder.CreateTestEvent(id: 9, recordId: 10, source: "Same", level: "Same", timeCreated: when, owningLog: "SameLog"));

        events.Add(FilterEventBuilder.CreateTestEvent(id: 1, recordId: 1, source: "Other", level: "Info", timeCreated: when.AddHours(1), owningLog: "SameLog"));
        events.Add(FilterEventBuilder.CreateTestEvent(id: 2, source: "Other", level: "Info", timeCreated: when.AddHours(2), owningLog: "SameLog"));

        return events;
    }

    private static int[] FilteredSurvivors(int count)
    {
        var survivors = Enumerable.Range(0, count).Where(index => index != 1 && index != 4).ToList();
        var shuffled = new List<int>(survivors);

        for (int index = 0; index < shuffled.Count; index += 2)
        {
            int swap = shuffled.Count - 1 - index;

            if (swap > index) { (shuffled[index], shuffled[swap]) = (shuffled[swap], shuffled[index]); }
        }

        return shuffled.ToArray();
    }

    private static int[] OracleOrder(IReadOnlyList<ResolvedEvent> sample, int[] survivors, SortConfig config)
    {
        Comparison<ResolvedEvent> chain = AosReferenceOrdering.Reference(
            config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);
        int[] order = (int[])survivors.Clone();

        Array.Sort(order, (a, b) =>
        {
            int compared = chain(sample[a], sample[b]);

            return compared != 0 ? compared : a.CompareTo(b);
        });

        return order;
    }

    private void AssertParityForEveryConfig(IReadOnlyList<ResolvedEvent> sample, int[] survivors, string label)
    {
        var reader = ColumnReaderTestFactory.ReaderOver(sample);
        var failures = new List<string>();

        foreach (SortConfig config in s_allConfigs)
        {
            int[] actual = ColumnDirectSort.SortColumnDirect(
                reader, survivors, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending, TestContext.Current.CancellationToken);
            int[] expected = OracleOrder(sample, survivors, config);

            if (!actual.SequenceEqual(expected))
            {
                failures.Add($"{label} {config}: expected [{string.Join(",", expected)}] but kernel gave [{string.Join(",", actual)}]");
            }
        }

        Assert.True(failures.Count == 0, Describe(failures));
    }
}
