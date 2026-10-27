// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using System.Collections.Frozen;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Banner;

internal sealed class BannerService
    : IAttentionBannerService,
        IProgressBannerService,
        ICriticalErrorService,
        IErrorBannerService,
        IInfoBannerService,
        INotificationBulkDismissService
{
    private readonly IDatabaseService _databaseService;
    private readonly Lock _stateLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly ITraceLogger _traceLogger;

    private DateTime _attentionCreatedUtc;
    private bool _attentionDismissed;
    private ImmutableList<DatabaseEntry> _attentionEntries = ImmutableList<DatabaseEntry>.Empty;
    private long _attentionVersion;
    private BannerProgressEntry? _backgroundProgress;
    private Exception? _currentCritical;
    private ImmutableHashSet<string> _dismissedAttentionFileNames = ImmutableHashSet<string>.Empty;
    private ImmutableList<ErrorBannerEntry> _errorBanners = ImmutableList<ErrorBannerEntry>.Empty;
    private ImmutableList<BannerInfoEntry> _infoBanners = ImmutableList<BannerInfoEntry>.Empty;
    private BannerProgressEntry? _manageDatabasesProgress;
    private Func<Task>? _recoveryCallback;
    private object? _recoveryToken;

    public BannerService(IDatabaseService databaseService, ITraceLogger traceLogger, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(databaseService);
        ArgumentNullException.ThrowIfNull(traceLogger);

        _databaseService = databaseService;
        _traceLogger = traceLogger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _databaseService.UpgradeBatchStarted += OnUpgradeBatchStarted;
        _databaseService.UpgradeBatchProgress += OnUpgradeBatchProgress;
        _databaseService.UpgradeBatchCompleted += OnUpgradeBatchCompleted;
        _databaseService.EntriesChanged += OnEntriesChanged;
        OnEntriesChanged(this, EventArgs.Empty);
    }

    // Explicit accessors keep five separate facet invocation lists; locking preserves thread-safe subscriptions.
    event Action IAttentionBannerService.StateChanged
    {
        add { lock (_stateLock) { AttentionStateChanged += value; } }
        remove { lock (_stateLock) { AttentionStateChanged -= value; } }
    }

    event Action ICriticalErrorService.StateChanged
    {
        add { lock (_stateLock) { CriticalStateChanged += value; } }
        remove { lock (_stateLock) { CriticalStateChanged -= value; } }
    }

    event Action IErrorBannerService.StateChanged
    {
        add { lock (_stateLock) { ErrorStateChanged += value; } }
        remove { lock (_stateLock) { ErrorStateChanged -= value; } }
    }

    event Action IInfoBannerService.StateChanged
    {
        add { lock (_stateLock) { InfoStateChanged += value; } }
        remove { lock (_stateLock) { InfoStateChanged -= value; } }
    }

    event Action IProgressBannerService.StateChanged
    {
        add { lock (_stateLock) { ProgressStateChanged += value; } }
        remove { lock (_stateLock) { ProgressStateChanged -= value; } }
    }

    private event Action? AttentionStateChanged;

    private event Action? CriticalStateChanged;

    private event Action? ErrorStateChanged;

    private event Action? InfoStateChanged;

    private event Action? ProgressStateChanged;

    public DateTime AttentionCreatedUtc
    {
        get { lock (_stateLock) { return _attentionCreatedUtc; } }
    }

    public bool AttentionDismissed
    {
        get { lock (_stateLock) { return _attentionDismissed; } }
    }

    public IReadOnlyList<DatabaseEntry> AttentionEntries
    {
        get { lock (_stateLock) { return _attentionEntries; } }
    }

    public BannerProgressEntry? BackgroundProgress
    {
        get { lock (_stateLock) { return _backgroundProgress; } }
    }

    public Exception? CurrentCritical
    {
        get { lock (_stateLock) { return _currentCritical; } }
    }

    public IReadOnlyList<ErrorBannerEntry> ErrorBanners
    {
        get { lock (_stateLock) { return _errorBanners; } }
    }

    public IReadOnlyList<BannerInfoEntry> InfoBanners
    {
        get { lock (_stateLock) { return _infoBanners; } }
    }

    public BannerProgressEntry? ManageDatabasesProgress
    {
        get { lock (_stateLock) { return _manageDatabasesProgress; } }
    }

    public NotificationDismissSnapshot CaptureDismissSnapshot(bool includeAttention)
    {
        lock (_stateLock)
        {
            return new NotificationDismissSnapshot(
                [.. _errorBanners],
                [.. _infoBanners],
                includeAttention && !_attentionDismissed && !_attentionEntries.IsEmpty ? _attentionVersion : null);
        }
    }

    public void ClearCritical()
    {
        lock (_stateLock)
        {
            _currentCritical = null;
        }

        RaiseCriticalStateChanged();
    }

    public void DismissAttention()
    {
        bool changed;

        lock (_stateLock)
        {
            changed = !_attentionDismissed;
            if (changed)
            {
                _attentionDismissed = true;
                _dismissedAttentionFileNames =
                [
                    .. _attentionEntries
                        .Select(entry => entry.FileName)
                ];
            }
        }

        if (changed)
        {
            RaiseAttentionStateChanged();
        }
    }

    public void DismissError(BannerId id)
    {
        bool removed;

        lock (_stateLock)
        {
            ImmutableList<ErrorBannerEntry> next = _errorBanners.RemoveAll(entry => entry.Id == id);
            removed = next.Count != _errorBanners.Count;
            _errorBanners = next;
        }

        if (removed)
        {
            RaiseErrorStateChanged();
        }
    }

    public void DismissInfoBanner(BannerId id)
    {
        bool removed;

        lock (_stateLock)
        {
            ImmutableList<BannerInfoEntry> next = _infoBanners.RemoveAll(entry => entry.Id == id);
            removed = next.Count != _infoBanners.Count;
            _infoBanners = next;
        }

        if (removed)
        {
            RaiseInfoStateChanged();
        }
    }

    public int DismissSnapshot(NotificationDismissSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        bool errorsChanged;
        bool infosChanged;
        bool attentionChanged;
        int removedCount;

        lock (_stateLock)
        {
            HashSet<BannerId> errorIds = [.. snapshot.Errors.Select(entry => entry.Id)];
            HashSet<BannerId> infoIds = [.. snapshot.Infos.Select(entry => entry.Id)];
            ImmutableList<ErrorBannerEntry> remainingErrors = _errorBanners.RemoveAll(entry => errorIds.Contains(entry.Id));
            ImmutableList<BannerInfoEntry> remainingInfos = _infoBanners.RemoveAll(entry => infoIds.Contains(entry.Id));
            errorsChanged = remainingErrors.Count != _errorBanners.Count;
            infosChanged = remainingInfos.Count != _infoBanners.Count;
            attentionChanged = snapshot.AttentionVersion == _attentionVersion
                && !_attentionDismissed && !_attentionEntries.IsEmpty;
            removedCount = _errorBanners.Count - remainingErrors.Count + _infoBanners.Count - remainingInfos.Count
                + (attentionChanged ? 1 : 0);
            _errorBanners = remainingErrors;
            _infoBanners = remainingInfos;
            if (attentionChanged)
            {
                _attentionDismissed = true;
                _dismissedAttentionFileNames = [.. _attentionEntries.Select(entry => entry.FileName)];
            }
        }

        if (errorsChanged) { RaiseErrorStateChanged(); }

        if (infosChanged) { RaiseInfoStateChanged(); }
        
        if (attentionChanged) { RaiseAttentionStateChanged(); }
        
        return removedCount;
    }

    public IDisposable RegisterRecoveryCallback(Func<Task> recover)
    {
        ArgumentNullException.ThrowIfNull(recover);

        var registration = new RecoveryRegistration(this);

        lock (_stateLock)
        {
            _recoveryCallback = recover;
            _recoveryToken = registration;
        }

        return registration;
    }

    public void ReportCritical(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        lock (_stateLock)
        {
            _currentCritical = ex;
        }

        RaiseCriticalStateChanged();
    }

    public BannerId ReportError(BannerMessage content, Func<Task>? action = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        bool hasAction = action is not null;

        if (content.RequiresAction != hasAction)
        {
            throw new ArgumentException(
                "Banner content action requirement must match the provided action.",
                hasAction ? nameof(content) : nameof(action));
        }

        var entry = new ErrorBannerEntry(BannerId.Create(), content, action, _timeProvider.GetUtcNow().UtcDateTime);

        lock (_stateLock)
        {
            _errorBanners = _errorBanners.Add(entry);
        }

        RaiseErrorStateChanged();

        return entry.Id;
    }

    public void ReportInfoBanner(BannerMessage content, BannerSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.RequiresAction)
        {
            throw new ArgumentException("Info banners cannot contain action content.", nameof(content));
        }

        var entry = new BannerInfoEntry(BannerId.Create(), content, severity, _timeProvider.GetUtcNow().UtcDateTime);

        lock (_stateLock)
        {
            _infoBanners = _infoBanners.Add(entry);
        }

        RaiseInfoStateChanged();
    }

    public async Task TryRecoverAsync()
    {
        Exception? snapshotCritical;
        Func<Task>? callback;

        lock (_stateLock)
        {
            snapshotCritical = _currentCritical;
            callback = _recoveryCallback;
        }

        if (callback is not null)
        {
            await callback();
        }

        bool cleared = false;

        lock (_stateLock)
        {
            if (ReferenceEquals(_currentCritical, snapshotCritical))
            {
                _currentCritical = null;
                cleared = true;
            }
        }

        if (cleared)
        {
            RaiseCriticalStateChanged();
        }
    }

    private static void SafeLog(Action log)
    {
        try { log(); }
        catch { /* Logger faults must not propagate from defensive logging sites. */ }
    }

    private void AssignProgressSlot(UpgradeProgressScope scope, BannerProgressEntry entry)
    {
        switch (scope)
        {
            case UpgradeProgressScope.Background:
                _backgroundProgress = entry;
                break;
            case UpgradeProgressScope.ManageDatabasesTriggered:
                _manageDatabasesProgress = entry;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown upgrade progress scope.");
        }
    }

    private void OnEntriesChanged(object? sender, EventArgs args)
    {
        bool stateChanged;

        lock (_stateLock)
        {
            ImmutableList<DatabaseEntry> newAttention =
            [
                .. _databaseService.Entries
                    .Where(entry => entry.Status is DatabaseStatus.UpgradeRequired
                        or DatabaseStatus.UpgradeFailed
                        or DatabaseStatus.UnrecognizedSchema
                        or DatabaseStatus.ObsoleteSchema
                        or DatabaseStatus.ClassificationFailed)
            ];

            bool hadAttention = !_attentionEntries.IsEmpty;
            stateChanged = !_attentionEntries.SequenceEqual(newAttention);
            _attentionEntries = newAttention;
            bool attentionResumed = false;

            if (_attentionDismissed)
            {
                foreach (var entry in newAttention)
                {
                    if (_dismissedAttentionFileNames.Contains(entry.FileName))
                    {
                        continue;
                    }

                    _attentionDismissed = false;
                    attentionResumed = true;
                    _dismissedAttentionFileNames = ImmutableHashSet<string>.Empty;
                    stateChanged = true;
                    break;
                }
            }

            if (stateChanged) { _attentionVersion++; }

            if (newAttention.IsEmpty)
            {
                _attentionCreatedUtc = default;
            }
            else if (!hadAttention || attentionResumed)
            {
                _attentionCreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
            }
        }

        // Single raise per OnEntriesChanged invocation — even if both `_attentionEntries`
        // and `_attentionDismissed` flipped inside the same lock, the Attention facet
        // observers see exactly one StateChanged. Prevents BannerHost double-render.
        if (stateChanged)
        {
            RaiseAttentionStateChanged();
        }
    }

    private void OnUpgradeBatchCompleted(object? sender, UpgradeBatchCompletedEventArgs args)
    {
        bool cleared = false;

        lock (_stateLock)
        {
            if (_backgroundProgress is not null && _backgroundProgress.BatchId == args.BatchId)
            {
                _backgroundProgress = null;
                cleared = true;
            }
            else if (_manageDatabasesProgress is not null && _manageDatabasesProgress.BatchId == args.BatchId)
            {
                _manageDatabasesProgress = null;
                cleared = true;
            }
        }

        if (cleared)
        {
            RaiseProgressStateChanged();
        }
    }

    private void OnUpgradeBatchProgress(object? sender, UpgradeBatchProgressEventArgs args)
    {
        BannerProgressEntry? next = null;

        lock (_stateLock)
        {
            BannerProgressEntry? backgroundSlot = _backgroundProgress;
            BannerProgressEntry? manageDatabasesSlot = _manageDatabasesProgress;

            if (backgroundSlot is not null && backgroundSlot.BatchId == args.BatchId)
            {
                next = backgroundSlot with
                {
                    CurrentBatchPosition = args.Position,
                    CurrentEntryName = args.FileName,
                    CurrentPhase = args.Phase,
                    QueuedBatchesAfter = _databaseService.QueuedBatchCount
                };
                _backgroundProgress = next;
            }
            else if (manageDatabasesSlot is not null && manageDatabasesSlot.BatchId == args.BatchId)
            {
                next = manageDatabasesSlot with
                {
                    CurrentBatchPosition = args.Position,
                    CurrentEntryName = args.FileName,
                    CurrentPhase = args.Phase,
                    QueuedBatchesAfter = _databaseService.QueuedBatchCount
                };
                _manageDatabasesProgress = next;
            }
        }

        if (next is not null)
        {
            RaiseProgressStateChanged();
        }
    }

    private void OnUpgradeBatchStarted(object? sender, UpgradeBatchStartedEventArgs args)
    {
        var entry = new BannerProgressEntry(
            args.BatchId,
            args.Scope,
            0,
            args.BatchSize,
            string.Empty,
            UpgradePhase.BackingUp,
            _databaseService.QueuedBatchCount,
            args.Cancel)
        {
            BatchFileNames = args.FileNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase)
        };

        lock (_stateLock)
        {
            AssignProgressSlot(args.Scope, entry);
        }

        RaiseProgressStateChanged();
    }

    // Literal facet-qualified names distinguish logs; nameof on any facet event yields only "StateChanged".
    private void RaiseAttentionStateChanged() =>
        RaiseSafely(AttentionStateChanged, "IAttentionBannerService.StateChanged");

    private void RaiseCriticalStateChanged() =>
        RaiseSafely(CriticalStateChanged, "ICriticalErrorService.StateChanged");

    private void RaiseErrorStateChanged() =>
        RaiseSafely(ErrorStateChanged, "IErrorBannerService.StateChanged");

    private void RaiseInfoStateChanged() =>
        RaiseSafely(InfoStateChanged, "IInfoBannerService.StateChanged");

    private void RaiseProgressStateChanged() =>
        RaiseSafely(ProgressStateChanged, "IProgressBannerService.StateChanged");

    // Single shared fault-isolation helper. Each facet's RaiseXxxStateChanged
    // method is a one-line delegation through here so the foreach + try/catch
    // shape exists exactly once in the file (catalog rule code-duplication-dry
    // REPEAT-MISS DISCIPLINE — third occurrence avoided).
    private void RaiseSafely(Action? handler, string eventName)
    {
        if (handler is null) { return; }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action)subscriber).Invoke();
            }
            catch (Exception ex)
            {
                SafeLog(() => _traceLogger.Warning(
                    $"{nameof(BannerService)}.{eventName}: subscriber threw: {ex}"));
            }
        }
    }

    private void UnregisterRecoveryIfActive(object token)
    {
        lock (_stateLock)
        {
            if (!ReferenceEquals(_recoveryToken, token))
            {
                return;
            }

            _recoveryCallback = null;
            _recoveryToken = null;
        }
    }

    private sealed class RecoveryRegistration(BannerService service) : IDisposable
    {
        private readonly BannerService _service = service;

        public void Dispose() => _service.UnregisterRecoveryIfActive(this);
    }
}
