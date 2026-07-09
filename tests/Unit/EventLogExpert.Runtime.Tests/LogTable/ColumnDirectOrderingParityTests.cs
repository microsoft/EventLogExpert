// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Runtime.LogTable;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.LogTable;

/// <summary>
///     Differential parity between the live array-of-structs ordering oracle (
///     <see cref="ResolvedEventOrdering.SelectComparer" /> /
///     <see cref="ResolvedEventGroupKey.For(ColumnName, ResolvedEvent)" />) and the column-direct twins (
///     <see cref="ResolvedEventOrdering.SelectColumnComparer" /> /
///     <see cref="ResolvedEventGroupKey.For(IEventColumnReader, EventLocator, ColumnName)" /> /
///     <see cref="IEventColumnView.GroupKeyAt" />). A <see cref="LegacyEventColumnReader" /> reads the same
///     <see cref="ResolvedEvent" /> corpus, so any divergence isolates the column-side comparison/group-key logic rather
///     than the store.
/// </summary>
public sealed class ColumnDirectOrderingParityTests
{
    private static readonly ColumnName[] s_allColumns = Enum.GetValues<ColumnName>();
    private static readonly bool[] s_bools = [false, true];
    private static readonly IReadOnlyList<ResolvedEvent> s_edgeCorpus = BuildEdgeCorpus();
    private static readonly LegacyEventColumnView s_edgeView = NewView(s_edgeCorpus);
    private static readonly IReadOnlyList<ResolvedEvent> s_totalOrderCorpus = BuildTotalOrderCorpus();
    private static readonly LegacyEventColumnView s_totalOrderView = NewView(s_totalOrderCorpus);
    private static readonly IReadOnlyList<SortConfig> s_allConfigs = BuildAllConfigs();

    [Fact]
    public void AbsentNullableColumns_SortFirst_Ascending_InBothBackends()
    {
        var absent = FilterEventBuilder.CreateTestEvent(recordId: null, processId: null, threadId: null, activityId: null);
        var present = FilterEventBuilder.CreateTestEvent(recordId: 5, processId: 5, threadId: 5, activityId: Guid.NewGuid());
        var view = NewView([absent, present]);
        EventLocator absentLocator = view.LocatorAt(0);
        EventLocator presentLocator = view.LocatorAt(1);

        foreach (ColumnName column in new[] { ColumnName.RecordId, ColumnName.ProcessId, ColumnName.ThreadId, ColumnName.ActivityId })
        {
            Comparison<ResolvedEvent> oracle = ResolvedEventOrdering.SelectComparer(column, false, null, false);
            Comparison<EventLocator> direct = ResolvedEventOrdering.SelectColumnComparer(view.Reader, column, false, null, false);

            Assert.True(oracle(absent, present) < 0, $"oracle {column}: absent should sort first");
            Assert.True(oracle(present, absent) > 0, $"oracle {column}: present should sort after absent");
            Assert.True(direct(absentLocator, presentLocator) < 0, $"column {column}: absent should sort first");
            Assert.True(direct(presentLocator, absentLocator) > 0, $"column {column}: present should sort after absent");
        }
    }

    [Fact]
    public void ActivityId_UsesTypedGuidOrder_MatchingBothBackends()
    {
        // Guid.CompareTo on .NET treats _a/_b/_c as unsigned, so 00000001-... sorts BEFORE ffffffff-... — the same order as
        // the "D" string. Both backends must follow Guid.CompareTo (the oracle via Nullable.Compare(Guid?), the column side
        // via the typed CompareGuidNullable), never AsString().
        var low = new Guid("00000001-0000-0000-0000-000000000000");
        var high = new Guid("ffffffff-0000-0000-0000-000000000000");
        var lowEvent = FilterEventBuilder.CreateTestEvent(recordId: 1, activityId: low);
        var highEvent = FilterEventBuilder.CreateTestEvent(recordId: 2, activityId: high);
        var view = NewView([lowEvent, highEvent]);

        Comparison<ResolvedEvent> oracle = ResolvedEventOrdering.SelectComparer(ColumnName.ActivityId, false, null, false);
        Comparison<EventLocator> direct = ResolvedEventOrdering.SelectColumnComparer(view.Reader, ColumnName.ActivityId, false, null, false);
        int oracleSign = Math.Sign(oracle(lowEvent, highEvent));
        int columnSign = Math.Sign(direct(view.LocatorAt(0), view.LocatorAt(1)));

        Assert.Equal(oracleSign, columnSign);
        Assert.Equal(Math.Sign(low.CompareTo(high)), columnSign);
        Assert.Equal(
            Math.Sign(string.Compare(low.ToString("D"), high.ToString("D"), StringComparison.Ordinal)),
            Math.Sign(low.CompareTo(high)));
    }

