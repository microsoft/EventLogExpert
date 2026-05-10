// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Services;

public sealed class ModalService : IModalService
{
    private readonly Lock _stateLock = new();

    private long _activeModalId;
    private object? _activeTcs;
    private Action? _cancelActiveDelegate;
    private long _idCounter;

    public event Action? StateChanged;

    public long ActiveModalId => Volatile.Read(ref _activeModalId);

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
            if (modalId != _activeModalId) { return; }

            if (_activeTcs is not TaskCompletionSource<TResult?> tcs)
            {
                // TResult mismatch: caller used the wrong generic. Must be a no-op so the real
                // awaiter (with the correct type) can still complete.
                return;
            }

            tcs.TrySetResult(result);
            ClearStateLocked();
            stateChanged = true;
        }

        if (stateChanged) { StateChanged?.Invoke(); }
    }

    public Task<TResult?> Show<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : IComponent
    {
        TaskCompletionSource<TResult?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_stateLock)
        {
            _cancelActiveDelegate?.Invoke();

            _idCounter++;
            Volatile.Write(ref _activeModalId, _idCounter);
            ActiveModalType = typeof(TModal);
            ActiveModalParameters = parameters;
            _activeTcs = tcs;
            _cancelActiveDelegate = () => tcs.TrySetResult(default);
        }

        StateChanged?.Invoke();
        return tcs.Task;
    }

    private void ClearStateLocked()
    {
        // Sentinel id 0 < any id issued by Show() (idCounter is pre-incremented), so any stale
        // modalId fails the equality check in Complete after this clear. The same id change makes
        // the inline-alert host broker lazily invalidate any host registered against the prior id.
        Volatile.Write(ref _activeModalId, 0);
        ActiveModalType = null;
        ActiveModalParameters = null;
        _activeTcs = null;
        _cancelActiveDelegate = null;
    }
}
