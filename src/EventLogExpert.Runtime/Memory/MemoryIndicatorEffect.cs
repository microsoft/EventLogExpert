// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using IDispatcher = Fluxor.IDispatcher;
using LogTableCloseLogAction = EventLogExpert.Runtime.LogTable.CloseLogAction;

namespace EventLogExpert.Runtime.Memory;

/// <summary>
///     Samples the managed heap on a cheap 1 Hz cadence (no forced GC on the sampling path) and publishes an advisory
///     <see cref="MemoryIndicatorRecomputedAction" /> when the displayed whole-MiB value or the effective
///     <see cref="MemoryUsageLevel" /> changes. A user-initiated log close schedules one deadline-gated, non-blocking
///     background gen2 so the reclaimed heap becomes visible without stalling the UI. The schedule is armed by the
///     terminal close (after the store is actually unrooted) but only when a <see cref="LogClosedByUserAction" /> was
///     recorded, so filter-driven XML reloads (which close-and-reopen via the same terminal action, without the user
///     action) never trigger a collection.
/// </summary>
internal sealed class MemoryIndicatorEffect : IDisposable
{
    private const long BytesPerMebibyte = 1024L * 1024L;

    // Color bands measure the app's managed heap against a fraction of the physical memory that was free to the app
    // (Elevated at 50%, High at 75% of available RAM). Basing this on free rather than total memory keeps the bands
    // meaningful on a busy machine and reachable on a large one - it is the headroom the app can actually grow into.
    private const double ElevatedAvailableFraction = 0.50;
    private const double HighAvailableFraction = 0.75;

    private readonly Lock _closeGate = new();
    private readonly long _collectDelayTicks;
    private readonly IDispatcher _dispatcher;
    private readonly Lock _gate = new();
    private readonly TimeSpan _interval;
    private readonly long _levelDwellTicks;
    private readonly ITraceLogger _logger;
    private readonly IProcessMemoryMeter _meter;
    private readonly Func<long> _now;
    private readonly HashSet<EventLogId> _pendingUserClosedLogs = [];
    private readonly Timer _timer;

    private MemoryUsageLevel _candidateLevel = MemoryUsageLevel.Normal;
    private long _candidateSince;
    private long _collectDueAt;
    private bool _disposed;
    private long _elevatedBytes;
    private long _highBytes;
    private long _lastDispatchedMebibytes = -1;
    private MemoryUsageLevel _lastLevel = MemoryUsageLevel.Normal;
    private EventLogId? _lastTerminalClose;
    private int _sampling;
    private bool _thresholdsReady;

    public MemoryIndicatorEffect(
        IProcessMemoryMeter meter,
        IDispatcher dispatcher,
        [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger,
        TimeSpan? sampleInterval = null,
        TimeSpan? closeReclaimDelay = null,
        TimeSpan? levelDwell = null,
        long? elevatedBytes = null,
        long? highBytes = null,
        Func<long>? monotonicTimestampProvider = null)
    {
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);

        _meter = meter;
        _dispatcher = dispatcher;
        _logger = logger;
        _interval = sampleInterval ?? TimeSpan.FromSeconds(1);
        _now = monotonicTimestampProvider ?? Stopwatch.GetTimestamp;
        _collectDelayTicks = ToTicks(closeReclaimDelay ?? TimeSpan.FromSeconds(1.5));
        _levelDwellTicks = ToTicks(levelDwell ?? TimeSpan.FromSeconds(2));

        if (elevatedBytes is { } elevated && highBytes is { } high)
        {
            if (elevated <= 0 || elevated >= high)
            {
                throw new ArgumentException(
                    $"Memory thresholds must satisfy 0 < elevated ({elevated}) < high ({high}).",
                    nameof(elevatedBytes));
            }

            _elevatedBytes = elevated;
            _highBytes = high;
            _thresholdsReady = true;
        }

        // Otherwise the thresholds derive lazily from available RAM on the first sample that can read it (see
        // EnsureThresholds); until then the indicator stays Normal rather than sizing against an unknown machine.

        // Constructed disarmed; StoreInitializedAction arms it so no sample can precede store initialization.
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }

