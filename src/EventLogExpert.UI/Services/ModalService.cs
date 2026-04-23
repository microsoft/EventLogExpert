// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Services;

public sealed class ModalService : IModalService
{
    private readonly Lock _stateLock = new();
    private IInlineAlertHost? _activeAlertHost;
    private long _activeAlertHostId;
    private object? _activeTcs;
    private Action? _cancelActiveDelegate;
    private long _idCounter;

    public event Action? StateChanged;

    public long ActiveModalId { get; private set; }

    public IDictionary<string, object?>? ActiveModalParameters { get; private set; }

    public Type? ActiveModalType { get; private set; }

    public void CancelActive()
    {
        bool stateChanged = false;

        lock (_stateLock)
        {
            if (_cancelActiveDelegate is null) { return; }

            _cancelActiveDelegate.Invoke();
            ClearStateLocked();
            stateChanged = true;
        }

        if (stateChanged) { StateChanged?.Invoke(); }
    }

    public void Complete<TResult>(long modalId, TResult? result)
    {
        bool stateChanged = false;

        lock (_stateLock)
        {
            if (modalId != ActiveModalId) { return; }

            if (_activeTcs is not TaskCompletionSource<TResult?> tcs)
            {
                // TResult mismatch: a caller used the wrong generic argument. Do NOT clear state
                // here — that would strand the real awaiter. Treat as a programming-error no-op.
                return;
            }

            tcs.TrySetResult(result);
            ClearStateLocked();
            stateChanged = true;
        }

        if (stateChanged) { StateChanged?.Invoke(); }
    }

    public void RegisterActiveAlertHost(long modalId, IInlineAlertHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        lock (_stateLock)
        {
            // Only the currently active modal can register. Late registrations from a stale modal
            // would route alerts to a torn-down host.
            if (modalId != ActiveModalId) { return; }

            _activeAlertHost = host;
            _activeAlertHostId = modalId;
        }
    }

    public Task<TResult?> Show<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : IComponent
    {
        TaskCompletionSource<TResult?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_stateLock)
        {
            // Cancel the previously active modal (if any) before swapping.
            _cancelActiveDelegate?.Invoke();

            _idCounter++;
            ActiveModalId = _idCounter;
            ActiveModalType = typeof(TModal);
            ActiveModalParameters = parameters;
            _activeTcs = tcs;
            _cancelActiveDelegate = () => tcs.TrySetResult(default);

            // Clear any host registration left over from the previous active modal. The new modal
            // will re-register itself in OnInitialized.
            _activeAlertHost = null;
            _activeAlertHostId = 0;
        }

        StateChanged?.Invoke();
        return tcs.Task;
    }

    public bool TryGetActiveAlertHost(out IInlineAlertHost? host)
    {
        lock (_stateLock)
        {
            host = _activeAlertHost;
            return host is not null;
        }
    }

    public void UnregisterActiveAlertHost(long modalId)
    {
        lock (_stateLock)
        {
            if (modalId != _activeAlertHostId) { return; }

            _activeAlertHost = null;
            _activeAlertHostId = 0;
        }
    }

    private void ClearStateLocked()
    {
        ActiveModalType = null;
        ActiveModalParameters = null;
        _activeTcs = null;
        _cancelActiveDelegate = null;
        _activeAlertHost = null;
        _activeAlertHostId = 0;
    }
}