    [Fact]
    public void GroupKeyAt_MatchesArrayOfStructsGroupKey_ForEveryColumnAndEvent()
    {
        var failures = new List<string>();

        for (int index = 0; index < s_edgeCorpus.Count; index++)
        {
            EventLocator locator = s_edgeView.LocatorAt(index);

            foreach (ColumnName column in s_allColumns)
            {
                string arrayOfStructsKey = ResolvedEventGroupKey.For(column, s_edgeCorpus[index]);
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
            Comparison<ResolvedEvent> comparer = ResolvedEventOrdering.SelectComparer(null, false, column, false);
            int[] order = Enumerable.Range(0, s_edgeCorpus.Count).ToArray();
            Array.Sort(order, (left, right) => comparer(s_edgeCorpus[left], s_edgeCorpus[right]));

            List<string> arrayOfStructsKeys = order.Select(index => ResolvedEventGroupKey.For(column, s_edgeCorpus[index])).ToList();
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

    [Fact]
    public void NumericColumns_CompareNumericallyNotLexically_InBothBackends()
    {
        // "10" sorts before "2" lexically, but 2 sorts before 10 numerically. Both backends must agree on the numeric order,
        // which proves the column side compares typed Int64 values rather than AsString().
        var small = FilterEventBuilder.CreateTestEvent(id: 2, recordId: 2, processId: 2, threadId: 2);
        var large = FilterEventBuilder.CreateTestEvent(id: 10, recordId: 10, processId: 10, threadId: 10);
        var view = NewView([small, large]);

        Assert.True(string.Compare("10", "2", StringComparison.Ordinal) < 0);

        foreach (ColumnName column in new[] { ColumnName.EventId, ColumnName.RecordId, ColumnName.ProcessId, ColumnName.ThreadId })
        {
            Comparison<ResolvedEvent> oracle = ResolvedEventOrdering.SelectComparer(column, false, null, false);
            Comparison<EventLocator> direct = ResolvedEventOrdering.SelectColumnComparer(view.Reader, column, false, null, false);

            Assert.True(oracle(small, large) < 0, $"oracle {column}: 2 should sort before 10");
            Assert.True(direct(view.LocatorAt(0), view.LocatorAt(1)) < 0, $"column {column}: 2 should sort before 10");
        }
    }

    [Fact]
    public void SelectColumnComparer_MatchesOracleSign_ForEveryPairAndConfig()
    {
        var failures = new List<string>();

        foreach (SortConfig config in s_allConfigs)
        {
            Comparison<ResolvedEvent> oracle =
                ResolvedEventOrdering.SelectComparer(config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);
            Comparison<EventLocator> direct = ResolvedEventOrdering.SelectColumnComparer(
                s_edgeView.Reader, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);

            for (int i = 0; i < s_edgeCorpus.Count; i++)
            {
                EventLocator left = s_edgeView.LocatorAt(i);

                for (int j = 0; j < s_edgeCorpus.Count; j++)
                {
                    int oracleSign = Math.Sign(oracle(s_edgeCorpus[i], s_edgeCorpus[j]));
                    int columnSign = Math.Sign(direct(left, s_edgeView.LocatorAt(j)));

                    if (oracleSign != columnSign)
                    {
                        failures.Add($"{config} pair({i},{j}): oracle={oracleSign} column={columnSign}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, Describe(failures));
    }

    [Fact]
    public void SelectColumnComparer_ProducesSameOrderAsOracle_OverTotalOrderCorpus()
    {
        var failures = new List<string>();

        foreach (SortConfig config in s_allConfigs)
        {
            Comparison<EventLocator> direct = ResolvedEventOrdering.SelectColumnComparer(
                s_totalOrderView.Reader, config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending);

            EventLocator[] locators = Enumerable.Range(0, s_totalOrderCorpus.Count).Select(s_totalOrderView.LocatorAt).ToArray();
            Array.Sort(locators, direct);

            List<long?> actual = locators.Select(locator => s_totalOrderCorpus[locator.Index].RecordId).ToList();
            List<long?> expected = s_totalOrderCorpus
                .SortEvents(config.OrderBy, config.IsDescending, config.GroupBy, config.IsGroupDescending)
                .Select(resolvedEvent => resolvedEvent.RecordId)
                .ToList();

            if (!actual.SequenceEqual(expected))
            {
                failures.Add($"{config}: expected [{string.Join(",", expected)}] but column sort gave [{string.Join(",", actual)}]");
            }
        }

        Assert.True(failures.Count == 0, Describe(failures));
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

    private static IReadOnlyList<ResolvedEvent> BuildTotalOrderCorpus()
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

        var events = new List<ResolvedEvent>();

        for (int index = 0; index < 10; index++)
        {
            // Many columns tie, but distinct non-null RecordIds guarantee a total order under every config.
            events.Add(FilterEventBuilder.CreateTestEvent(
                id: index % 3,
                recordId: index + 1,
                source: "Src" + (index % 2),
                level: levels[index % 4],
                computerName: "C" + (index % 2),
                logName: "L" + (index % 3),
                taskCategory: "T" + (index % 2),
                keywords: ["KW" + (index % 2)],
                processId: index % 3,
                threadId: index % 2,
                activityId: guids[index % 3],
                userId: sids[index % 2],
                timeCreated: baseTime.AddDays(index % 4)));
        }

        return events;
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
