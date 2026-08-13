// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Evaluation;
using System.Collections.Immutable;
using System.Threading.Channels;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed class OrderedViewWriter : IAsyncDisposable
{
    private const int DefaultTailBreachLimit = 3;
    private const int DefaultTailReplayBudget = 50_000;

    private readonly Task? _cadence;
    private readonly Channel<Command> _commandChannel;
    private readonly Task _owner;
    private readonly List<TaskCompletionSource<OrderedViewSnapshot>> _pendingDrain = [];
    private readonly int _publishEvery;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly OrderedViewState _state = new();
    private readonly int _tailBreachLimit;
    private readonly long _tailReplayBudget;

    private SortContext _adoptedConfig;
    private Filter _adoptedFilter;
    private int _adoptedGeneration;
    private ViewIdentity? _adoptedIdentity;
    private EventLogId? _adoptedLog;

    private int _adoptedScopeLogCount;

    private long _adoptedSequence;

    private int _buildsStarted;

    private (Task Task, CancellationTokenSource Cts)? _currentBuild;
    private RebuildRequest? _desiredBuild;
    private bool _dirty;

    private bool _faultAnnounced;

    private volatile Exception? _faulted;

    private long _highestSequence;
    private long _lastUpdateVersion;

    private PendingBuild? _pending;
    private Filter _pendingFilter;
    private int _pendingRebuilds;

    private bool _rebuildRequired;

    private bool _seededRowsAwaitingBuild;

    private int _sincePublish;

    private ImmutableHashSet<LogGeneration>? _singleLogInScope;
    private LogGeneration? _singleLogInScopeKey;
    private int _tailBreaches;

    public OrderedViewWriter(
        int publishEvery = 256,
        int publishIntervalMs = 16,
        long tailReplayBudget = DefaultTailReplayBudget,
        int tailBreachLimit = DefaultTailBreachLimit)
    {
        _publishEvery = Math.Max(1, publishEvery);
        _tailReplayBudget = Math.Max(0, tailReplayBudget);
        _tailBreachLimit = Math.Max(1, tailBreachLimit);
        _commandChannel = Channel.CreateUnbounded<Command>(new UnboundedChannelOptions { SingleReader = true });
        _owner = Task.Run(RunAsync);

        if (publishIntervalMs > 0) { _cadence = Task.Run(() => CadenceLoopAsync(publishIntervalMs)); }
    }

    public event Action<Exception, ViewIdentity?>? FaultRaised;

    public event Action<OrderedViewUpdate>? Updated;

    private enum CommandKind
    {
        ViewRequest,
        Reset,
        Adopt,
        RebuildFailed,
        Flush,
        Drain,
        Reconcile,
        RemoveLog,
        Clear,
        ClearFault
    }

    public OrderedViewSnapshot Current => _state.Current;

    public Exception? Faulted => _faulted;

    public long Generation => _state.Generation;

    public long ScopeVersion => _state.ScopeVersion;

    internal int BuildsStarted => _buildsStarted;

    internal Task? CurrentBuildTask => _currentBuild?.Task;

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _commandChannel.Writer.TryComplete();

        if (_cadence is not null)
        {
            try { await _cadence; }
            catch (OperationCanceledException) { }
        }
        
        await _owner;

        _desiredBuild = null;

        if (_currentBuild is { } build)
        {
            build.Cts.Cancel();

            try { await build.Task; }
            catch { /* cancelled/faulted build - ignore on dispose */ }

            build.Cts.Dispose();
            _currentBuild = null;
        }

        _shutdown.Dispose();
    }

    public async Task<OrderedViewSnapshot> DrainAsync()
    {
        var done = new TaskCompletionSource<OrderedViewSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_commandChannel.Writer.TryWrite(Command.ForDrain(done))) { done.TrySetResult(_state.Current); }

        return await done.Task;
    }

    public void EnqueueClear(ViewIdentity identity, long sequence) =>
        _commandChannel.Writer.TryWrite(Command.ForClear(identity, sequence));

    public void EnqueueClearFault() => _commandChannel.Writer.TryWrite(Command.ForClearFault());

    public void EnqueueFlush() => _commandChannel.Writer.TryWrite(Command.ForFlush());

    public void EnqueueReconcile(EventLogId logId, IEventColumnReader reader) =>
        _commandChannel.Writer.TryWrite(Command.ForReconcile(logId, reader));

    public void EnqueueRemoveLog(EventLogId logId) =>
        _commandChannel.Writer.TryWrite(Command.ForRemoveLog(logId));

    public void EnqueueResetLog(EventLogId logId, int newGeneration) =>
        _commandChannel.Writer.TryWrite(Command.ForReset(logId, newGeneration));

    public void EnqueueViewRequest(ViewRequest request) =>
        _commandChannel.Writer.TryWrite(Command.ForViewRequest(request));

    private OrderedViewUpdate BuildUpdate()
    {
        OrderedViewSnapshot snapshot = _state.Current;
        ImmutableHashSet<LogGeneration> inScope = _state.AdoptedInScope;

        if (_adoptedLog is { } log &&
            inScope.Contains(new LogGeneration(log, _adoptedGeneration)) &&
            snapshot.TryGetReaderByLog(log, _adoptedGeneration, out IEventColumnReader? reader))
        {
            var singleKey = new LogGeneration(log, _adoptedGeneration);

            if (_singleLogInScope is null || _singleLogInScopeKey != singleKey)
            {
                _singleLogInScope = [singleKey];
                _singleLogInScopeKey = singleKey;
            }

            return new OrderedViewReady(snapshot.Version,
                _adoptedIdentity,
                _adoptedSequence,
                log,
                _singleLogInScope,
                new OrderedColumnView(snapshot, reader),
                _adoptedConfig,
                _adoptedFilter)
            {
                ContentToken = ViewContentToken.From(_adoptedFilter, _singleLogInScope, snapshot)
            };
        }

        if (_adoptedLog is null && inScope.Count > 0)
        {
            return new OrderedViewReady(snapshot.Version,
                _adoptedIdentity,
                _adoptedSequence,
                null,
                inScope,
                new CombinedOrderedColumnView(snapshot, inScope),
                _adoptedConfig,
                _adoptedFilter)
            {
                ContentToken = ViewContentToken.From(_adoptedFilter, inScope, snapshot)
            };
        }

        if (_adoptedScopeLogCount > 0)
        {
            return new OrderedViewReady(snapshot.Version,
                _adoptedIdentity,
                _adoptedSequence,
                _adoptedLog,
                [],
                EmptyColumnView.Instance,
                _adoptedConfig,
                _adoptedFilter);
        }

        return new OrderedViewCleared(snapshot.Version, _adoptedIdentity, _adoptedSequence);
    }

    private async Task CadenceLoopAsync(int intervalMs)
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                await Task.Delay(intervalMs, _shutdown.Token);

                _commandChannel.Writer.TryWrite(Command.ForFlush());
            }
        }
        catch (OperationCanceledException) { }
    }

    private void CancelRunningBuild()
    {
        _currentBuild?.Cts.Cancel();
    }

    private void CompleteRebuild()
    {
        _pendingRebuilds--;
        DisposeCompletedBuild();

        if (_desiredBuild is { } desired)
        {
            _desiredBuild = null;
            StartRebuild(desired);
        }

        if (_pendingRebuilds != 0 || _pendingDrain.Count <= 0)
        {
            return;
        }

        PublishNow();

        foreach (var signal in _pendingDrain) { signal.TrySetResult(_state.Current); }

        _pendingDrain.Clear();
    }

    private void Dispatch(in Command command)
    {
        switch (command.Kind)
        {
            case CommandKind.ViewRequest:
                if (command.ViewRequest is { } viewRequest) { DispatchViewRequest(viewRequest); }

                break;
            case CommandKind.Reset:
                RebindAndStart(_state.BeginReset(command.LogId, command.Generation));

                break;
            case CommandKind.Reconcile:
                if (command.Reader is { } reconcileReader)
                {
                    bool reconciled;

                    try
                    {
                        reconciled = _state.ReconcileLog(command.LogId, reconcileReader);
                    }
                    catch (Exception reconcileFault)
                    {
                        RecordFault(reconcileFault);
                        RequireRebuild();

                        break;
                    }

                    if (reconciled)
                    {
                        _dirty = true;

                        if (_state.Current.Count == 0) { PublishNow(); }
                        else if (++_sincePublish >= _publishEvery) { PublishNow(); }
                    }
                }

                break;
            case CommandKind.RemoveLog:
                RebindAndStart(_state.RemoveLog(command.LogId));

                break;
            case CommandKind.Clear:
                if (command.Sequence <= _highestSequence) { break; }

                _highestSequence = command.Sequence;
                _state.Clear();

                CancelRunningBuild();
                _desiredBuild = null;
                _dirty = false;
                _sincePublish = 0;
                _adoptedLog = null;
                _adoptedScopeLogCount = 0;
                _adoptedIdentity = command.Identity;
                _adoptedSequence = command.Sequence;
                _pending = null;

                _rebuildRequired = false;
                _faultAnnounced = false;
                _seededRowsAwaitingBuild = false;
                _tailBreaches = 0;

                break;
            case CommandKind.Adopt:
                try
                {
                    AdoptOutcome outcome = AdoptOutcome.DroppedStale;

                    if (command is { Request: { } request, Rebuilt: { } rebuilt })
                    {
                        outcome = _state.TryAdoptRebuild(request, rebuilt, _tailReplayBudget, _tailBreaches < _tailBreachLimit);

                        if (outcome == AdoptOutcome.Adopted)
                        {
                            _tailBreaches = 0;
                            _dirty = false;
                            _sincePublish = 0;

                            _adoptedLog = request.SingleLog;
                            _adoptedScopeLogCount = request.Scope.LogCount;

                            _adoptedGeneration = _adoptedLog is { } adoptedLog ?
                                request.RequestedGeneration.GetValueOrDefault(adoptedLog) : 0;

                            _adoptedConfig = request.Context;
                            _adoptedFilter = _pendingFilter;

                            if (_pending is { } adopting && adopting.EngineGeneration == request.Generation)
                            {
                                _adoptedIdentity = adopting.Identity;
                                _adoptedSequence = adopting.Sequence;
                                _pending = null;
                            }

                            _rebuildRequired = false;
                            _faultAnnounced = false;
                            _seededRowsAwaitingBuild = false;
                        }
                    }

                    if (outcome == AdoptOutcome.AbandonedTail)
                    {
                        _tailBreaches++;
                        RebindAndStart(_state.CaptureScopeReseed());
                    }
                    else if (outcome != AdoptOutcome.Adopted && _seededRowsAwaitingBuild)
                    {
                        // rows a retag seeded onto it, exactly as a throwing one would.
                        RequireRebuild();
                    }
                }
                catch
                {
                    // A replay that throws abandons this build just as surely as an off-thread failure does, so its
                    // token has to go the same way. Left in place it would still name a build that no longer exists,
                    // and the next request for this same view would attach to it rather than start one - so nothing
                    // would ever be rebuilt and that identity would never be published. Generation-matched for the
                    // same reason the off-thread path is: a newer build may already have claimed the slot.
                    // A build that abandons here takes any rows a retag seeded onto it with it: the replay that was
                    // going to place them never runs, and no replacement was captured. Repairing is what keeps
                    // coverage honest about what the index holds.
                    if (_seededRowsAwaitingBuild) { RequireRebuild(); }

                    if (command.Request is { } abandoned &&
                        _pending is { } dead &&
                        dead.EngineGeneration == abandoned.Generation)
                    {
                        _pending = null;
                    }

                    throw;
                }
                finally
                {
                    CompleteRebuild();
                }

                break;
            case CommandKind.RebuildFailed:
                if (command.Error is { } error)
                {
                    var failed = command.Request;

                    bool owned = failed is null || failed.Generation == _state.Generation;

                    ViewIdentity? faultedIdentity = owned && _pending is { } carrying && failed is not null &&
                        carrying.EngineGeneration == failed.Generation ?
                        carrying.Identity :
                        null;

                    RecordFault(error, owned, faultedIdentity);

                    if (failed is not null)
                    {
                        _state.NotifyRebuildFailed(failed);

                        if (_pending is { } dead && dead.EngineGeneration == failed.Generation) { _pending = null; }
                    }
                }

                if (_seededRowsAwaitingBuild) { RequireRebuild(); }

                CompleteRebuild();

                break;
            case CommandKind.Flush:
                if (_dirty) { PublishNow(); }

                break;
            case CommandKind.ClearFault:
                _faulted = null;
                _faultAnnounced = false;

                break;
            case CommandKind.Drain:
                if (command.Signal is { } drainSignal)
                {
                    if (_pendingRebuilds == 0)
                    {
                        PublishNow();
                        drainSignal.TrySetResult(_state.Current);
                    }
                    else
                    {
                        _pendingDrain.Add(drainSignal);
                    }
                }

                break;
        }
    }

    private void DispatchViewRequest(ViewRequest request)
    {
        if (request.Sequence <= _highestSequence) { return; }

        _highestSequence = request.Sequence;

        if (_pending is { } pending &&
            pending.EngineGeneration == _state.Generation &&
            request.Identity.CoversSameViewAs(pending.Identity) &&
            _state.CoversSameGenerations(request.ScopeReaders))
        {
            if (_state.ReconcileScopeReaders(request.ScopeReaders)) { _seededRowsAwaitingBuild = true; }

            _pending = pending with { Identity = request.Identity, Sequence = request.Sequence };

            return;
        }

        if (!_rebuildRequired &&
            !_seededRowsAwaitingBuild &&
            _adoptedIdentity is { } adoptedIdentity &&
            request.Identity.CoversSameViewAs(adoptedIdentity) &&
            _state.CanRestampAdopted(request.ScopeLogs, request.ScopeReaders))
        {
            _state.SupersedeInFlight();
            CancelRunningBuild();
            _desiredBuild = null;
            _pending = null;
            _tailBreaches = 0;

            _state.RestoreRequestedFromAdopted();
            _pendingFilter = _adoptedFilter;
            _adoptedIdentity = request.Identity;
            _adoptedSequence = request.Sequence;
            PublishNow();

            return;
        }

        _pendingFilter = request.Filter;
        _state.TrySetActiveScope(request.ScopeLogs, request.Sequence);
        _state.ReconcileScopeReaders(request.ScopeReaders);

        RebuildRequest rebuild = _state.BeginRebuild(request.Predicate, request.Context, request.Hold);

        _tailBreaches = 0;
        _pending = new PendingBuild(request.Identity, request.Sequence, rebuild.Generation);
        StartRebuild(rebuild);
    }

    private void DisposeCompletedBuild()
    {
        _currentBuild?.Cts.Dispose();
        _currentBuild = null;
    }

    private void FailPendingDrain()
    {
        foreach (var signal in _pendingDrain) { signal.TrySetResult(_state.Current); }

        _pendingDrain.Clear();
    }

    private void PublishNow()
    {
        _state.Publish();
        _dirty = false;
        _sincePublish = 0;
    }

    private void RaiseUpdateIfAdvanced()
    {
        if (_rebuildRequired) { return; }

        long version = _state.Current.Version;

        if (version <= _lastUpdateVersion) { return; }

        Action<OrderedViewUpdate>? handler = Updated;

        if (handler is null) { return; }

        OrderedViewUpdate update;

        try
        {
            update = BuildUpdate();
        }
        catch (Exception buildFault)
        {
            _lastUpdateVersion = version;
            RecordFault(buildFault);

            return;
        }

        _lastUpdateVersion = version;

        try { handler(update); }
        catch (Exception subscriberFault) { RecordFault(subscriberFault, announce: false); }
    }

    private void RebindAndStart(RebuildRequest request)
    {
        if (_pending is { } pending) { _pending = pending with { EngineGeneration = request.Generation }; }

        StartRebuild(request);
    }

    private void RecordFault(Exception fault, bool announce = true, ViewIdentity? identity = null)
    {
        _faulted ??= fault;

        if (!announce || _faultAnnounced) { return; }

        _faultAnnounced = true;

        try { FaultRaised?.Invoke(fault, identity); }
        catch (Exception) { /* a broken fault subscriber must not mask the original fault or kill the owner loop */ }
    }

    private void RequireRebuild()
    {
        if (_rebuildRequired) { return; }

        _rebuildRequired = true;

        RebindAndStart(_state.CaptureScopeReseed());
    }

    private async Task RunAsync()
    {
        var reader = _commandChannel.Reader;

        while (await reader.WaitToReadAsync())
        {
            while (reader.TryRead(out var command))
            {
                try
                {
                    Dispatch(command);
                }
                catch (Exception ex)
                {
                    // One bad command must not kill the pipeline; record and drain.
                    RecordFault(ex);

                    if (command is { Kind: CommandKind.Drain, Signal: { } drainSignal }) { drainSignal.TrySetException(ex); }
                }

                RaiseUpdateIfAdvanced();
            }
        }

        FailPendingDrain();
    }

    private void StartBuild(RebuildRequest request)
    {
        _pendingRebuilds++;
        _buildsStarted++;

        var cts = new CancellationTokenSource();
        var token = cts.Token;

        var build = Task.Run(() =>
        {
            try
            {
                ChunkedOrderIndex rebuilt = OrderedViewState.BuildIndex(request, token);
                _commandChannel.Writer.TryWrite(Command.ForAdopt(request, rebuilt));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _commandChannel.Writer.TryWrite(Command.ForRebuildFailed(null));      // superseded/shutdown - expected, not a fault
            }
            catch (Exception ex)
            {
                // Includes an OperationCanceledException the token did NOT ask for: a predicate that throws one is a
                // faulty predicate, not a supersede, and misreporting it would swallow the fault and leave a held gate set.
                _commandChannel.Writer.TryWrite(Command.ForRebuildFailed(ex, request));   // a faulty predicate - a real fault
            }
        });

        _currentBuild = (build, cts);
    }

    private void StartRebuild(RebuildRequest request)
    {
        if (_shutdown.IsCancellationRequested) { return; }

        if (_pendingRebuilds > 0)
        {
            _desiredBuild = request;
            CancelRunningBuild();

            return;
        }

        StartBuild(request);
    }

    private readonly struct Command
    {
        public CommandKind Kind { get; private init; }

        public IEventColumnReader? Reader { get; private init; }

        public RebuildRequest? Request { get; private init; }

        public ChunkedOrderIndex? Rebuilt { get; private init; }

        public TaskCompletionSource<OrderedViewSnapshot>? Signal { get; private init; }

        public Exception? Error { get; private init; }

        public EventLogId LogId { get; private init; }

        public int Generation { get; private init; }

        public ViewIdentity? Identity { get; private init; }

        public long Sequence { get; private init; }

        public ViewRequest? ViewRequest { get; private init; }

        public static Command ForViewRequest(ViewRequest request) =>
            new() { Kind = CommandKind.ViewRequest, ViewRequest = request };

        public static Command ForReset(EventLogId logId, int generation) =>
            new() { Kind = CommandKind.Reset, LogId = logId, Generation = generation };

        public static Command ForReconcile(EventLogId logId, IEventColumnReader reader) =>
            new() { Kind = CommandKind.Reconcile, LogId = logId, Reader = reader };

        public static Command ForRemoveLog(EventLogId logId) =>
            new() { Kind = CommandKind.RemoveLog, LogId = logId };

        public static Command ForClear(ViewIdentity identity, long sequence) =>
            new() { Kind = CommandKind.Clear, Identity = identity, Sequence = sequence };

        public static Command ForAdopt(RebuildRequest request, ChunkedOrderIndex rebuilt) =>
            new() { Kind = CommandKind.Adopt, Request = request, Rebuilt = rebuilt };

        public static Command ForRebuildFailed(Exception? error, RebuildRequest? request = null) =>
            new() { Kind = CommandKind.RebuildFailed, Error = error, Request = request };

        public static Command ForFlush() => new() { Kind = CommandKind.Flush };

        public static Command ForClearFault() => new() { Kind = CommandKind.ClearFault };

        public static Command ForDrain(TaskCompletionSource<OrderedViewSnapshot> signal) =>
            new() { Kind = CommandKind.Drain, Signal = signal };
    }

    private sealed record PendingBuild(ViewIdentity Identity, long Sequence, long EngineGeneration);
}
