// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using System.Diagnostics;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.LogTable;

/// <summary>
///     Differential parity for the bulk column-scan sort kernel (
///     <see cref="ResolvedEventOrdering.SortColumnDirect" />): over the full 756-config sort matrix it must produce the
///     exact physical-index order that sorting the same survivors through the relocated array-of-structs reference
///     comparer (<see cref="AosReferenceOrdering.Reference" />) plus the same final physical-index ascending tie-break
///     produces. A <see cref="LegacyEventColumnReader" /> feeds both sides the same corpus, so any divergence isolates the
///     kernel. A perf smoke test guards that the kernel finishes on a large synthetic corpus.
/// </summary>
public sealed class ColumnDirectSortKernelTests(ITestOutputHelper output)
{
    private static readonly ColumnName[] s_allColumns = Enum.GetValues<ColumnName>();
    private static readonly bool[] s_bools = [false, true];
    private static readonly IReadOnlyList<SortConfig> s_allConfigs = BuildAllConfigs();

    private static readonly IReadOnlyList<ResolvedEvent> s_edgeCorpus = BuildEdgeCorpus();
    private static readonly IReadOnlyList<ResolvedEvent> s_tieBurstCorpus = BuildTieBurstCorpus();

    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void MatrixCovers756Configs_AcrossOrderGroupAscendingDescending()
    {
        Assert.Equal(756, s_allConfigs.Count);
        Assert.Contains(s_allConfigs, config => config.OrderBy is null && config.IsDescending && config.GroupBy is null);
        Assert.Contains(s_allConfigs, config => config.OrderBy is null && config.IsDescending && config.GroupBy is not null);
    }

    [Fact]
    public void SortColumnDirect_CompletesWithinBudget_OnLargeSyntheticCorpus()
    {
        const int eventCount = 200_000;
        const int budgetMilliseconds = 20_000;
        IReadOnlyList<ResolvedEvent> corpus = BuildPerfCorpus(eventCount);
        var reader = NewReader(corpus);
        int[] survivors = AllIndices(eventCount);

        // A representative spread: ungrouped default, an ordered descending column, and a grouped-plus-ordered chain.
        SortConfig[] configs =
        [
            new SortConfig(null, false, null, false),
            new SortConfig(ColumnName.DateAndTime, true, null, false),
            new SortConfig(ColumnName.Level, false, ColumnName.Source, true)
        ];

        foreach (SortConfig config in configs)
        {
            var stopwatch = Stopwatch.StartNew();
            int[] sorted = ResolvedEventOrdering.SortColumnDirect(
                reader, survivors, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);
            stopwatch.Stop();

            Assert.Equal(eventCount, sorted.Length);
            _output.WriteLine($"SortColumnDirect {config}: {stopwatch.ElapsedMilliseconds} ms for {eventCount:N0} events");

            Assert.True(
                stopwatch.ElapsedMilliseconds < budgetMilliseconds,
                $"{config} took {stopwatch.ElapsedMilliseconds} ms, over the {budgetMilliseconds} ms budget");
        }

        // Log the array-of-structs baseline for context only; a strict "faster than AoS" assert is too flaky for CI.
        var baseline = Stopwatch.StartNew();
        _ = AosReferenceOrdering.Order(corpus, ColumnName.DateAndTime, isDescending: true).Length;
        baseline.Stop();
        _output.WriteLine($"AosReferenceOrdering.Order (reference baseline, DateAndTime desc): {baseline.ElapsedMilliseconds} ms for {eventCount:N0} events");
    }

    [Fact]
    public void SortColumnDirect_MatchesOracleOrder_ForEveryConfig_OverEdgeCorpus()
    {
        AssertParityForEveryConfig(s_edgeCorpus, AllIndices(s_edgeCorpus.Count), "edge");
    }

    [Fact]
    public void SortColumnDirect_MatchesOracleOrder_ForEveryConfig_OverFilteredSurvivors()
    {
        // A non-contiguous, shuffled survivor subset (like a filter result): the kernel must still return the canonical
        // order over exactly those physical rows, independent of the input order.
        int[] survivors = FilteredSurvivors(s_edgeCorpus.Count);

        AssertParityForEveryConfig(s_edgeCorpus, survivors, "filtered-survivors");
    }

    [Fact]
    public void SortColumnDirect_MatchesOracleOrder_ForEveryConfig_OverNullRecordIdTieBursts()
    {
        // Full-tie bursts (identical events, null RecordId) plus equal-primary/equal-OwningLog groups exercise M4: the
        // chain ties all the way through RecordId and OwningLog, so only the final physical-index tie-break separates them.
        AssertParityForEveryConfig(s_tieBurstCorpus, AllIndices(s_tieBurstCorpus.Count), "tie-burst");
    }

    [Fact]
    public void SortColumnDirect_ReturnsPermutationOfSurvivors_ForEveryConfig()
    {
        var reader = NewReader(s_edgeCorpus);
        int[] survivors = FilteredSurvivors(s_edgeCorpus.Count);

        foreach (SortConfig config in s_allConfigs)
        {
            int[] sorted = ResolvedEventOrdering.SortColumnDirect(
                reader, survivors, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);

            Assert.Equal(survivors.Length, sorted.Length);
            Assert.Equal(survivors.OrderBy(index => index), sorted.OrderBy(index => index));
        }
    }

