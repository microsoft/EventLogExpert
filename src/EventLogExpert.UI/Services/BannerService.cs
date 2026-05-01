// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Services;

public sealed class BannerService : IBannerService
{
    private readonly Lock _stateLock = new();

    private Exception? _currentCritical;
    private ImmutableList<ErrorBannerEntry> _errorBanners = ImmutableList<ErrorBannerEntry>.Empty;
    private ImmutableList<BannerInfoEntry> _infoBanners = ImmutableList<BannerInfoEntry>.Empty;
    private Func<Task>? _recoveryCallback;
    private object? _recoveryToken;

    public event Action? StateChanged;

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
