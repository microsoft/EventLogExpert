// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Modal;
using Fluxor.Blazor.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Modal;

/// <summary>
///     Base for modals shown via <see cref="IModalService" />. Owns the per-show id handshake with the service and
///     provides Complete/Cancel helpers that close the dialog before completing the task.
/// </summary>
public abstract class ModalBase<TResult> : FluxorComponent, IInlineAlertHost
{
    private readonly Lock _inlineAlertLock = new();

    private InlineAlertEntry? _activeInlineAlert;
    private ModalId _modalId;

    [Inject] internal IModalCoordinator ModalCoordinator { get; init; } = null!;

    [Inject] internal IModalService ModalService { get; init; } = null!;

    /// <summary>Bound by concrete modals via <c>@ref</c> on their <see cref="ModalChrome" />.</summary>
    protected ModalChrome? ChromeRef { get; set; }

    protected InlineAlertRequest? CurrentInlineAlert => _activeInlineAlert?.Request;

    public Task CloseAsync() => CompleteAsync(default);

    /// <inheritdoc />
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

        // Replace semantics: a stacked alert cancels any prior one.
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

    protected async Task CompleteAsync(TResult? result)
    {
        await OnClosingAsync();

        if (ChromeRef is not null)
        {
            await ChromeRef.CloseAsync();
        }

        ModalService.Complete(_modalId, result);
    }

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            // Cancel pending inline alert so background callers don't hang on a torn-down modal.
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

            ModalCoordinator.UnregisterInlineAlertHost(_modalId);

            // Defensive: complete the task if we were torn down without an explicit close path.
            // Stale ids are ignored, so this is a no-op when Complete already ran.
            ModalService.Complete(_modalId, default(TResult));
        }

        await base.DisposeAsyncCore(disposing);
    }

    // Route Esc/native-close through OnCancelAsync so all close paths share the same pipeline.
    // Protected so derived modals' .razor markup can wire OnDialogClosedByUser="HandleDialogClosedByUserAsync"
    // even when the derived class lives in a different assembly than ModalBase.
    protected Task HandleDialogClosedByUserAsync() => OnCancelAsync();

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

    /// <summary>Cleanup hook invoked on every close path. Override for side-effects.</summary>
    protected virtual Task OnClosingAsync() => Task.CompletedTask;

    protected virtual Task OnExportAsync() => Task.CompletedTask;

    protected virtual Task OnImportAsync() => Task.CompletedTask;

    protected override void OnInitialized()
    {
        // Capture the active id so a stale modal can never complete a successor's task.
        _modalId = ModalService.ActiveModalId;
        ModalCoordinator.RegisterInlineAlertHost(_modalId, this);
        base.OnInitialized();
    }

    protected virtual Task OnSaveAsync() => CompleteAsync(default);

    // Sync dispose path for the cancellation callback (BCL Action delegate forces sync — see ShowInlineAlertAsync.Register).
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

        _ = InvokeAsync(StateHasChanged);
    }

    private sealed class InlineAlertEntry(InlineAlertRequest request, TaskCompletionSource<InlineAlertResult> tcs)
    {
        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public InlineAlertRequest Request { get; } = request;

        public TaskCompletionSource<InlineAlertResult> Tcs { get; } = tcs;
    }
}
