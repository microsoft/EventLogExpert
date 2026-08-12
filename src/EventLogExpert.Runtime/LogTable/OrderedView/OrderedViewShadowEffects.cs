// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Histogram;
using Fluxor;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed class OrderedViewShadowEffects(
    IState<EventLogState> eventLogState,
    IState<LogTableState> logTableState,
    IState<RawEventStoreState> rawEventStore,
    OrderedViewWriter writer,
    ViewRequestIssuer issuer,
    OrderedViewDispatchBridge bridge,
    IDispatcher dispatcher,
    EventLogConcurrencyState concurrencyState)
{
    private readonly OrderedViewDispatchBridge _bridge = bridge;
    private readonly EventLogConcurrencyState _concurrencyState = concurrencyState;
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly ViewRequestIssuer _issuer = issuer;
    private readonly IState<LogTableState> _logTableState = logTableState;
    private readonly IState<RawEventStoreState> _rawEventStore = rawEventStore;
    private readonly OrderedViewWriter _writer = writer;

    [EffectMethod(typeof(AddTableAction))]
    public Task HandleAddTable(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod]
    public Task HandleApplyFilter(ApplyFilterAction action, IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod(typeof(CloseAllButThisAction))]
    public Task HandleCloseAllButThis(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod(typeof(CloseAllLogsAction))]
    public Task HandleCloseAllLogs(IDispatcher dispatcher) =>
        Shadow(() =>
        {
            long sequence = _issuer.ResetForCloseAll();

            _dispatcher.Dispatch(new ViewRequestInvalidatedAction(sequence));
            _writer.EnqueueClear(_logTableState.Value.ViewIdentity, sequence);
        });

    [EffectMethod(typeof(CloseGroupAction))]
    public Task HandleCloseGroup(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod]
    public Task HandleCloseLog(CloseLogAction action, IDispatcher dispatcher) =>
        Shadow(() =>
        {
            _writer.EnqueueRemoveLog(action.LogId);
            Sync();
        });

    [EffectMethod(typeof(CloseOthersInGroupAction))]
    public Task HandleCloseOthersInGroup(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod]
    public Task HandleIngestRawEvents(IngestRawEventsAction action, IDispatcher dispatcher) =>
        Shadow(() =>
        {
            Sync();

            foreach (EventLogId logId in action.EventsByLog.Keys) { Reconcile(logId); }
        });

    [EffectMethod(typeof(LoadColumnsCompletedAction))]
    public Task HandleLoadColumnsCompleted(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod]
    public Task HandleLoadEvents(LoadEventsAction action, IDispatcher dispatcher) =>
        Shadow(() =>
        {
            Sync();
            Reconcile(action.LogData.Id);
        });

    [EffectMethod]
    public Task HandleLoadEventsPartial(LoadEventsPartialAction action, IDispatcher dispatcher) =>
        Shadow(() =>
        {
            Sync();
            Reconcile(action.LogData.Id);
        });

    [EffectMethod(typeof(MoveTabToGroupAction))]
    public Task HandleMoveTabToGroup(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod(typeof(NewGroupFromTabAction))]
    public Task HandleNewGroupFromTab(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod]
    public Task HandleOrderedViewDisplayFaulted(OrderedViewDisplayFaultedAction action, IDispatcher dispatcher)
    {
        LogTableState state = _logTableState.Value;

        if (action.Identity is not { } faulted || faulted != state.ViewIdentity) { return Task.CompletedTask; }

        if (!_issuer.TryBeginRecovery(faulted, state.LastPublishedSnapshotVersion)) { return Task.CompletedTask; }

        _writer.EnqueueClearFault();
        _issuer.ResetForClear();

        dispatcher.Dispatch(new OrderedViewDisplayRecoveredAction());

        return Shadow(Sync);
    }

    [EffectMethod(typeof(RemoveTabFromGroupAction))]
    public Task HandleRemoveTabFromGroup(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod(typeof(SetActiveTableAction))]
    public Task HandleSetActiveTable(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod]
    public Task HandleSetGroupBy(SetGroupByAction action, IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod]
    public Task HandleSetHistogramVisible(SetHistogramVisibleAction action, IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod]
    public Task HandleSetOrderBy(SetOrderByAction action, IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod(typeof(SetTabGroupCollapsedAction))]
    public Task HandleSetTabGroupCollapsed(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod(typeof(ToggleGroupSortingAction))]
    public Task HandleToggleGroupSorting(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod(typeof(ToggleSortingAction))]
    public Task HandleToggleSorting(IDispatcher dispatcher) => Shadow(Sync);

    private void Reconcile(EventLogId logId)
    {
        if (_rawEventStore.Value.ByLog.TryGetValue(logId, out var store))
        {
            _writer.EnqueueReconcile(logId, store.CreateReader(logId));
        }
    }

    private IReadOnlyDictionary<EventLogId, IEventColumnReader> ScopeReaders(ImmutableArray<EventLogId> scope)
    {
        var readers = new Dictionary<EventLogId, IEventColumnReader>(scope.Length);

        foreach (EventLogId logId in scope)
        {
            if (_rawEventStore.Value.ByLog.TryGetValue(logId, out var store))
            {
                readers[logId] = store.CreateReader(logId);
            }
        }

        return readers;
    }

    private Task Shadow(Action work)
    {
        if (!_issuer.Enabled) { return Task.CompletedTask; }

        try { work(); }
        catch (Exception fault)
        {
            _issuer.RecordFault(fault);
            _bridge.NotifyShadowFault(fault);
        }

        return Task.CompletedTask;
    }

    private void Sync()
    {
        LogTableState state = _logTableState.Value;
        ViewIdentity identity = state.ViewIdentity;
        Filter filter = identity.Filter;

        if (filter.RequiresXml &&
            _eventLogState.Value.OpenLogs.Values.Any(log => !_concurrencyState.IsLoadedWithXml(log.Id)))
        {
            return;
        }

        if (_issuer.TryIssue(identity) is not { } sequence) { return; }

        Func<IEventColumnReader, EventLocator, bool> survives = FilterService.CompileSurvivorPredicate(filter);

        _dispatcher.Dispatch(new ViewRequestInvalidatedAction(sequence));

        _writer.EnqueueViewRequest(
            new ViewRequest(
                identity,
                sequence,
                identity.Scope,
                ScopeReaders(identity.Scope),
                state.SortContext,
                filter,
                (locator, reader) => survives(reader, locator)));
    }
}
