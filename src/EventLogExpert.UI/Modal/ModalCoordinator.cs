// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Alerts;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.UI.Modal;

internal sealed class ModalCoordinator : IModalCoordinator, IDisposable
{
    private readonly IModalService _modalService;
    private readonly SemaphoreSlim _pushSemaphore = new(1, 1);
    private readonly Lock _stateLock = new();

    private ModalRegistration? _activeRegistration;
    private ModalSession? _activeSession;
    private bool _disposed;
    private TaskCompletionSource<bool>? _inFlightCloseTcs;

    public ModalCoordinator(IModalService modalService)
    {
        ArgumentNullException.ThrowIfNull(modalService);

        _modalService = modalService;
        _modalService.StateChanged += OnModalServiceStateChanged;
        OnModalServiceStateChanged();
    }

    public event Action? StateChanged;

    public ModalSession? ActiveSession
    {
        get { lock (_stateLock) { return _activeSession; } }
    }

    public void Complete<TResult>(ModalId modalId, TResult? result) =>
        _modalService.Complete(modalId, result);

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;
        _modalService.StateChanged -= OnModalServiceStateChanged;
        _pushSemaphore.Dispose();
    }

    public void ForceCloseActive() => _modalService.CancelActive();

    public ModalScope? GetActiveModalScope()
    {
        lock (_stateLock) { return GetActiveRegistration()?.Scope; }
    }

    public async Task<ModalOpenResult<TResult>> PushAsync<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : IComponent
    {
        Task<TResult?> showTask;

        await _pushSemaphore.WaitAsync();

        try
        {
            if (_modalService.ActiveModalId != ModalId.None)
            {
                bool accepted = await RequestCloseActiveAsync(ModalCloseReason.OtherModalActivation);

                if (!accepted) { return new ModalOpenResult<TResult>(default, WasOpened: false); }
            }

            showTask = _modalService.Show<TModal, TResult>(parameters);
        }
        finally
        {
            _pushSemaphore.Release();
        }

        TResult? result = await showTask;
        return new ModalOpenResult<TResult>(result, WasOpened: true);
    }

    public void RegisterModal(ModalRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        lock (_stateLock)
        {
            if (registration.ModalId.IsNone || registration.ModalId != _modalService.ActiveModalId) { return; }

            _activeRegistration = registration;
        }
    }

    public async Task<bool> RequestCloseActiveAsync(ModalCloseReason reason)
    {
        ModalRegistration? snapshot = null;
        TaskCompletionSource<bool>? newTcs = null;
        Task<bool>? inFlight = null;

        lock (_stateLock)
        {
            ModalRegistration? activeRegistration = GetActiveRegistration();

            if (activeRegistration is not null
                && activeRegistration.Scope == ModalScope.Critical
                && reason == ModalCloseReason.OtherModalActivation)
            {
                return false;
            }

            if (_inFlightCloseTcs is not null)
            {
                inFlight = _inFlightCloseTcs.Task;
            }
            else if (activeRegistration is null)
            {
                return reason != ModalCloseReason.OtherModalActivation || _modalService.ActiveModalId == ModalId.None;
            }
            else
            {
                snapshot = activeRegistration;
                newTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlightCloseTcs = newTcs;
            }
        }

        if (inFlight is not null) { return await inFlight; }

        try
        {
            bool accepted = await snapshot!.RequestClose(new ModalCloseRequest(reason));
            newTcs!.TrySetResult(accepted);
            return accepted;
        }
        catch (OperationCanceledException)
        {
            // Modal is gone via ForceCloseActive race; treat as accepted so coalesced callers can proceed.
            newTcs!.TrySetResult(true);
            return true;
        }
        catch (Exception ex)
        {
            newTcs!.TrySetException(ex);
            throw;
        }
        finally
        {
            lock (_stateLock) { _inFlightCloseTcs = null; }
        }
    }

    public bool TryGetInlineAlertHost([NotNullWhen(true)] out IInlineAlertHost? host)
    {
        lock (_stateLock)
        {
            host = GetActiveRegistration()?.InlineAlertHost;

            return host is not null;
        }
    }

    public void UnregisterModal(ModalId modalId)
    {
        lock (_stateLock)
        {
            if (_activeRegistration?.ModalId == modalId)
            {
                _activeRegistration = null;
            }
        }
    }

    private ModalRegistration? GetActiveRegistration()
    {
        if (_activeRegistration is null) { return null; }

        if (_activeRegistration.ModalId == _modalService.ActiveModalId)
        {
            return _activeRegistration;
        }

        _activeRegistration = null;

        return null;
    }

    private void OnModalServiceStateChanged()
    {
        bool changed;

        lock (_stateLock)
        {
            ModalId id = _modalService.ActiveModalId;
            Type? type = _modalService.ActiveModalType;
            IDictionary<string, object?>? parameters = _modalService.ActiveModalParameters;

            if (_modalService.ActiveModalId != id) { return; }

            ModalSession? newSession = type is null ? null : new ModalSession(id, type, parameters);
            changed = !Equals(_activeSession, newSession);
            _activeSession = newSession;

            if (_activeRegistration is not null && _activeRegistration.ModalId != id)
            {
                _activeRegistration = null;
            }
        }

        if (changed) { StateChanged?.Invoke(); }
    }
}

