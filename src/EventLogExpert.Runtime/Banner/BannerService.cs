// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.Database.Upgrade;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Banner;

internal sealed class BannerService : IBannerService
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

    public event Action? StateChanged;

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

        RaiseStateChanged();
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
            RaiseStateChanged();
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
            RaiseStateChanged();
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
            RaiseStateChanged();
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

        RaiseStateChanged();
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

        RaiseStateChanged();
        return entry.Id;
    }

    public void ReportInfoBanner(string title, string message, BannerSeverity severity)
    {
        var entry = new BannerInfoEntry(BannerId.Create(), title, message, severity, DateTime.UtcNow);

        lock (_stateLock)
        {
            _infoBanners = _infoBanners.Add(entry);
        }

        RaiseStateChanged();
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
            RaiseStateChanged();
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

        if (stateChanged)
        {
            RaiseStateChanged();
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
            RaiseStateChanged();
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
            RaiseStateChanged();
        }
    }

    private void OnUpgradeBatchStarted(object? sender, UpgradeBatchStartedEventArgs args)
    {
        var entry = new BannerProgressEntry(
            args.BatchId,
            args.Scope,
            CurrentBatchPosition: 0,
            CurrentBatchSize: args.BatchSize,
            CurrentEntryName: string.Empty,
            CurrentPhase: UpgradePhase.BackingUp,
            QueuedBatchesAfter: _databaseService.QueuedBatchCount,
            Cancel: args.Cancel);

        lock (_stateLock)
        {
            AssignProgressSlot(args.Scope, entry);
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        var handler = StateChanged;

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
                    $"{nameof(BannerService)}.{nameof(StateChanged)}: subscriber threw: {ex}"));
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
