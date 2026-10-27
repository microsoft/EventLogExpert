// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Histogram;
using Fluxor;
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

    private ViewProbe Probe()
    {
        LogTableState state = _logTableState.Value;
        ViewIdentity identity = state.ViewIdentity;
        RawEventStoreState rawStore = _rawEventStore.Value;
        Dictionary<EventLogId, IEventColumnReader> readers = new(identity.Scope.Length);

        foreach (EventLogId logId in identity.Scope)
        {
            if (rawStore.ByLog.TryGetValue(logId, out EventColumnStore? store))
            {
                readers[logId] = store.CreateReader(logId);
            }
        }

        return new ViewProbe(identity, readers, state.SortContext);
    }

    private void Reconcile(EventLogId logId, bool isReplace)
    {
        if (_rawEventStore.Value.ByLog.TryGetValue(logId, out var store))
        {
            _writer.EnqueueReconcile(logId, store.CreateReader(logId), isReplace);
        }
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

        // Arm before the gate check so a deferred gate holds the entitlement for the next successful issue.
        if (force == ReissueForce.Always)
        {
            _issuer.ArmForce();
        }
        else if (force == ReissueForce.WhenFaulted && state.PresentationState == PresentationState.Faulted)
        {
            _issuer.ArmRecovery();
        }

        if (XmlFilterGate.IsDeferred(filter, _eventLogState.Value, _rawEventStore.Value, _concurrencyState, _matchCache))
        {
            return;
        }

        IssueOutcome outcome = _issuer.TryIssue(identity, Probe);

        if (outcome is not { Kind: IssueKind.Issued, Readers: { } readers }) { return; }

        if (outcome.ClearFault) { _writer.EnqueueClearFault(); }

        Func<IEventColumnReader, EventLocator, bool> survives =
            XmlFilterGate.BuildSurvivorPredicate(filter, _concurrencyState, _matchCache);

        _dispatcher.Dispatch(new ViewRequestInvalidatedAction(outcome.Sequence));

        _writer.EnqueueViewRequest(
            new ViewRequest(
                identity,
                outcome.Sequence,
                identity.Scope,
                readers,
                outcome.SortContext,
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
