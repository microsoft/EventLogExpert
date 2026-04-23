// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Base;

/// <summary>
/// Compositional wrapper component that renders the shared <c>&lt;dialog&gt;</c> chrome (header,
/// body, footer, close button) for a modal. Owned by parent <see cref="ModalBase{TResult}"/>
/// instances which bind it via <c>@ref</c>.
/// </summary>
/// <remarks>
/// Accessibility:
/// <list type="bullet">
/// <item><c>aria-modal="true"</c> on the dialog (explicit even though <c>showModal()</c> implies it).</item>
/// <item><c>aria-labelledby</c> points to the visible title heading when <see cref="Title"/> is set;
/// <c>aria-label</c> falls back to <see cref="AriaLabel"/> for modals without a visible title so
/// screen readers still announce a meaningful name.</item>
/// <item>Close X button is keyboard reachable with <c>aria-label="Close"</c>.</item>
/// <item>Native <c>&lt;dialog&gt;.showModal()</c> provides focus trap, Esc-to-close, and focus
/// restoration on close.</item>
/// <item>Esc and other native cancel routes are intercepted via <c>@oncancel</c> so the parent
/// modal can run its <see cref="ModalBase{TResult}.HandleDialogClosedByUserAsync"/> path and
/// complete the awaiting task. We <c>preventDefault</c> on <c>cancel</c> and explicitly close
/// from C# to keep ordering deterministic.</item>
/// </list>
/// </remarks>
public sealed partial class ModalChrome : ComponentBase, IAsyncDisposable
{
    private readonly string _inlineAlertMessageId = $"modal-inline-alert-message-{Guid.NewGuid():N}";
    private readonly string _inlineAlertTitleId = $"modal-inline-alert-title-{Guid.NewGuid():N}";
    private readonly string _titleId = $"modal-title-{Guid.NewGuid():N}";

    private ElementReference _dialogRef;
    private ElementReference _inlineAlertAcceptButtonRef;
    private ElementReference _inlineAlertCancelButtonRef;
    private ElementReference _inlineAlertInputRef;
    private string _inlineAlertPromptValue = string.Empty;
    private bool _isClosed;
    private InlineAlertRequest? _previouslyRenderedInlineAlert;

    [Parameter] public string AcceptLabel { get; set; } = "OK";

    /// <summary>Fallback accessible name for modals without a visible title (e.g., the migrated
    /// Settings/DebugLog/etc. modals). Ignored when <see cref="Title"/> is set.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string CancelLabel { get; set; } = "Cancel";

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string CloseLabel { get; set; } = "Close";

    [Parameter] public string? DialogClass { get; set; }

    [Parameter] public string ExportLabel { get; set; } = "Export";

    [Parameter] public RenderFragment? ExtraFooterContent { get; set; }

    [Parameter] public FooterPreset Footer { get; set; } = FooterPreset.CloseOnly;

    [Parameter] public string ImportLabel { get; set; } = "Import";

    /// <summary>Active inline-alert request to render as a banner above the modal body. When set,
    /// the chrome's footer and body are rendered as <c>inert</c> + buttons disabled, and Esc
    /// dismisses the alert (cancel) instead of the host modal.</summary>
    [Parameter] public InlineAlertRequest? InlineAlert { get; set; }

