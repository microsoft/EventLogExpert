// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Services;

public sealed class BannerService : IBannerService
{
    private readonly IDatabaseService _databaseService;
    private readonly Lock _stateLock = new();
    private readonly ITraceLogger _traceLogger;

    private BannerProgressEntry? _backgroundProgress;
    private Exception? _currentCritical;
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
    }

    public event Action? StateChanged;

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

    public void DismissError(Guid id)
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

    public void DismissInfoBanner(Guid id)
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

    public Guid ReportError(string title, string message, string? actionLabel = null, Func<Task>? action = null)
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
        var entry = new ErrorBannerEntry(Guid.NewGuid(), title, message, normalizedLabel, normalizedAction, DateTime.UtcNow);

        lock (_stateLock)
        {
            _errorBanners = _errorBanners.Add(entry);
        }

        RaiseStateChanged();
        return entry.Id;
    }

    public void ReportInfoBanner(string title, string message, BannerSeverity severity)
    {
        var entry = new BannerInfoEntry(Guid.NewGuid(), title, message, severity, DateTime.UtcNow);

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
            // Only clear if the critical exception is still the one we set out to recover. If a newer one
            // was reported while the callback was running, leave it visible so the user sees the new state.
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
        catch { /* Ignore */ }
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

        // Iterate the invocation list so one throwing subscriber does not block subsequent ones —
        // a direct multicast Invoke aborts the chain on first exception.
        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action)subscriber).Invoke();
            }
            catch (Exception ex)
            {
                SafeLog(() => _traceLogger.Warn(
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
