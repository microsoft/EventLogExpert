// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;
using EventLogExpert.Runtime.Settings;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Tests.Memory;

public sealed class MemoryGovernorEffectTests
{
    private const long AutoBudget = 200_000_000;
    private const long AvailablePhysical = 200_000_000;
    private const long Baseline = 100_000_000;

    [Fact]
    public async Task AutoBudget_RecomputesFromCachedBaseline_IgnoringGrownHeap()
    {
        using var harness = new Harness();
        harness.SeedNonEmptyStore();
        harness.Meter.Used = 500_000_000;
        harness.Meter.Settled = 500_000_000;
        harness.SeedState(MemoryPressureLevel.Warning, budget: 999, current: 500_000_000);
        harness.Settings.MemoryBudgetBytes.Returns(0L);

        await harness.Effect.HandleBudgetChanged(harness.Dispatcher);
        harness.Effect.Sample();

        Assert.Equal(AutoBudget, Assert.Single(harness.Recomputed).BudgetBytes);
    }

    [Fact]
    public async Task BelowResumeThreshold_ResumesToNormal()
    {
        using var harness = new Harness();
        harness.SeedNonEmptyStore();
        harness.SeedState(MemoryPressureLevel.Paused, AutoBudget, current: 205_000_000);
        harness.Meter.Settled = 180_000_000;

        await harness.Effect.HandleCloseLog(harness.Dispatcher);
        harness.Effect.Sample();

        Assert.Equal(MemoryPressureLevel.Normal, Assert.Single(harness.Recomputed).Level);
    }

    [Fact]
    public async Task BudgetChanged_ReMaterializesBudgetAndDispatches()
    {
        using var harness = new Harness();
        harness.SeedNonEmptyStore();
        harness.SeedState(MemoryPressureLevel.Normal, AutoBudget, current: Baseline);
        harness.Settings.MemoryBudgetBytes.Returns(120_000_000);

        await harness.Effect.HandleBudgetChanged(harness.Dispatcher);
        harness.Effect.Sample();

        Assert.Equal(120_000_000, Assert.Single(harness.Recomputed).BudgetBytes);
    }

    [Fact]
    public void Ctor_SamplesAvailablePhysicalBeforeCapturingBaseline()
    {
        using var harness = new Harness();

        int availableIndex = harness.Meter.Calls.IndexOf("available");
        int baselineIndex = harness.Meter.Calls.IndexOf("used");

        Assert.True(availableIndex >= 0);
        Assert.True(baselineIndex >= 0);
        Assert.True(availableIndex < baselineIndex);
    }

    [Fact]
    public async Task EmptyStore_ForcesNormalAndMarksPartialSetStale()
    {
        using var harness = new Harness(settingBudget: 50_000_000);
        var staleId = EventLogId.Create();
        harness.Store = new RawEventStoreState();
        harness.SeedState(
            MemoryPressureLevel.Paused,
            budget: 50_000_000,
            current: Baseline,
            partial: ImmutableHashSet.Create(staleId));
        harness.Meter.Settled = Baseline;

        await harness.Effect.HandleCloseAllLogs(harness.Dispatcher);
        harness.Effect.Sample();

        var escape = Assert.Single(harness.Recomputed);
        Assert.Equal(MemoryPressureLevel.Normal, escape.Level);
        Assert.Contains(staleId, escape.StalePartialLogIds);
    }

    [Fact]
    public async Task ExplicitBudgetBelowBaseline_PausesWhileOpen()
    {
        using var harness = new Harness(settingBudget: 50_000_000);
        harness.SeedNonEmptyStore();
        harness.SeedState(MemoryPressureLevel.Normal, budget: 50_000_000, current: Baseline);
        harness.Meter.Settled = Baseline;

        await harness.Effect.HandleLoadEvents(harness.Dispatcher);
        harness.Effect.Sample();

        Assert.Equal(MemoryPressureLevel.Paused, Assert.Single(harness.Recomputed).Level);
    }