    [Parameter] public EventCallback OnAccept { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Invoked when the dialog is closed by the user (Esc or native close), as opposed to
    /// a footer button click. Parent <see cref="ModalBase{TResult}"/> wires this to its
    /// <see cref="ModalBase{TResult}.HandleDialogClosedByUserAsync"/>.</summary>
    [Parameter] public EventCallback OnDialogClosedByUser { get; set; }

    [Parameter] public EventCallback OnExport { get; set; }

    [Parameter] public EventCallback OnImport { get; set; }

    /// <summary>Invoked when the user resolves the active <see cref="InlineAlert" /> by clicking
    /// the accept or cancel button or pressing Esc. Parent <see cref="ModalBase{TResult}"/> wires
    /// this to its <see cref="ModalBase{TResult}.HandleInlineAlertResolvedAsync" />.</summary>
    [Parameter] public EventCallback<InlineAlertResult> OnInlineAlertResolved { get; set; }

    [Parameter] public EventCallback OnSave { get; set; }

    [Parameter] public string SaveLabel { get; set; } = "Save";

    [Parameter] public bool ShowCloseButton { get; set; }

    [Parameter] public string? Title { get; set; }

    private bool HasInlineAlert => InlineAlert is not null;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    /// <summary>Imperatively close the dialog. Idempotent and best-effort.</summary>
    public async Task CloseAsync()
    {
        if (_isClosed) { return; }

        _isClosed = true;

        try
        {
            await JSRuntime.InvokeVoidAsync("closeModal", _dialogRef);
        }
        catch
        {
            // Best-effort during teardown; if the JS runtime is gone or the element is detached
            // there is nothing meaningful we can do.
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Best-effort: ensure the dialog is closed if the component is being torn down (e.g.,
        // because the host swapped to a different active modal type).
        await CloseAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("showModal", _dialogRef);
            }
            catch
            {
                // Best-effort; if showModal fails we'll still be a hidden inline element rather than
                // a modal, but throwing here would tear down the host.
            }
        }

        // When an inline alert appears, move focus into it. We compare references against the
        // last-rendered alert to avoid re-focusing on every parent re-render.
        if (!ReferenceEquals(_previouslyRenderedInlineAlert, InlineAlert))
        {
            _previouslyRenderedInlineAlert = InlineAlert;

            if (InlineAlert is not null)
            {
                _inlineAlertPromptValue = InlineAlert.IsPrompt ? InlineAlert.PromptInitialValue ?? string.Empty : string.Empty;

                try
                {
                    if (InlineAlert.IsPrompt)
                    {
                        await _inlineAlertInputRef.FocusAsync(preventScroll: true);
                    }
                    else if (!string.IsNullOrEmpty(InlineAlert.AcceptLabel))
                    {
                        await _inlineAlertAcceptButtonRef.FocusAsync(preventScroll: true);
                    }
                    else
                    {
                        await _inlineAlertCancelButtonRef.FocusAsync(preventScroll: true);
                    }
                }
                catch
                {
                    // Best-effort: if the element is no longer in the DOM (alert was canceled
                    // during the same render cycle) ignore the focus failure.
                }
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private Task HandleAcceptAsync() => OnAccept.InvokeAsync();

    private Task HandleCancelButtonAsync() => OnCancel.InvokeAsync();

    private async Task HandleCancelEventAsync()
    {
        // <dialog> 'cancel' fires for Esc. We preventDefault in markup so the browser does NOT
        // close the dialog automatically; instead the parent's HandleDialogClosedByUserAsync runs
        // OnClosingAsync and then ModalService.Complete. The actual <dialog>.close() happens later
        // when the host re-renders without this modal (DynamicComponent disposal). This guard
        // simply prevents double-invoking the callback if a stray 'cancel' arrives after we've
        // already closed (e.g., disposal race).
        if (_isClosed) { return; }

        // While an inline alert is active, Esc dismisses the alert (cancel) instead of the host
        // modal. This matches the user's expectation that the alert "intercepts" interaction.
        if (HasInlineAlert)
        {
            await HandleInlineAlertCancelAsync();
            return;
        }

        await OnDialogClosedByUser.InvokeAsync();
    }

    private Task HandleCloseButtonAsync() => OnClose.InvokeAsync();

    private Task HandleCloseEventAsync()
    {
        // 'close' fires after close() runs (either ours via CloseAsync, or the browser's native
        // path if our preventDefault on cancel ever fails). Treat as already-closed bookkeeping.
        _isClosed = true;
        return Task.CompletedTask;
    }

    private Task HandleExportAsync() => OnExport.InvokeAsync();

    private Task HandleImportAsync() => OnImport.InvokeAsync();

    private async Task HandleInlineAlertAcceptAsync()
    {
        if (InlineAlert is null) { return; }

        string? promptValue = InlineAlert.IsPrompt ? _inlineAlertPromptValue : null;
        await OnInlineAlertResolved.InvokeAsync(new InlineAlertResult(true, promptValue));
    }

    private Task HandleInlineAlertCancelAsync() =>
        OnInlineAlertResolved.InvokeAsync(new InlineAlertResult(false, null));

    private Task HandleSaveAsync() => OnSave.InvokeAsync();
}
