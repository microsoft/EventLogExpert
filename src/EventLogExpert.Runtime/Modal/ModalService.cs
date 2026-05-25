// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Runtime.Modal;

internal sealed class ModalService : IModalService
{
    private readonly Lock _stateLock = new();

    private long _activeModalId;
    private object? _activeTcs;
    private Action? _cancelActiveDelegate;
    private long _idCounter;

    public event Action? StateChanged;

    public ModalId ActiveModalId => new(Volatile.Read(ref _activeModalId));

    public IDictionary<string, object?>? ActiveModalParameters { get; private set; }

    public Type? ActiveModalType { get; private set; }

    public void CancelActive()
    {
        Action? cancelDelegate = null;
        bool stateChanged = false;

        lock (_stateLock)
        {
            if (_cancelActiveDelegate is null) { return; }

            cancelDelegate = _cancelActiveDelegate;
            ClearStateLocked();
            stateChanged = true;
        }

        try
        {
            if (stateChanged) { StateChanged?.Invoke(); }
        }
        finally
        {
            cancelDelegate?.Invoke();
        }
    }

    public void Complete<TResult>(ModalId modalId, TResult? result)
    {
        TaskCompletionSource<TResult?>? tcsToComplete = null;
        bool stateChanged = false;

        lock (_stateLock)
        {
            if (modalId.Value != _activeModalId) { return; }

            if (_activeTcs is not TaskCompletionSource<TResult?> tcs)
            {
                // TResult mismatch: caller used the wrong generic. Must be a no-op so the real
                // awaiter (with the correct type) can still complete.
                return;
            }

            tcsToComplete = tcs;
            ClearStateLocked();
            stateChanged = true;
        }

        try
        {
            if (stateChanged) { StateChanged?.Invoke(); }
        }
        finally
        {
            tcsToComplete?.TrySetResult(result);
        }
    }

    public Task<TResult?> Show<TModal, TResult>(IDictionary<string, object?>? parameters = null)
        where TModal : IComponent
    {
        TaskCompletionSource<TResult?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Action? priorCancelDelegate;

        lock (_stateLock)
        {
            priorCancelDelegate = _cancelActiveDelegate;

            _idCounter++;
            ActiveModalType = typeof(TModal);
            ActiveModalParameters = parameters;
            _activeTcs = tcs;
            _cancelActiveDelegate = () => tcs.TrySetResult(default);
            // Publish id LAST: writes preceding a Volatile.Write aren't reordered past it, so
            // cross-thread readers that observe the new id are guaranteed to see the new state.
            Volatile.Write(ref _activeModalId, _idCounter);
        }

        try
        {
            StateChanged?.Invoke();
        }
        finally
        {
            priorCancelDelegate?.Invoke();
        }

        return tcs.Task;
    }

    private void ClearStateLocked()
    {
        ActiveModalType = null;
        ActiveModalParameters = null;
        _activeTcs = null;
        _cancelActiveDelegate = null;
        // Publish sentinel id LAST so readers observing id=0 are also guaranteed to see the
        // cleared type/parameters. Matches the Show() publication-barrier discipline.
        Volatile.Write(ref _activeModalId, 0);
    }
}
