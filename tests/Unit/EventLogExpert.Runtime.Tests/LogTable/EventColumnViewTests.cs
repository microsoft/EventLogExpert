// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.LogTable;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.LogTable;

/// <summary>
///     End-to-end display parity for <see cref="EventColumnView" /> over a REAL <see cref="EventColumnStore" />
///     (built and sealed into columnar chunks, so the view rehydrates from columns, not the original objects). Across the
///     full 756-config sort matrix the view must (a) report the corpus count, (b) display the same order as the
///     array-of-structs oracle <see cref="ResolvedEventOrdering.SortEvents" />, and (c) round-trip every
///     <see cref="DisplayRow.Loc" /> through <see cref="EventColumnView.Rank" /> and <c>GetDetail</c>. A filtered survivor
///     subset must display exactly those rows. The corpus uses distinct non-null RecordIds so both sort paths resolve to
///     one strict total order (the null-RecordId tie-burst corpus is <see cref="ColumnDirectSortKernelTests" />' job, not
///     this wrapper's).
/// </summary>
public sealed class EventColumnViewTests
{
    private static readonly ColumnName[] s_allColumns = Enum.GetValues<ColumnName>();
    private static readonly bool[] s_bools = [false, true];
    private static readonly IReadOnlyList<SortConfig> s_allConfigs = BuildAllConfigs();

    private static readonly EventLogId s_logId = EventLogId.Create();
    private static readonly IReadOnlyList<ResolvedEvent> s_corpus = BuildCorpus();

    private static readonly IEventColumnReader s_reader =
        EventColumnStore.Build(s_corpus, generation: 0, contentVersion: 0).CreateReader(s_logId);

    [Fact]
    public void Create_CountMatchesCorpus_ForEveryConfig()
    {
        int[] survivors = AllIndices();

        foreach (SortConfig config in s_allConfigs)
        {
            EventColumnView view = CreateView(survivors, config);

            Assert.Equal(s_corpus.Count, view.Count);
        }
    }

    [Fact]
    public void Create_FilteredSurvivors_DisplaysExactlyThatSubset()
    {
        int[] survivors = FilteredSurvivors();
        var survivorSet = survivors.ToHashSet();
        var config = new SortConfig(ColumnName.DateAndTime, IsDescending: false, GroupBy: null, IsGroupDescending: false);

        EventColumnView view = CreateView(survivors, config);
        IReadOnlyList<DisplayRow> displayed = view.Slice(0, view.Count);

        Assert.Equal(survivors.Length, view.Count);
        Assert.Equal(survivors.Length, displayed.Count);

        // The displayed physical rows are EXACTLY the survivor set (order-independent set equality).
        Assert.Equal(survivorSet, displayed.Select(row => row.Loc.Index).ToHashSet());

        // The displayed order still matches the oracle sorted over just the survivor events.
        IReadOnlyList<ResolvedEvent> oracle = survivors.Select(index => s_corpus[index]).SortEvents(config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);

        for (int displayIndex = 0; displayIndex < oracle.Count; displayIndex++)
        {
            Assert.True(SameValueIdentity(oracle[displayIndex], displayed[displayIndex].Lean));
        }

        // A filtered-out physical row ranks -1; a surviving one ranks to a display slot that maps back to it.
        for (int physical = 0; physical < s_corpus.Count; physical++)
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
        Assert.Equal(756, s_allConfigs.Count);
    }

    [Fact]
    public void Rank_LocatorOutsideView_ReturnsMinusOne()
    {
        EventColumnView view = CreateView(AllIndices(), new SortConfig(null, IsDescending: false, GroupBy: null, IsGroupDescending: false));

        Assert.Equal(-1, view.Rank(new EventLocator(EventLogId.Create(), 0, 0)));
        Assert.Equal(-1, view.Rank(new EventLocator(s_logId, 999, 0)));
        Assert.Equal(-1, view.Rank(new EventLocator(s_logId, 0, s_corpus.Count)));
        Assert.Equal(-1, view.Rank(new EventLocator(s_logId, 0, -1)));
    }

