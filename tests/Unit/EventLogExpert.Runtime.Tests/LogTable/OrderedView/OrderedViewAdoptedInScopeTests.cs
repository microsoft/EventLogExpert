// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewAdoptedInScopeTests
{
    private static readonly SortContext s_context = new(ColumnName.DateAndTime, false, null, false);

    [Fact]
    public void AdoptedInScope_AfterClear_IsEmpty()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        ReconcileAll(state, logId, generation: 0, count: 5);
        Rebuild(state);
        Assert.NotEmpty(state.AdoptedInScope);

        state.Clear();

        Assert.Empty(state.AdoptedInScope);
    }

    [Fact]
    public void AdoptedInScope_AfterRebuild_ContainsEveryInScopeLogThatHasRows()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        ReconcileAll(state, logA, generation: 0, count: 12);
        ReconcileAll(state, logB, generation: 0, count: 8);
        Rebuild(state);

        Assert.Equal(
            new HashSet<LogGeneration> { new(logA, 0), new(logB, 0) },
            state.AdoptedInScope.ToHashSet());
    }

    [Fact]
    public void AdoptedInScope_EveryMemberResolves_SoTheCombinedViewBuildsWithoutThrowing()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        ReconcileAll(state, logA, generation: 0, count: 11);
        ReconcileAll(state, logB, generation: 0, count: 4);
        RebuildRequest request = state.BeginRebuild((locator, _) => locator.LogId == logA, s_context);
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));

        var facade = new CombinedOrderedColumnView(state.Current, [.. state.AdoptedInScope]);

        Assert.Equal(state.Current.Count, facade.Count);
    }

    [Fact]
    public void AdoptedInScope_ReflectsTheAdoptedScope_NotLiveScope()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        EventLogId logC = EventLogId.Create();
        var state = new OrderedViewState();

        ReconcileAll(state, logA, generation: 0, count: 7);
        ReconcileAll(state, logB, generation: 0, count: 5);
        ReconcileAll(state, logC, generation: 0, count: 6);
        Rebuild(state);

        RebuildRequest? narrowed = ViewRequests.AdvanceScope(state, [logA, logB], 1);
        Assert.NotNull(narrowed);
        Assert.True(state.TryAdoptRebuild(narrowed, OrderedViewState.BuildIndex(narrowed, CancellationToken.None)));

        Assert.Equal(
            new HashSet<LogGeneration> { new(logA, 0), new(logB, 0) },
            state.AdoptedInScope.ToHashSet());
    }

    [Fact]
    public void AdoptedInScope_ResetAdoptedBeforeNewGenerationReader_ExcludesReaderlessMember_AndFacadeBuilds()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        ReconcileAll(state, logA, generation: 0, count: 10);
        ReconcileAll(state, logB, generation: 0, count: 6);
        Rebuild(state);

        RebuildRequest reset = state.BeginReset(logB, newGeneration: 1);
        Assert.True(state.TryAdoptRebuild(reset, OrderedViewState.BuildIndex(reset, CancellationToken.None)));

        Assert.DoesNotContain(new LogGeneration(logB, 1), state.AdoptedInScope);
        Assert.Contains(new LogGeneration(logA, 0), state.AdoptedInScope);

        var facade = new CombinedOrderedColumnView(state.Current, [.. state.AdoptedInScope]);
        Assert.Equal(state.Current.Count, facade.Count);
    }

    [Fact]
    public void AdoptedInScope_ResetToAZeroEventGenerationWithAReconciledEmptyReader_IsExcluded()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        ReconcileAll(state, logA, generation: 0, count: 8);
        ReconcileAll(state, logB, generation: 0, count: 5);
        Rebuild(state);

        RebuildRequest reset = state.BeginReset(logB, newGeneration: 1);
        Assert.False(state.ReconcileLog(logB, Reader(logB, generation: 1, count: 0)));
        Assert.True(state.TryAdoptRebuild(reset, OrderedViewState.BuildIndex(reset, CancellationToken.None)));

        Assert.DoesNotContain(new LogGeneration(logB, 1), state.AdoptedInScope);

        var facade = new CombinedOrderedColumnView(state.Current, [.. state.AdoptedInScope]);
        Assert.Equal(state.Current.Count, facade.Count);
    }

    [Fact]
    public void AdoptedInScope_UpdatesOnLiveTailPublish_WhenAnEmptyInScopeLogGainsItsFirstRow()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        ReconcileAll(state, logA, generation: 0, count: 8);
        Assert.False(state.ReconcileLog(logB, Reader(logB, generation: 0, count: 0)));
        Rebuild(state);
        Assert.DoesNotContain(new LogGeneration(logB, 0), state.AdoptedInScope);

        Assert.True(state.ReconcileLog(logB, Reader(logB, generation: 0, count: 3)));
        state.Publish();

        Assert.Contains(new LogGeneration(logB, 0), state.AdoptedInScope);

        var facade = new CombinedOrderedColumnView(state.Current, [.. state.AdoptedInScope]);
        Assert.Equal(state.Current.Count, facade.Count);
    }

    [Fact]
    public void AdoptedInScope_ZeroEventLog_IsAbsent()
    {
        EventLogId withRows = EventLogId.Create();
        EventLogId empty = EventLogId.Create();
        var state = new OrderedViewState();

        ReconcileAll(state, withRows, generation: 0, count: 9);
        Assert.False(state.ReconcileLog(empty, Reader(empty, generation: 0, count: 0)));
        Rebuild(state);

        Assert.Contains(new LogGeneration(withRows, 0), state.AdoptedInScope);
        Assert.DoesNotContain(new LogGeneration(empty, 0), state.AdoptedInScope);
    }

    [Fact]
    public void AdoptedInScope_ZeroSurvivorMember_IsPresentAndItsReaderResolves()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        ReconcileAll(state, logA, generation: 0, count: 10);
        ReconcileAll(state, logB, generation: 0, count: 6);

        RebuildRequest request = state.BeginRebuild((locator, _) => locator.LogId == logA, s_context);
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));

        Assert.Contains(new LogGeneration(logB, 0), state.AdoptedInScope);
        Assert.True(state.Current.TryGetReaderByLog(logB, 0, out _));
        Assert.Equal(10, state.Current.Count);
    }

    [Fact]
    public void ReconcileLog_RegistersAPostResetZeroSurvivorGeneration_KeyedOnPublishedLogGenerationNotLogId()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        ReconcileAll(state, logA, generation: 0, count: 8);
        ReconcileAll(state, logB, generation: 0, count: 5);

        RebuildRequest filtered = state.BeginRebuild(static (_, _) => false, s_context);
        Assert.True(state.TryAdoptRebuild(filtered, OrderedViewState.BuildIndex(filtered, CancellationToken.None)));

        RebuildRequest reset = state.BeginReset(logB, newGeneration: 1);
        Assert.True(state.TryAdoptRebuild(reset, OrderedViewState.BuildIndex(reset, CancellationToken.None)));
        Assert.DoesNotContain(new LogGeneration(logB, 1), state.AdoptedInScope);

        Assert.True(state.ReconcileLog(logB, Reader(logB, generation: 1, count: 4)));
        state.Publish();

        Assert.Contains(new LogGeneration(logB, 1), state.AdoptedInScope);
    }

    [Fact]
    public void ReconcileLog_RegistersTheFirstZeroSurvivorMember_ThenSuppressesFurtherFilteredGrowth()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        RebuildRequest request = state.BeginRebuild(static (_, _) => false, s_context);
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));

        Assert.True(state.ReconcileLog(logId, Reader(logId, generation: 0, count: 3)));
        state.Publish();
        Assert.Contains(new LogGeneration(logId, 0), state.AdoptedInScope);

        Assert.False(state.ReconcileLog(logId, Reader(logId, generation: 0, count: 6)));
    }

    [Fact]
    public void ReconcileLog_ReturnsFalse_ForAZeroEventReader()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Assert.False(state.ReconcileLog(logId, Reader(logId, generation: 0, count: 0)));
    }

    [Fact]
    public void ReconcileLog_ReturnsFalse_WhenTheLogIsOutOfScope()
    {
        EventLogId inScope = EventLogId.Create();
        EventLogId outOfScope = EventLogId.Create();
        var state = new OrderedViewState();

        RebuildRequest? narrowed = ViewRequests.AdvanceScope(state, [inScope], 1);
        Assert.NotNull(narrowed);
        Assert.True(state.TryAdoptRebuild(narrowed, OrderedViewState.BuildIndex(narrowed, CancellationToken.None)));

        Assert.False(state.ReconcileLog(outOfScope, Reader(outOfScope, generation: 0, count: 3)));
    }

    [Fact]
    public void ReconcileLog_ReturnsFalse_WhenTheReaderIsNotStrictlyNewer()
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = Reader(logId, generation: 0, count: 6);
        var state = new OrderedViewState();

        Assert.True(state.ReconcileLog(logId, reader));
        Assert.False(state.ReconcileLog(logId, reader));
    }

    [Fact]
    public void ReconcileLog_ReturnsTrue_ForAStrictlyNewerSameCountContentReplace()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Assert.True(state.ReconcileLog(logId, Reader(logId, generation: 0, count: 5)));

        IEventColumnReader replaced =
            EventColumnStore.Build(MakeEvents("Log", firstRecordId: 1, count: 5), generation: 0, contentVersion: 1).CreateReader(logId);

        Assert.True(state.ReconcileLog(logId, replaced));
    }

    [Fact]
    public void ReconcileLog_ReturnsTrue_WhenRowsEnterTheIndex()
    {
        EventLogId logId = EventLogId.Create();
        var state = new OrderedViewState();

        Assert.True(state.ReconcileLog(logId, Reader(logId, generation: 0, count: 6)));
    }

    [Fact]
    public void ScopeExpansionWindow_HoldsNewScopeRowsOutOfTheIndex_SoAdoptedInScopeStaysCoherentAndTheFacadePartitions()
    {
        EventLogId logA = EventLogId.Create();
        EventLogId logB = EventLogId.Create();
        var state = new OrderedViewState();

        ReconcileAll(state, logA, generation: 0, count: 8);
        RebuildRequest? scopeA = ViewRequests.AdvanceScope(state, [logA], 1);
        Assert.NotNull(scopeA);
        Assert.True(state.TryAdoptRebuild(scopeA, OrderedViewState.BuildIndex(scopeA, CancellationToken.None)));

        Assert.NotNull(ViewRequests.AdvanceScope(state, [logA, logB], 2));
        state.ReconcileLog(logB, Reader(logB, generation: 0, count: 4));
        state.Publish();

        Assert.Equal(8, state.Current.Count);
        Assert.DoesNotContain(new LogGeneration(logB, 0), state.AdoptedInScope);

        var facade = new CombinedOrderedColumnView(state.Current, [.. state.AdoptedInScope]);
        var eventIdCounts = new Dictionary<int, int>();
        facade.CountEventIds(eventIdCounts, CancellationToken.None);
        Assert.Equal(8, facade.Count);
    }

    private static List<ResolvedEvent> MakeEvents(string owningLog, long firstRecordId, int count)
    {
        var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<ResolvedEvent>(count);

        for (int i = 0; i < count; i++)
        {
            events.Add(new ResolvedEvent(owningLog, LogPathType.Channel)
            {
                RecordId = firstRecordId + i,
                TimeCreated = clock.AddSeconds(i),
                Id = 1000 + (i % 5),
                Level = "Information",
                Source = "Provider.A",
                LogName = owningLog
            });
        }

        return events;
    }

    private static IEventColumnReader Reader(EventLogId logId, int generation, int count) =>
        EventColumnStore.Build(MakeEvents("Log", firstRecordId: 1, count), generation, contentVersion: generation).CreateReader(logId);

    private static void Rebuild(OrderedViewState state)
    {
        RebuildRequest request = state.BeginRebuild(static (_, _) => true, s_context);
        Assert.True(state.TryAdoptRebuild(request, OrderedViewState.BuildIndex(request, CancellationToken.None)));
    }

    private static void ReconcileAll(OrderedViewState state, EventLogId logId, int generation, int count) =>
        state.ReconcileLog(logId, Reader(logId, generation, count));
}
