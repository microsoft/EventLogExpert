// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
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
    EventLogConcurrencyState concurrencyState,
    XmlFilterMatchCache matchCache)
{
    private readonly OrderedViewDispatchBridge _bridge = bridge;
    private readonly EventLogConcurrencyState _concurrencyState = concurrencyState;
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly ViewRequestIssuer _issuer = issuer;
    private readonly IState<LogTableState> _logTableState = logTableState;
    private readonly XmlFilterMatchCache _matchCache = matchCache;
    private readonly IState<RawEventStoreState> _rawEventStore = rawEventStore;
    private readonly OrderedViewWriter _writer = writer;

    private enum ReissueForce { None, WhenFaulted, Always }

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

            if (XmlDeferred()) { return; }

            foreach (EventLogId logId in action.EventsByLog.Keys) { Reconcile(logId, action.Mode == RawIngestMode.Replace); }
        });

    [EffectMethod(typeof(LoadColumnsCompletedAction))]
    public Task HandleLoadColumnsCompleted(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod]
    public Task HandleLoadEvents(LoadEventsAction action, IDispatcher dispatcher) =>
        Shadow(() =>
        {
            Sync();

            if (!XmlDeferred()) { Reconcile(action.LogData.Id, isReplace: true); }
        });

    [EffectMethod]
    public Task HandleLoadEventsPartial(LoadEventsPartialAction action, IDispatcher dispatcher) =>
        Shadow(() =>
        {
            Sync();

            if (!XmlDeferred()) { Reconcile(action.LogData.Id, isReplace: false); }
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
    public Task HandleSetGroupBy(SetGroupByAction action, IDispatcher dispatcher) => Shadow(SyncAbsoluteOrdering);

    [EffectMethod]
    public Task HandleSetHistogramVisible(SetHistogramVisibleAction action, IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod]
    public Task HandleSetOrderBy(SetOrderByAction action, IDispatcher dispatcher) => Shadow(SyncAbsoluteOrdering);

    [EffectMethod(typeof(SetTabGroupCollapsedAction))]
    public Task HandleSetTabGroupCollapsed(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod(typeof(ToggleGroupSortingAction))]
    public Task HandleToggleGroupSorting(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod(typeof(ToggleSortingAction))]
    public Task HandleToggleSorting(IDispatcher dispatcher) => Shadow(Sync);

    [EffectMethod(typeof(XmlFilterMatchReadyAction))]
    public Task HandleXmlFilterMatchReady(IDispatcher dispatcher) => Shadow(() => Sync(ReissueForce.Always));

    private void Reconcile(EventLogId logId, bool isReplace)
    {
        if (_rawEventStore.Value.ByLog.TryGetValue(logId, out var store))
        {
            _writer.EnqueueReconcile(logId, store.CreateReader(logId), isReplace);
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

    private void Sync() => Sync(ReissueForce.None);

    private void Sync(ReissueForce force)
    {
        LogTableState state = _logTableState.Value;
        ViewIdentity identity = state.ViewIdentity;
        Filter filter = identity.Filter;

        // Arm the sticky force BEFORE the gate check: the gate scans every open log but the ViewIdentity covers only the
        // active scope, so an out-of-scope log can reopen the gate and let a racing Sync consume the de-dup before a
        // post-gate arm runs. Arming first lets that racing (or next) TryIssue force past the de-dup; a non-deferred
        // Always consumes it immediately via forceReissue. WhenFaulted is deliberately not armed (pre-existing, tracked).
        if (force == ReissueForce.Always) { _issuer.ArmForce(); }

        if (XmlFilterGate.IsDeferred(filter, _eventLogState.Value, _rawEventStore.Value, _concurrencyState, _matchCache))
        {
            return;
        }

        // SetOrderBy/SetGroupBy are absolute ordering requests and the user's manual recovery path while faulted
        // (parameterless toggles stay inert). When such a request equals the already-masked requested value - most
        // often clicking a remounted clear chip's x after a clear CAUSED the fault, where the requested column is
        // already null - the ViewIdentity is unchanged and the issuer would de-dup the failed identity, so the retry
        // would never reach the writer. WhenFaulted re-trusts the engine (EnqueueClearFault) and forces the re-issue
        // atomically past the de-dup; Always forces unconditionally (XmlFilterMatchReady: the store/match stamp changed
        // under an unchanged identity) without clearing the fault.
        bool clearFault = force == ReissueForce.WhenFaulted && state.PresentationState == PresentationState.Faulted;
        bool forceReissue = force == ReissueForce.Always || clearFault;

        if (clearFault) { _writer.EnqueueClearFault(); }

        if (_issuer.TryIssue(identity, forceReissue) is not { } sequence) { return; }

        Func<IEventColumnReader, EventLocator, bool> survives =
            XmlFilterGate.BuildSurvivorPredicate(filter, _concurrencyState, _matchCache);

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

    private void SyncAbsoluteOrdering() => Sync(ReissueForce.WhenFaulted);

    private bool XmlDeferred() =>
        XmlFilterGate.IsDeferred(
            _logTableState.Value.ViewIdentity.Filter,
            _eventLogState.Value,
            _rawEventStore.Value,
            _concurrencyState,
            _matchCache);
}