    [Fact]
    public async Task HeapAtBaseline_DoesNotStartPaused()
    {
        using var harness = new Harness();
        harness.SeedNonEmptyStore();
        harness.SeedState(MemoryPressureLevel.Normal, AutoBudget, current: Baseline);
        harness.Meter.Settled = Baseline;

        await harness.Effect.HandleLoadEvents(harness.Dispatcher);
        harness.Effect.Sample();

        Assert.DoesNotContain(harness.Recomputed, action => action.Level == MemoryPressureLevel.Paused);
    }

    [Fact]
    public async Task InHysteresisBand_DoesNotResume()
    {
        using var harness = new Harness();
        harness.SeedNonEmptyStore();
        harness.SeedState(MemoryPressureLevel.Paused, AutoBudget, current: 205_000_000);
        harness.Meter.Settled = 190_000_000;

        await harness.Effect.HandleCloseLog(harness.Dispatcher);
        harness.Effect.Sample();

        Assert.DoesNotContain(harness.Recomputed, action => action.Level == MemoryPressureLevel.Normal);
    }

    [Fact]
    public async Task ManyTriggersBeforeSample_CoalesceIntoOnePausedDispatch()
    {
        using var harness = new Harness();
        harness.SeedNonEmptyStore();
        harness.SeedState(MemoryPressureLevel.Normal, AutoBudget, current: 150_000_000);
        harness.Meter.Used = 210_000_000;

        for (int trigger = 0; trigger < 50; trigger++)
        {
            await harness.Effect.HandleIngestRawEvents(harness.Dispatcher);
        }

        harness.Effect.Sample();

        Assert.Equal(MemoryPressureLevel.Paused, Assert.Single(harness.Recomputed).Level);
    }

    [Fact]
    public async Task Mark_CarriesOnlyRacedClosedLogsAsStale()
    {
        using var harness = new Harness();
        var openId = EventLogId.Create();
        var closedId = EventLogId.Create();
        harness.Store = new RawEventStoreState
        {
            ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(openId, EventColumnStore.Empty)
        };
        harness.SeedState(
            MemoryPressureLevel.Warning,
            AutoBudget,
            current: 190_000_000,
            partial: ImmutableHashSet.Create(openId, closedId));
        harness.Meter.Used = 190_000_000;

        await harness.Effect.HandleMarkPartiallyLoaded(harness.Dispatcher);
        harness.Effect.Sample();

        var action = Assert.Single(harness.Recomputed);
        Assert.Contains(closedId, action.StalePartialLogIds);
        Assert.DoesNotContain(openId, action.StalePartialLogIds);
    }

    [Fact]
    public async Task ResumeThresholdIsOverTheAllowance_ResumesAtBaselineHeap()
    {
        using var harness = new Harness(baseline: 1_000_000_000, availablePhysical: 200_000_000);
        harness.SeedNonEmptyStore();
        harness.SeedState(MemoryPressureLevel.Paused, budget: 1_100_000_000, current: 1_100_000_000, baseline: 1_000_000_000);
        harness.Meter.Settled = 1_000_000_000;

        await harness.Effect.HandleCloseLog(harness.Dispatcher);
        harness.Effect.Sample();

        Assert.Equal(MemoryPressureLevel.Normal, Assert.Single(harness.Recomputed).Level);
    }

    [Fact]
    public void SampleWithoutPendingTrigger_DoesNotDispatch()
    {
        using var harness = new Harness();
        harness.SeedNonEmptyStore();
        harness.SeedState(MemoryPressureLevel.Normal, AutoBudget, current: 150_000_000);
        harness.Meter.Used = 210_000_000;

        harness.Effect.Sample();

        Assert.Empty(harness.Recomputed);
    }

    [Fact]
    public async Task SettledOverBudget_DispatchesPaused()
    {
        using var harness = new Harness();
        harness.SeedNonEmptyStore();
        harness.SeedState(MemoryPressureLevel.Normal, AutoBudget, current: 150_000_000);
        harness.Meter.Settled = 210_000_000;

        await harness.Effect.HandleLoadEvents(harness.Dispatcher);
        harness.Effect.Sample();

        Assert.Equal(MemoryPressureLevel.Paused, Assert.Single(harness.Recomputed).Level);
    }

