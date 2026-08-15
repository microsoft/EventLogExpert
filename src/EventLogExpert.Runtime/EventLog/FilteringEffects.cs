// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.LogTable;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class FilteringEffects(
    IState<EventLogState> eventLogState,
    IState<RawEventStoreState> rawEventStore,
    LiveTailIngestCoordinator liveTailCoordinator,
    XmlReloadCoordinator xmlReloadCoordinator,
    IXmlFilterMatcher matcher,
    XmlFilterMatchCache matchCache,
    EventLogConcurrencyState concurrencyState,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
{
    private readonly EventLogConcurrencyState _concurrencyState = concurrencyState;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly LiveTailIngestCoordinator _liveTailCoordinator = liveTailCoordinator;
    private readonly ITraceLogger _logger = logger;
    private readonly XmlFilterMatchCache _matchCache = matchCache;
    private readonly IXmlFilterMatcher _matcher = matcher;
    private readonly IState<RawEventStoreState> _rawEventStore = rawEventStore;
    private readonly XmlReloadCoordinator _xmlReloadCoordinator = xmlReloadCoordinator;

    private enum MatchComputeOutcome
    {
        Published,
        Superseded,
        FaultReloaded
    }

    [EffectMethod]
    public Task HandleAddEvent(AddEventAction action, IDispatcher dispatcher)
    {
        if (!_eventLogState.Value.ContinuouslyUpdate ||
            !_eventLogState.Value.OpenLogs.TryGetValue(action.NewEvent.OwningLog, out var owningLog))
        {
            return Task.CompletedTask;
        }

        _liveTailCoordinator.Enqueue(owningLog.Id, action.NewEvent);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleApplyFilter(ApplyFilterAction action, IDispatcher dispatcher)
    {
        Filter filter = action.Filter;

        if (!filter.RequiresXml || _eventLogState.Value.OpenLogs.IsEmpty)
        {
            _matchCache.Clear();

            return;
        }

        // Reload the live (Channel) logs unless the File compute already fell back to the full escape-hatch reload.
        // The reload is decoupled from which compute won the monotonic publish, so a racing same-filter recompute
        // cannot strand it; ReloadChannelLogsAsync re-checks the current filter and no-ops if a newer filter won.
        if (await ComputeFileMatchesAsync(filter, dispatcher) != MatchComputeOutcome.FaultReloaded)
        {
            await ReloadChannelLogsAsync(filter, dispatcher);
        }
    }

    [EffectMethod(typeof(CloseAllLogsAction))]
    public Task HandleCloseAllLogs(IDispatcher dispatcher)
    {
        _matchCache.Clear();

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleCloseLog(CloseLogAction action, IDispatcher dispatcher)
    {
        _matchCache.Remove(action.LogId);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleIngestRawEvents(IngestRawEventsAction action, IDispatcher dispatcher) =>
        RecomputeFileMatchesAsync(dispatcher);

    [EffectMethod]
    public Task HandleLoadEvents(LoadEventsAction action, IDispatcher dispatcher) =>
        RecomputeFileMatchesAsync(dispatcher);

    [EffectMethod]
    public Task HandleSetContinuouslyUpdate(SetContinuouslyUpdateAction action, IDispatcher dispatcher)
    {
        if (action.ContinuouslyUpdate)
        {
            LogReloadEffects.ProcessNewEventBuffer(_eventLogState.Value, dispatcher);
        }
        else
        {
            _liveTailCoordinator.Flush();
        }

        return Task.CompletedTask;
    }

    private async Task<MatchComputeOutcome> ComputeFileMatchesAsync(Filter filter, IDispatcher dispatcher)
    {
        long sequence = _matchCache.NextSequence();
        EventLogState state = _eventLogState.Value;
        RawEventStoreState rawStore = _rawEventStore.Value;
        Dictionary<EventLogId, XmlFilterMatch> matches = new(state.OpenLogs.Count);
        bool recomputed = false;

        try
        {
            foreach ((string name, OpenLogInfo info) in state.OpenLogs)
            {
                // Only at-rest File logs not already loaded with a materialized XML column use on-demand matching.
                if (info.Type != LogPathType.File || _concurrencyState.IsLoadedWithXml(info.Id)) { continue; }

                if (!rawStore.ByLog.TryGetValue(info.Id, out EventColumnStore? store)) { continue; }

                // Reuse a still-current match to avoid a redundant native rescan of a stable log.
                if (_matchCache.GetMatch(filter, info.Id) is { } current &&
                    current.Generation == store.Generation &&
                    current.ContentVersion == store.ContentVersion &&
                    current.Count == store.Count)
                {
                    matches[info.Id] = current;

                    continue;
                }

                IEventColumnReader reader = store.CreateReader(info.Id);

                matches[info.Id] = await Task.Run(
                    () => _matcher.ComputeMatch(reader, filter, name, info.Type, CancellationToken.None));

                recomputed = true;
            }
        }
        catch (Exception ex)
        {
            // A native scan fault falls back to the full reload escape hatch, unless a newer filter has already
            // superseded this compute - then the newer apply owns recovery and this branch must not act.
            if (_eventLogState.Value.AppliedFilter.HasFilteringChangedFrom(filter))
            {
                return MatchComputeOutcome.Superseded;
            }

            _logger.Trace(
                $"{nameof(ComputeFileMatchesAsync)}: on-demand XML match faulted ({ex.Message}); falling back to reload.");

            await ReloadEscapeHatchAsync(filter, dispatcher);

            return MatchComputeOutcome.FaultReloaded;
        }

        // Discard a superseded result: a newer filter was applied while the scan ran.
        if (_eventLogState.Value.AppliedFilter.HasFilteringChangedFrom(filter))
        {
            return MatchComputeOutcome.Superseded;
        }

        // Nothing was rescanned: the stored match already covers every current File log, so the gate is already
        // satisfied - skip a redundant publish + ordered-view rebuild.
        if (!recomputed) { return MatchComputeOutcome.Published; }

        // Monotonic publish: a lower-sequence (older-snapshot) compute cannot overwrite a newer one's match.
        if (!_matchCache.Set(filter, matches, sequence)) { return MatchComputeOutcome.Superseded; }

        // Evict any log that closed during the scan/publish from the just-published map using a fresh open-log
        // snapshot. This is remove-only, so a close racing the publish cannot leave an orphaned bitset: it is dropped
        // here if the close is already visible, or by the close handler's Remove/Clear if the close lands afterward.
        _matchCache.RemoveNotIn(_eventLogState.Value.OpenLogs.Values.Select(log => log.Id).ToHashSet());

        dispatcher.Dispatch(new XmlFilterMatchReadyAction());

        return MatchComputeOutcome.Published;
    }

    private async Task RecomputeFileMatchesAsync(IDispatcher dispatcher)
    {
        Filter filter = _eventLogState.Value.AppliedFilter;

        if (filter.RequiresXml) { await ComputeFileMatchesAsync(filter, dispatcher); }
    }

    private async Task ReloadChannelLogsAsync(Filter filter, IDispatcher dispatcher)
    {
        if (_eventLogState.Value.AppliedFilter.HasFilteringChangedFrom(filter)) { return; }

        PendingXmlReload pendingReload = _xmlReloadCoordinator.Resolve(filter, LogPathType.Channel);

        if (pendingReload.IsNeeded)
        {
            await _xmlReloadCoordinator.ReloadAsync(pendingReload, dispatcher);
        }
    }

    private async Task ReloadEscapeHatchAsync(Filter filter, IDispatcher dispatcher)
    {
        PendingXmlReload pendingReload = _xmlReloadCoordinator.Resolve(filter);

        if (pendingReload.IsNeeded)
        {
            await _xmlReloadCoordinator.ReloadAsync(pendingReload, dispatcher);
        }
    }
}
