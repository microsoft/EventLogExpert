// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Services;

public sealed class BannerService : IBannerService
{
    private readonly Lock _stateLock = new();

    private ImmutableList<CriticalAlertEntry> _criticalAlerts = ImmutableList<CriticalAlertEntry>.Empty;
    private ImmutableList<BannerInfoEntry> _infoBanners = ImmutableList<BannerInfoEntry>.Empty;
    private Func<Task>? _recoveryCallback;
    private object? _recoveryToken;

    private Exception? _unhandledError;

    public event Action? StateChanged;

    public IReadOnlyList<CriticalAlertEntry> CriticalAlerts
    {
        get { lock (_stateLock) { return _criticalAlerts; } }
    }

    public IReadOnlyList<BannerInfoEntry> InfoBanners
    {
        get { lock (_stateLock) { return _infoBanners; } }
    }

    public Exception? UnhandledError
    {
        get { lock (_stateLock) { return _unhandledError; } }
    }

    public void ClearError()
    {
        lock (_stateLock)
        {
            _unhandledError = null;
        }

        RaiseStateChanged();
    }

    public void DismissCritical(Guid id)
    {
        bool removed;

        lock (_stateLock)
        {
            ImmutableList<CriticalAlertEntry> next = _criticalAlerts.RemoveAll(entry => entry.Id == id);
            removed = next.Count != _criticalAlerts.Count;
            _criticalAlerts = next;
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

    public void ReportCritical(string title, string message)
    {
        var entry = new CriticalAlertEntry(Guid.NewGuid(), title, message, DateTime.UtcNow);

        lock (_stateLock)
        {
            _criticalAlerts = _criticalAlerts.Add(entry);
        }

        RaiseStateChanged();
    }

    public void ReportError(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        lock (_stateLock)
        {
            _unhandledError = ex;
        }

        RaiseStateChanged();
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
        Exception? snapshotError;
        Func<Task>? callback;

        lock (_stateLock)
        {
            snapshotError = _unhandledError;
            callback = _recoveryCallback;
        }

        if (callback is not null)
        {
            await callback();
        }

        bool cleared = false;

        lock (_stateLock)
        {
            // Only clear if the error is still the one we set out to recover. If a newer error was reported
            // while the callback was running, leave it visible so the user sees the new state.
            if (ReferenceEquals(_unhandledError, snapshotError))
            {
                _unhandledError = null;
                cleared = true;
            }
        }

        if (cleared)
        {
            RaiseStateChanged();
        }
    }

    private void RaiseStateChanged() => StateChanged?.Invoke();

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
