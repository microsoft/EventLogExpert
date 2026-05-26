// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Banner;

internal sealed class BannerService
    : IAttentionBannerService,
        IProgressBannerService,
        ICriticalErrorService,
        IErrorBannerService,
        IInfoBannerService
{
    private readonly IDatabaseService _databaseService;
    private readonly Lock _stateLock = new();
    private readonly ITraceLogger _traceLogger;

    private bool _attentionDismissed;
    private ImmutableList<DatabaseEntry> _attentionEntries = ImmutableList<DatabaseEntry>.Empty;
    private BannerProgressEntry? _backgroundProgress;
    private Exception? _currentCritical;
    private ImmutableHashSet<string> _dismissedAttentionFileNames = ImmutableHashSet<string>.Empty;
    private ImmutableList<ErrorBannerEntry> _errorBanners = ImmutableList<ErrorBannerEntry>.Empty;
    private ImmutableList<BannerInfoEntry> _infoBanners = ImmutableList<BannerInfoEntry>.Empty;
    private Func<Task>? _recoveryCallback;
    private object? _recoveryToken;
    private BannerProgressEntry? _settingsProgress;

    public BannerService(IDatabaseService databaseService, ITraceLogger traceLogger)
    {
        ArgumentNullException.ThrowIfNull(databaseService);
        ArgumentNullException.ThrowIfNull(traceLogger);

        _databaseService = databaseService;
        _traceLogger = traceLogger;
        _databaseService.UpgradeBatchStarted += OnUpgradeBatchStarted;
        _databaseService.UpgradeBatchProgress += OnUpgradeBatchProgress;
        _databaseService.UpgradeBatchCompleted += OnUpgradeBatchCompleted;
        _databaseService.EntriesChanged += OnEntriesChanged;
        OnEntriesChanged(this, EventArgs.Empty);
    }

    // The five explicit-interface event accessors below are an unavoidable
    // 5× duplication: C# does not allow an explicit-interface event accessor
    // to delegate its add/remove implementation to another helper. The
    // duplication is intentional and necessary to preserve five distinct
    // invocation lists on the shared backing-store BannerService instance.
    // Each accessor synchronizes through `_stateLock` to preserve the
    // thread-safe add/remove guarantee the compiler-synthesized field-like
    // event used to provide.

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

    // Five separate backing delegate fields — one per facet. Each interface's
    // `event Action StateChanged` is wired through an explicit-interface accessor
    // below; this guarantees five distinct invocation lists rather than one
    // shared multicast (which a single `public event Action? StateChanged;`
    // would silently produce).
    private event Action? AttentionStateChanged;

    private event Action? CriticalStateChanged;

    private event Action? ErrorStateChanged;

    private event Action? InfoStateChanged;

    private event Action? ProgressStateChanged;

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

    public BannerProgressEntry? SettingsProgress
    {
        get { lock (_stateLock) { return _settingsProgress; } }
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
                _dismissedAttentionFileNames = _attentionEntries
                    .Select(entry => entry.FileName)
                    .ToImmutableHashSet();
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

    public BannerId ReportError(string title, string message, string? actionLabel = null, Func<Task>? action = null)
    {
        bool hasAction = action is not null;
        bool hasLabel = !string.IsNullOrWhiteSpace(actionLabel);

        if (hasAction != hasLabel)
        {
            throw new ArgumentException(
                "actionLabel and action must both be provided together, or both omitted.",
                hasAction ? nameof(actionLabel) : nameof(action));
        }

        string? normalizedLabel = hasLabel ? actionLabel : null;
        Func<Task>? normalizedAction = hasAction ? action : null;
        var entry = new ErrorBannerEntry(BannerId.Create(), title, message, normalizedLabel, normalizedAction, DateTime.UtcNow);

        lock (_stateLock)
        {
            _errorBanners = _errorBanners.Add(entry);
        }

        RaiseErrorStateChanged();
        return entry.Id;
    }

    public void ReportInfoBanner(string title, string message, BannerSeverity severity)
    {
        var entry = new BannerInfoEntry(BannerId.Create(), title, message, severity, DateTime.UtcNow);

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
            case UpgradeProgressScope.SettingsTriggered:
                _settingsProgress = entry;
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
            ImmutableList<DatabaseEntry> newAttention = _databaseService.Entries
                .Where(entry => entry.Status is DatabaseStatus.UpgradeRequired
                    or DatabaseStatus.UpgradeFailed
                    or DatabaseStatus.UnrecognizedSchema
                    or DatabaseStatus.ObsoleteSchema
                    or DatabaseStatus.ClassificationFailed)
                .ToImmutableList();

            stateChanged = !_attentionEntries.SequenceEqual(newAttention);
            _attentionEntries = newAttention;

            if (_attentionDismissed)
            {
                foreach (var entry in newAttention)
                {
                    if (_dismissedAttentionFileNames.Contains(entry.FileName))
                    {
                        continue;
                    }

                    _attentionDismissed = false;
                    _dismissedAttentionFileNames = ImmutableHashSet<string>.Empty;
                    stateChanged = true;
                    break;
                }
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
            else if (_settingsProgress is not null && _settingsProgress.BatchId == args.BatchId)
            {
                _settingsProgress = null;
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
            BannerProgressEntry? settingsSlot = _settingsProgress;

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
            else if (settingsSlot is not null && settingsSlot.BatchId == args.BatchId)
            {
                next = settingsSlot with
                {
                    CurrentBatchPosition = args.Position,
                    CurrentEntryName = args.FileName,
                    CurrentPhase = args.Phase,
                    QueuedBatchesAfter = _databaseService.QueuedBatchCount
                };
                _settingsProgress = next;
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
            args.Cancel);

        lock (_stateLock)
        {
            AssignProgressSlot(args.Scope, entry);
        }

        RaiseProgressStateChanged();
    }

    // Per-facet raisers — single-line delegations to RaiseSafely. String literals
    // (not nameof) on the eventName argument so each facet's warning log is
    // distinguishable: nameof(IXyz.StateChanged) collapses to "StateChanged"
    // for all five, defeating the diagnostic intent of the split.
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
