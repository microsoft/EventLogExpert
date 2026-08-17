// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using Fluxor;
using System.Collections.Immutable;
using CloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.Memory;

internal sealed class MemoryGovernorEffect : IDisposable
{
    private const double ResumeAllowanceFraction = 0.85;

    private static readonly TimeSpan s_defaultSampleInterval = TimeSpan.FromMilliseconds(500);

    private readonly long _baselineAllowance;
    private readonly long _baselineBytes;
    private readonly IDispatcher _dispatcher;
    private readonly Lock _gate = new();
    private readonly IState<MemoryGovernorState> _governorState;
    private readonly IProcessMemoryMeter _meter;
    private readonly IState<RawEventStoreState> _rawEventStore;
    private readonly Timer _sampleTimer;
    private readonly ISettingsService _settings;

    private long _budgetBytes;
    private bool _disposed;
    private bool _pendingForcedSample;
    private bool _pendingSample;

    public MemoryGovernorEffect(
        IProcessMemoryMeter meter,
        IState<MemoryGovernorState> governorState,
        IState<RawEventStoreState> rawEventStore,
        ISettingsService settings,
        IDispatcher dispatcher,
        TimeSpan? sampleInterval = null)
    {
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(governorState);
        ArgumentNullException.ThrowIfNull(rawEventStore);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _meter = meter;
        _governorState = governorState;
        _rawEventStore = rawEventStore;
        _settings = settings;
        _dispatcher = dispatcher;

        _baselineAllowance = ComputeStartupAllowance(meter);
        _baselineBytes = meter.GetProcessUsedBytes(forceFullCollection: false);
        _budgetBytes = MaterializeBudget(settings.MemoryBudgetBytes);

        settings.MemoryBudgetChanged += OnMemoryBudgetChanged;

        TimeSpan interval = sampleInterval ?? s_defaultSampleInterval;
        _sampleTimer = new Timer(_ => Sample(), null, interval, interval);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }

            _disposed = true;
        }

        _settings.MemoryBudgetChanged -= OnMemoryBudgetChanged;
        _sampleTimer.Dispose();
    }

    [EffectMethod(typeof(MemoryBudgetChangedAction))]
    public Task HandleBudgetChanged(IDispatcher dispatcher)
    {
        Volatile.Write(ref _budgetBytes, MaterializeBudget(_settings.MemoryBudgetBytes));
        RequestSample(forceFullCollection: true);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(CloseAllLogsAction))]
    public Task HandleCloseAllLogs(IDispatcher dispatcher)
    {
        RequestSample(forceFullCollection: true);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(CloseLogAction))]
    public Task HandleCloseLog(IDispatcher dispatcher)
    {
        RequestSample(forceFullCollection: true);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(IngestRawEventsAction))]
    public Task HandleIngestRawEvents(IDispatcher dispatcher)
    {
        RequestSample(forceFullCollection: false);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(LoadEventsAction))]
    public Task HandleLoadEvents(IDispatcher dispatcher)
    {
        RequestSample(forceFullCollection: true);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(LoadEventsPartialAction))]
    public Task HandleLoadEventsPartial(IDispatcher dispatcher)
    {
        RequestSample(forceFullCollection: false);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(MarkPartiallyLoadedForMemoryAction))]
    public Task HandleMarkPartiallyLoaded(IDispatcher dispatcher)
    {
        RequestSample(forceFullCollection: false);

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(StoreInitializedAction))]
    public Task HandleStoreInitialized(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new MemoryGovernorInitializedAction(_baselineBytes, Volatile.Read(ref _budgetBytes)));

        return Task.CompletedTask;
    }

    internal void Sample()
    {
        bool forceFullCollection;

        lock (_gate)
        {
            if (_disposed || !_pendingSample) { return; }

            forceFullCollection = _pendingForcedSample;
            _pendingSample = false;
            _pendingForcedSample = false;
        }

        RecomputeAndDispatch(_dispatcher, forceFullCollection);
    }

    private static ImmutableHashSet<EventLogId> ComputeStaleLogIds(
        ImmutableHashSet<EventLogId> marked,
        ImmutableDictionary<EventLogId, EventColumnStore> byLog)
    {
        if (marked.IsEmpty) { return ImmutableHashSet<EventLogId>.Empty; }

        ImmutableHashSet<EventLogId>.Builder stale = ImmutableHashSet.CreateBuilder<EventLogId>();

        foreach (EventLogId logId in marked)
        {
            if (!byLog.ContainsKey(logId)) { stale.Add(logId); }
        }

        return stale.ToImmutable();
    }

    private static long ComputeStartupAllowance(IProcessMemoryMeter meter)
    {
        long available = meter.GetAvailablePhysicalBytes();

        return available > 0 ? available / 2 : 0;
    }

    private static MemoryPressureLevel NextLevel(MemoryPressureLevel current, long used, long budget, long resumeAt)
    {
        if (used >= budget) { return MemoryPressureLevel.Paused; }

        if (used < resumeAt) { return MemoryPressureLevel.Normal; }

        return current == MemoryPressureLevel.Paused ? MemoryPressureLevel.Paused : MemoryPressureLevel.Warning;
    }

    private long MaterializeBudget(long settingValue) =>
        settingValue > 0 ? settingValue : _baselineBytes + _baselineAllowance;

    private void OnMemoryBudgetChanged() => _dispatcher.Dispatch(new MemoryBudgetChangedAction());

    private void RecomputeAndDispatch(IDispatcher dispatcher, bool forceFullCollection)
    {
        MemoryGovernorState state = _governorState.Value;
        ImmutableDictionary<EventLogId, EventColumnStore> byLog = _rawEventStore.Value.ByLog;
        long budget = Volatile.Read(ref _budgetBytes);

        if (byLog.IsEmpty)
        {
            if (state.Level == MemoryPressureLevel.Normal &&
                state.PartiallyLoadedForMemory.IsEmpty &&
                state.BudgetBytes == budget)
            {
                return;
            }

            dispatcher.Dispatch(new MemoryGovernorRecomputedAction(
                MemoryPressureLevel.Normal,
                _meter.GetProcessUsedBytes(forceFullCollection),
                budget,
                state.PartiallyLoadedForMemory));

            return;
        }

        long used = _meter.GetProcessUsedBytes(forceFullCollection);
        long resumeAt = _baselineBytes + (long)(ResumeAllowanceFraction * (budget - _baselineBytes));
        MemoryPressureLevel level = NextLevel(state.Level, used, budget, resumeAt);
        ImmutableHashSet<EventLogId> stale = ComputeStaleLogIds(state.PartiallyLoadedForMemory, byLog);

        long allowance = Math.Max(1, budget - _baselineBytes);
        bool levelChanged = level != state.Level;
        bool budgetChanged = budget != state.BudgetBytes;
        bool currentMoved = Math.Abs(used - state.CurrentBytes) >= allowance / 100;
        bool setChanged = !stale.IsEmpty;

        if (!levelChanged && !budgetChanged && !currentMoved && !setChanged) { return; }

        dispatcher.Dispatch(new MemoryGovernorRecomputedAction(level, used, budget, stale));
    }

    private void RequestSample(bool forceFullCollection)
    {
        lock (_gate)
        {
            _pendingSample = true;

            if (forceFullCollection) { _pendingForcedSample = true; }
        }
    }
}
