// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Alerts;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Modal;

public abstract class ModalBase<TResult> : ComponentBase, IInlineAlertHost, IAsyncDisposable
{
    private readonly Lock _inlineAlertLock = new();

    private InlineAlertEntry? _activeInlineAlert;
    private volatile bool _isDisposed;
    private ModalId _modalId;

    [Inject] internal IModalCoordinator ModalCoordinator { get; init; } = null!;

    [Inject] internal IModalService ModalService { get; init; } = null!;

    protected ModalChrome? ChromeRef { get; set; }

    protected InlineAlertRequest? CurrentInlineAlert => _activeInlineAlert?.Request;

    protected bool IsDisposed => _isDisposed;

    protected virtual ModalScope Scope => ModalScope.Standard;

    public Task CloseAsync() => CompleteAsync(default);

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) { return; }

        _isDisposed = true;
        await DisposeAsyncCore(true);
        GC.SuppressFinalize(this);
    }

    public async Task<InlineAlertResult> ShowInlineAlertAsync(InlineAlertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TaskCompletionSource<InlineAlertResult> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        InlineAlertEntry entry = new(request, tcs);
        InlineAlertEntry? prior;

        lock (_inlineAlertLock)
        {
            prior = _activeInlineAlert;
            _activeInlineAlert = entry;
        }

        if (prior is not null)
        {
            await prior.CancellationRegistration.DisposeAsync();
            prior.Tcs.TrySetCanceled();
        }

        await InvokeAsync(StateHasChanged);

        if (cancellationToken.CanBeCanceled)
        {
            CancellationTokenRegistration registration = cancellationToken.Register(static state =>
                {
                    var (host, e) = ((ModalBase<TResult> Host, InlineAlertEntry Entry))state!;
                    host.TryClearInlineAlertFromCallback(e, null, true);
                },
                (this, entry));

            entry.CancellationRegistration = registration;
        }

        return await tcs.Task;
    }

    internal async Task<bool> RequestCloseAsync(ModalCloseRequest request)
    {
        bool accepted = await OnRequestCloseAsync(request);

        if (accepted) { await OnCancelAsync(); }

        return accepted;
    }

    protected async Task CompleteAsync(TResult? result)
    {
        await OnClosingAsync();

        if (ChromeRef is not null)
        {
            await ChromeRef.CloseAsync();
        }

        ModalService.Complete(_modalId, result);
    }

    protected virtual async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            InlineAlertEntry? pending;

            lock (_inlineAlertLock)
            {
                pending = _activeInlineAlert;
                _activeInlineAlert = null;
            }

            if (pending is not null)
            {
                await pending.CancellationRegistration.DisposeAsync();
                pending.Tcs.TrySetCanceled();
            }

            ModalCoordinator.UnregisterModal(_modalId);

            ModalService.Complete(_modalId, default(TResult));
        }
    }

    protected Task HandleCancelButtonClickAsync() =>
        ModalCoordinator.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

    protected Task HandleDialogClosedByUserAsync() =>
        ModalCoordinator.RequestCloseActiveAsync(ModalCloseReason.UserDismiss);

    protected async Task HandleInlineAlertResolvedAsync(InlineAlertResult result)
    {
        InlineAlertEntry? cleared;

        lock (_inlineAlertLock)
        {
            cleared = _activeInlineAlert;
            _activeInlineAlert = null;
        }

        if (cleared is null) { return; }

        await cleared.CancellationRegistration.DisposeAsync();
        cleared.Tcs.TrySetResult(result);
        await InvokeAsync(StateHasChanged);
    }

    protected virtual Task OnAcceptAsync() => CompleteAsync(default);

    protected virtual Task OnCancelAsync() => CompleteAsync(default);

    protected virtual Task OnClosingAsync() => Task.CompletedTask;

    protected virtual Task OnExportAsync() => Task.CompletedTask;

    protected virtual Task OnImportAsync() => Task.CompletedTask;

    protected override void OnInitialized()
    {
        _modalId = ModalService.ActiveModalId;
        var registration = new ModalRegistration(_modalId, RequestCloseAsync, Scope, this);
        ModalCoordinator.RegisterModal(registration);
        base.OnInitialized();
    }

    protected virtual Task<bool> OnRequestCloseAsync(ModalCloseRequest request) => Task.FromResult(true);

    protected virtual Task OnSaveAsync() => CompleteAsync(default);

    private async Task DispatchGuardedRenderAsync()
    {
        try
        {
            await InvokeAsync(() =>
            {
                if (!IsDisposed) { StateHasChanged(); }
            });
        }
        catch (ObjectDisposedException) { /* Renderer torn down between the guard and the dispatch. */ }
        catch (OperationCanceledException) { /* Circuit shutting down. */ }
    }

    private void TryClearInlineAlertFromCallback(InlineAlertEntry expected, InlineAlertResult? result, bool cancel)
    {
        InlineAlertEntry? cleared;

        lock (_inlineAlertLock)
        {
            if (_activeInlineAlert is null) { return; }

            if (!ReferenceEquals(_activeInlineAlert, expected)) { return; }

            cleared = _activeInlineAlert;
            _activeInlineAlert = null;
        }

        cleared.CancellationRegistration.Dispose();

        if (cancel)
        {
            cleared.Tcs.TrySetCanceled();
        }
        else
        {
            cleared.Tcs.TrySetResult(result ?? new InlineAlertResult(false, null));
        }

        if (IsDisposed) { return; }

        _ = DispatchGuardedRenderAsync();
    }

    private sealed class InlineAlertEntry(InlineAlertRequest request, TaskCompletionSource<InlineAlertResult> tcs)
    {
        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public InlineAlertRequest Request { get; } = request;

        public TaskCompletionSource<InlineAlertResult> Tcs { get; } = tcs;
    }
}
