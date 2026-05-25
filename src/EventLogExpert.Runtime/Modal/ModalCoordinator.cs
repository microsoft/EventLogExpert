// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.Modal;

internal sealed class ModalCoordinator : IModalCoordinator, IDisposable
{
    private readonly IModalService _modalService;
    private readonly Lock _stateLock = new();

    private ModalSession? _activeSession;
    private bool _disposed;
    private IInlineAlertHost? _host;
    private ModalId _hostModalId;

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
    }

    public void ForceCloseActive() => _modalService.CancelActive();

    public Task<TResult?> PushAsync<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : IComponent
        => _modalService.Show<TModal, TResult>(parameters);

    public void RegisterInlineAlertHost(ModalId modalId, IInlineAlertHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        lock (_stateLock)
        {
            if (modalId != _modalService.ActiveModalId) { return; }

            _hostModalId = modalId;
            _host = host;
        }
    }

    public bool TryGetInlineAlertHost([NotNullWhen(true)] out IInlineAlertHost? host)
    {
        lock (_stateLock)
        {
            if (_hostModalId != _modalService.ActiveModalId)
            {
                _host = null;
                _hostModalId = ModalId.None;
            }

            host = _host;
            return host is not null;
        }
    }

    public void UnregisterInlineAlertHost(ModalId modalId)
    {
        lock (_stateLock)
        {
            if (_hostModalId != modalId) { return; }

            _host = null;
            _hostModalId = ModalId.None;
        }
    }

    private void OnModalServiceStateChanged()
    {
        bool changed;

        lock (_stateLock)
        {
            ModalId id = _modalService.ActiveModalId;
            Type? type = _modalService.ActiveModalType;
            IDictionary<string, object?>? parameters = _modalService.ActiveModalParameters;

            // Re-read id after type/parameters reads; bail if a concurrent mutation could have torn the snapshot.
            if (_modalService.ActiveModalId != id) { return; }

            ModalSession? newSession = type is null ? null : new ModalSession(id, type, parameters);
            changed = !Equals(_activeSession, newSession);
            _activeSession = newSession;

            if (_hostModalId != id)
            {
                _host = null;
                _hostModalId = ModalId.None;
            }
        }

        if (changed) { StateChanged?.Invoke(); }
    }
}