    [Fact]
    public void Slice_MatchesAoSOracleOrder_ForEveryConfig()
    {
        int[] survivors = AllIndices();
        var failures = new List<string>();

        foreach (SortConfig config in s_allConfigs)
        {
            EventColumnView view = CreateView(survivors, config);
            IReadOnlyList<DisplayRow> displayed = view.Slice(0, view.Count);
            IReadOnlyList<ResolvedEvent> oracle = s_corpus.SortEvents(config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);

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
            EventColumnView view = CreateView(survivors, config);
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
        EventColumnView view = CreateView(AllIndices(), config);
        IReadOnlyList<ResolvedEvent> oracle = s_corpus.SortEvents(config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);

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

    private static int[] AllIndices() => Enumerable.Range(0, s_corpus.Count).ToArray();

    // Test-local copy of the ColumnDirectSortKernelTests config matrix: the full order x group x asc/desc cross-product.
    // Kept independent of the kernel suite's private fixture so the two suites do not couple.
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

    private static IReadOnlyList<ResolvedEvent> BuildCorpus()
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

        // Distinct non-null RecordIds (1..16): the RecordId tie-break is the ultimate arbiter of both the AoS and the
        // column-direct chains, so a unique RecordId makes the total order deterministic and identical between them. Every
        // other column repeats to exercise per-column ties that resolve through that shared tie-break.
        return
        [
            FilterEventBuilder.CreateTestEvent(id: 5, recordId: 1, source: "Alpha", level: "Information", computerName: "Host-A", logName: "AppLog", taskCategory: "Cat-A", keywords: ["K1"], processId: 30, threadId: 9, activityId: guidA, userId: sidLow, timeCreated: middle),
            FilterEventBuilder.CreateTestEvent(id: 5, recordId: 2, source: "Beta", level: "Error", computerName: "Host-B", logName: "SysLog", taskCategory: "Cat-B", keywords: ["K2"], processId: 10, threadId: 1, activityId: guidC, userId: sidHigh, timeCreated: early),
            FilterEventBuilder.CreateTestEvent(id: 1, recordId: 3, source: "Gamma", level: "Warning", computerName: "Host-C", logName: "SecLog", taskCategory: "Cat-C", keywords: [], processId: null, threadId: 4, activityId: null, userId: sidMid, timeCreated: late),
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

    private static EventColumnView CreateView(ReadOnlySpan<int> survivors, SortConfig config) =>
        EventColumnView.Create(s_reader, survivors, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);

    private static string Describe(IReadOnlyList<string> failures) =>
        $"{failures.Count} divergence(s):{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(25))}";

    private static int[] FilteredSurvivors()
    {
        // A non-contiguous, shuffled subset of physical rows (like a filter result): drop two indices and reverse the rest
        // so the kernel must impose the canonical order regardless of the survivor input order.
        List<int> survivors = Enumerable.Range(0, s_corpus.Count).Where(index => index != 2 && index != 11).ToList();
        survivors.Reverse();

        return survivors.ToArray();
    }

    private static IEnumerable<ColumnName?> OrderByOptions()
    {
        yield return null;

        foreach (ColumnName column in s_allColumns) { yield return column; }
    }

    private static bool SameValueIdentity(ResolvedEvent expected, ResolvedEvent actual) =>
        expected.RecordId == actual.RecordId
        && expected.Id == actual.Id
        && expected.TimeCreated == actual.TimeCreated
        && string.Equals(expected.Source, actual.Source, StringComparison.Ordinal)
        && string.Equals(expected.Level, actual.Level, StringComparison.Ordinal);

    private readonly record struct SortConfig(ColumnName? OrderBy, bool IsDescending, ColumnName? GroupBy, bool IsGroupDescending);
}