    [Fact]
    public void StoreInitialized_DispatchesInitializedWithAutoBudgetOverBaseline()
    {
        using var harness = new Harness();

        harness.Effect.HandleStoreInitialized(harness.Dispatcher);

        var initialized = Assert.Single(harness.Dispatched.OfType<MemoryGovernorInitializedAction>());
        Assert.Equal(Baseline, initialized.BaselineBytes);
        Assert.Equal(AutoBudget, initialized.BudgetBytes);
    }

    [Fact]
    public async Task SubThresholdMoveAtUnchangedLevel_DoesNotDispatch()
    {
        using var harness = new Harness();
        harness.SeedNonEmptyStore();
        harness.SeedState(MemoryPressureLevel.Normal, AutoBudget, current: 150_000_000);
        harness.Meter.Settled = 150_500_000;

        await harness.Effect.HandleLoadEvents(harness.Dispatcher);
        harness.Effect.Sample();

        Assert.Empty(harness.Recomputed);
    }

    [Fact]
    public async Task TransientSpikeButSettledBelowBudget_DoesNotPause()
    {
        using var harness = new Harness();
        harness.SeedNonEmptyStore();
        harness.SeedState(MemoryPressureLevel.Normal, AutoBudget, current: 150_000_000);
        harness.Meter.Used = 250_000_000;
        harness.Meter.Settled = 150_000_000;

        await harness.Effect.HandleLoadEvents(harness.Dispatcher);
        harness.Effect.Sample();

        Assert.DoesNotContain(harness.Recomputed, action => action.Level == MemoryPressureLevel.Paused);
    }

    private sealed class FakeMeter : IProcessMemoryMeter
    {
        public long AvailablePhysical { get; set; }

        public List<string> Calls { get; } = [];

        public long Settled { get; set; }

        public long Used { get; set; }

        public long GetAvailablePhysicalBytes()
        {
            Calls.Add("available");

            return AvailablePhysical;
        }

        public long GetProcessUsedBytes(bool forceFullCollection)
        {
            Calls.Add("used");

            return forceFullCollection ? Settled : Used;
        }
    }

    private sealed class Harness : IDisposable
    {
        public Harness(
            long baseline = Baseline,
            long availablePhysical = AvailablePhysical,
            long settingBudget = 0)
        {
            Meter = new FakeMeter { Used = baseline, Settled = baseline, AvailablePhysical = availablePhysical };
            Settings.MemoryBudgetBytes.Returns(settingBudget);
            GovernorState.Value.Returns(_ => State);
            RawStore.Value.Returns(_ => Store);
            Dispatcher.When(dispatcher => dispatcher.Dispatch(Arg.Any<object>()))
                .Do(call => Dispatched.Add(call.Arg<object>()!));

            Effect = new MemoryGovernorEffect(Meter, GovernorState, RawStore, Settings, Dispatcher, Timeout.InfiniteTimeSpan);
        }

        public List<object> Dispatched { get; } = [];

        public IDispatcher Dispatcher { get; } = Substitute.For<IDispatcher>();

        public MemoryGovernorEffect Effect { get; }

        public IState<MemoryGovernorState> GovernorState { get; } = Substitute.For<IState<MemoryGovernorState>>();

        public FakeMeter Meter { get; }

        public IState<RawEventStoreState> RawStore { get; } = Substitute.For<IState<RawEventStoreState>>();

        public IReadOnlyList<MemoryGovernorRecomputedAction> Recomputed =>
            [.. Dispatched.OfType<MemoryGovernorRecomputedAction>()];

        public ISettingsService Settings { get; } = Substitute.For<ISettingsService>();

        public MemoryGovernorState State { get; set; } = new();

        public RawEventStoreState Store { get; set; } = new();

        public void Dispose() => Effect.Dispose();

        public void SeedNonEmptyStore() =>
            Store = new RawEventStoreState
            {
                ByLog = ImmutableDictionary<EventLogId, EventColumnStore>.Empty.Add(EventLogId.Create(), EventColumnStore.Empty)
            };

        public void SeedState(
            MemoryPressureLevel level,
            long budget,
            long current,
            long baseline = Baseline,
            ImmutableHashSet<EventLogId>? partial = null) =>
            State = new MemoryGovernorState
            {
                Level = level,
                BudgetBytes = budget,
                CurrentBytes = current,
                BaselineBytes = baseline,
                PartiallyLoadedForMemory = partial ?? []
            };
    }
}