            _disposed = true;
            _timer.Dispose();
        }
    }

    [EffectMethod(typeof(CloseAllLogsAction))]
    public Task HandleCloseAllLogs(IDispatcher dispatcher)
    {
        lock (_closeGate)
        {
            _pendingUserClosedLogs.Clear();
            _lastTerminalClose = null;
        }

        ScheduleCloseReclaim();

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLogClosed(LogTableCloseLogAction action, IDispatcher dispatcher)
    {
        // The terminal close fires for both user closes and filter-driven reloads, and may arrive before OR after the
        // matching LogClosedByUserAction (the user-close path dispatches them separately, and Fluxor can drain the
        // terminal synchronously). Reclaim now if the user intent was already recorded; otherwise remember this
        // terminal so a user marker arriving next can reclaim. A reload never emits LogClosedByUserAction, so its
        // terminal is remembered but never acted on.
        bool reclaim;

        lock (_closeGate)
        {
            reclaim = _pendingUserClosedLogs.Remove(action.LogId);

            if (!reclaim) { _lastTerminalClose = action.LogId; }
        }

        if (reclaim) { ScheduleCloseReclaim(); }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleLogClosedByUser(LogClosedByUserAction action, IDispatcher dispatcher)
    {
        // If the terminal close already ran for this log the store is unrooted, so reclaim now; otherwise record the
        // intent for the terminal close that follows. Either way the gen2 lands after the store is dropped.
        bool reclaim;

        lock (_closeGate)
        {
            // EventLogId is unique per open, so a matching remembered terminal is exactly this log's close.
            reclaim = _lastTerminalClose == action.LogId;

            if (reclaim) { _lastTerminalClose = null; }
            else { _pendingUserClosedLogs.Add(action.LogId); }
        }

        if (reclaim) { ScheduleCloseReclaim(); }

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(StoreInitializedAction))]
    public Task HandleStoreInitialized(IDispatcher dispatcher)
    {
        Arm(TimeSpan.Zero);

        return Task.CompletedTask;
    }

    internal void Tick()
    {
        // Single-flight: a re-entrant callback (or a manual test drive overlapping a timer tick) exits immediately.
        if (Interlocked.Exchange(ref _sampling, 1) == 1) { return; }

        try
        {
            if (Volatile.Read(ref _disposed)) { return; }

            long due = Volatile.Read(ref _collectDueAt);

            if (due != 0 && _now() >= due)
            {
                _meter.RequestBackgroundReclaim();

                // CAS-clear so a second close landing between the read and the clear keeps its later deadline.
                Interlocked.CompareExchange(ref _collectDueAt, 0, due);
            }

            long heapBytes = _meter.GetManagedHeapBytes();
            long usedMebibytes = heapBytes / BytesPerMebibyte;
            EnsureThresholds();
            MemoryUsageLevel effective = ResolveEffectiveLevel(LevelFor(heapBytes));

            if (usedMebibytes == _lastDispatchedMebibytes && effective == _lastLevel) { return; }

            long workingSet = _meter.GetWorkingSetBytes();

            if (Volatile.Read(ref _disposed)) { return; }

            _dispatcher.Dispatch(new MemoryIndicatorRecomputedAction(usedMebibytes, effective, workingSet));

            // Commit the last-dispatched markers only after a successful dispatch, so a throwing dispatch is retried on
            // the next tick rather than silently swallowed.
            _lastDispatchedMebibytes = usedMebibytes;
            _lastLevel = effective;
        }
        catch (Exception fault)
        {
            _logger.Trace($"{nameof(MemoryIndicatorEffect)}: a sample failed and was isolated: {fault}");
        }
        finally
        {
            Volatile.Write(ref _sampling, 0);
            Arm(_interval);
        }
    }

    private static long NonZero(long value) => value == 0 ? 1 : value;

    private static long ToTicks(TimeSpan span) => (long)(span.TotalSeconds * Stopwatch.Frequency);

    private void Arm(TimeSpan due)
    {
        lock (_gate)
        {
            if (_disposed) { return; }

            try { _timer.Change(due, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
    }

    // Sizes the color bands to the memory that was free to the app once a load reading is available. Runs on the tick
    // thread (single-flighted), so the plain fields need no lock.
    private void EnsureThresholds()
    {
        if (_thresholdsReady) { return; }

        long availableBytes = _meter.GetAvailablePhysicalBytes();

        if (availableBytes <= 0) { return; }

        long elevated = (long)(ElevatedAvailableFraction * availableBytes);
        long high = (long)(HighAvailableFraction * availableBytes);

        // Hold the same invariant the injected path enforces; a pathologically tiny reading that collapses the bands is
        // ignored until a real one arrives.
        if (elevated <= 0 || elevated >= high) { return; }

        _elevatedBytes = elevated;
        _highBytes = high;
        _thresholdsReady = true;
    }

    private MemoryUsageLevel LevelFor(long heapBytes) =>
        !_thresholdsReady ? MemoryUsageLevel.Normal :
        heapBytes >= _highBytes ? MemoryUsageLevel.High :
        heapBytes >= _elevatedBytes ? MemoryUsageLevel.Elevated :
        MemoryUsageLevel.Normal;

    private MemoryUsageLevel ResolveEffectiveLevel(MemoryUsageLevel raw)
    {
        if (raw == _lastLevel)
        {
            _candidateLevel = raw;

            return raw;
        }

        if (raw != _candidateLevel)
        {
            _candidateLevel = raw;
            _candidateSince = _now();

            return _lastLevel;
        }

        // The candidate differs from the effective level; promote it only once it has held for the dwell window, so a
        // value oscillating across a threshold at 1 Hz does not flicker the chip color or spam announcements.
        return _now() - _candidateSince >= _levelDwellTicks ? raw : _lastLevel;
    }

    private void ScheduleCloseReclaim() => Volatile.Write(ref _collectDueAt, NonZero(_now() + _collectDelayTicks));
}
