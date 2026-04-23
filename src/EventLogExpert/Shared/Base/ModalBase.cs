// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;
using Fluxor.Blazor.Web.Components;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Base;

/// <summary>
///     Base for modals shown via <see cref="IModalService" />. Owns the per-show id handshake with the service and
///     provides Complete/Cancel helpers that close the dialog before completing the task.
/// </summary>
public abstract class ModalBase<TResult> : FluxorComponent, IInlineAlertHost
{
    private readonly Lock _inlineAlertLock = new();

    private InlineAlertEntry? _activeInlineAlert;
    private long _modalId;

    [Inject] internal IModalService ModalService { get; init; } = null!;

    /// <summary>Bound by concrete modals via <c>@ref</c> on their <see cref="ModalChrome" />.</summary>
    protected ModalChrome? ChromeRef { get; set; }

    protected InlineAlertRequest? CurrentInlineAlert => _activeInlineAlert?.Request;

    public Task CloseAsync() => CompleteAsync(default);

    /// <inheritdoc />
    public Task<InlineAlertResult> ShowInlineAlertAsync(InlineAlertRequest request, CancellationToken cancellationToken)
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
        prior?.CancellationRegistration.Dispose();
        prior?.Tcs.TrySetCanceled();

        // Marshal to dispatcher; callers may be on a background thread.
        _ = InvokeAsync(StateHasChanged);

        if (!cancellationToken.CanBeCanceled)
        {
            return tcs.Task;
        }

        CancellationTokenRegistration registration = cancellationToken.Register(static state =>
            {
                var (host, e) = ((ModalBase<TResult> Host, InlineAlertEntry Entry))state!;
                host.TryClearInlineAlert(e, null, true);
            },
            (this, entry));

        entry.CancellationRegistration = registration;

        return tcs.Task;
    }

    // Route Esc/native-close through OnCancelAsync so all close paths share the same pipeline.
    internal Task HandleDialogClosedByUserAsync() => OnCancelAsync();

    internal Task HandleInlineAlertResolvedAsync(InlineAlertResult result)
    {
        TryClearInlineAlert(null, result, false);
        return Task.CompletedTask;
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

            pending?.CancellationRegistration.Dispose();
            pending?.Tcs.TrySetCanceled();

            ModalService.UnregisterActiveAlertHost(_modalId);

            // Defensive: complete the task if we were torn down without an explicit close path.
            // Stale ids are ignored, so this is a no-op when Complete already ran.
            ModalService.Complete(_modalId, default(TResult));
        }

        await base.DisposeAsyncCore(disposing);
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
        ModalService.RegisterActiveAlertHost(_modalId, this);
        base.OnInitialized();
    }

    protected virtual Task OnSaveAsync() => CompleteAsync(default);

    // When `expected` is non-null, only clears if it matches the active entry — guards against
    // a cancellation callback clobbering a successor alert.
    private void TryClearInlineAlert(InlineAlertEntry? expected, InlineAlertResult? result, bool cancel)
    {
        InlineAlertEntry? cleared;

        lock (_inlineAlertLock)
        {
            if (_activeInlineAlert is null) { return; }

            if (expected is not null && !ReferenceEquals(_activeInlineAlert, expected)) { return; }

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