    private static int[] AllIndices(int count) => Enumerable.Range(0, count).ToArray();

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

        // Oracle-vs-kernel edge: do not pair a null and an empty-string value in the same string column here. The
        // AosReferenceOrdering oracle is struct-based and sorts null before "" (raw string.Compare), while the live
        // SortColumnDirect reads through the reader (AsString collapses null to "") and ties them, so pairing the two
        // in one column would fail this parity test spuriously. No current row does so.
        return
        [
            FilterEventBuilder.CreateTestEvent(id: 2, recordId: 2, processId: 2, threadId: 2, source: "Alpha", level: "Information", timeCreated: early, activityId: guidLow, userId: sidLow),
            FilterEventBuilder.CreateTestEvent(id: 10, recordId: 10, processId: 10, threadId: 10, source: "Alpha", level: "Information", timeCreated: early, activityId: guidHigh, userId: sidHigh),
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

    private static IReadOnlyList<ResolvedEvent> BuildPerfCorpus(int count)
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

    private static IReadOnlyList<ResolvedEvent> BuildTieBurstCorpus()
    {
        var when = new DateTime(2024, 3, 3, 3, 3, 3, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>();

        // Six fully identical events with null RecordId: every chain (incl. OwningLog) ties, so only the physical index
        // separates them.
        for (int index = 0; index < 6; index++)
        {
            events.Add(FilterEventBuilder.CreateTestEvent(
                id: 9, source: "Same", level: "Same", timeCreated: when, owningLog: "SameLog"));
        }

        // Same primary, null RecordId, distinct OwningLog: the OwningLog tie-break separates them before the index step.
        events.Add(FilterEventBuilder.CreateTestEvent(id: 9, source: "Same", level: "Same", timeCreated: when, owningLog: "LogB"));
        events.Add(FilterEventBuilder.CreateTestEvent(id: 9, source: "Same", level: "Same", timeCreated: when, owningLog: "LogA"));

        // Same primary and OwningLog with distinct RecordId: the RecordId tie-break separates them.
        events.Add(FilterEventBuilder.CreateTestEvent(id: 9, recordId: 20, source: "Same", level: "Same", timeCreated: when, owningLog: "SameLog"));
        events.Add(FilterEventBuilder.CreateTestEvent(id: 9, recordId: 10, source: "Same", level: "Same", timeCreated: when, owningLog: "SameLog"));

        // A couple of distinct rows so grouped/ordered configs still see more than one group.
        events.Add(FilterEventBuilder.CreateTestEvent(id: 1, recordId: 1, source: "Other", level: "Info", timeCreated: when.AddHours(1), owningLog: "SameLog"));
        events.Add(FilterEventBuilder.CreateTestEvent(id: 2, source: "Other", level: "Info", timeCreated: when.AddHours(2), owningLog: "SameLog"));

        return events;
    }

    private static string Describe(IReadOnlyList<string> failures) =>
        $"{failures.Count} divergence(s):{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(25))}";

    private static int[] FilteredSurvivors(int count)
    {
        // Every physical row except a couple, handed to the kernel in a shuffled (non-sorted) order.
        var survivors = Enumerable.Range(0, count).Where(index => index != 1 && index != 4).ToList();
        var shuffled = new List<int>(survivors);

        for (int index = 0; index < shuffled.Count; index += 2)
        {
            int swap = shuffled.Count - 1 - index;

            if (swap > index) { (shuffled[index], shuffled[swap]) = (shuffled[swap], shuffled[index]); }
        }

        return shuffled.ToArray();
    }

    private static LegacyEventColumnReader NewReader(IReadOnlyList<ResolvedEvent> corpus) =>
        new(EventLogId.Create(), generation: 1, contentVersion: 1, corpus);

    // The oracle order: sort the survivors through the relocated array-of-structs reference comparer, then break residual
    // ties by physical index ascending (the same total-order completion the kernel appends), so the comparison is
    // well-defined on ties.
    private static int[] OracleOrder(IReadOnlyList<ResolvedEvent> corpus, int[] survivors, SortConfig config)
    {
        Comparison<ResolvedEvent> chain = AosReferenceOrdering.Reference(
            config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);
        int[] order = (int[])survivors.Clone();

        Array.Sort(order, (a, b) =>
        {
            int compared = chain(corpus[a], corpus[b]);

            return compared != 0 ? compared : a.CompareTo(b);
        });

        return order;
    }

    private static IEnumerable<ColumnName?> OrderByOptions()
    {
        yield return null;

        foreach (ColumnName column in s_allColumns)
        {
            yield return column;
        }
    }

    private void AssertParityForEveryConfig(IReadOnlyList<ResolvedEvent> corpus, int[] survivors, string label)
    {
        var reader = NewReader(corpus);
        var failures = new List<string>();

        foreach (SortConfig config in s_allConfigs)
        {
            int[] actual = ResolvedEventOrdering.SortColumnDirect(
                reader, survivors, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);
            int[] expected = OracleOrder(corpus, survivors, config);

            if (!actual.SequenceEqual(expected))
            {
                failures.Add($"{label} {config}: expected [{string.Join(",", expected)}] but kernel gave [{string.Join(",", actual)}]");
            }
        }

        Assert.True(failures.Count == 0, Describe(failures));
    }

    private readonly record struct SortConfig(ColumnName? OrderBy, bool IsDescending, ColumnName? GroupBy, bool IsGroupDescending);
}
