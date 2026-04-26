// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Shared.Base;

/// <summary>
///     Renders the shared <c>&lt;dialog&gt;</c> chrome (header, body, footer) for a modal. Owned by
///     <see cref="ModalBase{TResult}" /> via <c>@ref</c>. Esc/native cancel is intercepted with
///     <c>@oncancel:preventDefault</c> so close ordering is driven from C#.
/// </summary>
public sealed partial class ModalChrome : ComponentBase, IAsyncDisposable
{
    private readonly string _inlineAlertMessageId = $"modal-inline-alert-message-{Guid.NewGuid():N}";
    private readonly string _inlineAlertTitleId = $"modal-inline-alert-title-{Guid.NewGuid():N}";
    private readonly string _titleId = $"modal-title-{Guid.NewGuid():N}";

    private ElementReference _dialogRef;
    private ElementReference _inlineAlertAcceptButtonRef;
    private ElementReference _inlineAlertCancelButtonRef;
    private InlineAlertRequest? _inlineAlertInitializedFor;
    private ElementReference _inlineAlertInputRef;
    private string _inlineAlertPromptValue = string.Empty;
    private bool _isClosed;
    private bool _isClosingByCancel;
    private InlineAlertRequest? _previouslyRenderedInlineAlert;

    [Parameter] public string AcceptLabel { get; set; } = "OK";

    /// <summary>Fallback accessible name when <see cref="Title" /> is not set.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string CancelLabel { get; set; } = "Cancel";

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Accessible name for the header (X) close button. Independent of <see cref="CloseLabel" /> so
    /// localized footer labels (e.g. "Exit") don't bleed into the icon button's announcement.</summary>
    [Parameter] public string CloseButtonAriaLabel { get; set; } = "Close";

    [Parameter] public string CloseLabel { get; set; } = "Close";

    [Parameter] public string? DialogClass { get; set; }

    [Parameter] public string ExportLabel { get; set; } = "Export";

    [Parameter] public RenderFragment? ExtraFooterContent { get; set; }

    [Parameter] public FooterPreset Footer { get; set; } = FooterPreset.CloseOnly;

    /// <summary>Optional height (e.g. <c>"60%"</c>, <c>"40rem"</c>) exposed as <c>var(--modal-height)</c>.</summary>
    [Parameter] public string? Height { get; set; }

    [Parameter] public string ImportLabel { get; set; } = "Import";

    /// <summary>When set, renders as a banner over an inert body/footer and Esc dismisses the alert (not the host modal).</summary>
    [Parameter] public InlineAlertRequest? InlineAlert { get; set; }

    /// <summary>Optional max width exposed as <c>var(--modal-max-width)</c>.</summary>
    [Parameter] public string? MaxWidth { get; set; }

    /// <summary>Optional min width exposed as <c>var(--modal-min-width)</c>.</summary>
    [Parameter] public string? MinWidth { get; set; }

    [Parameter] public EventCallback OnAccept { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Invoked when the dialog is closed by Esc or native cancel (not a footer button).</summary>
    [Parameter] public EventCallback OnDialogClosedByUser { get; set; }

    [Parameter] public EventCallback OnExport { get; set; }

    [Parameter] public EventCallback OnImport { get; set; }

    /// <summary>Invoked when the user resolves the active <see cref="InlineAlert" />.</summary>
    [Parameter] public EventCallback<InlineAlertResult> OnInlineAlertResolved { get; set; }

    [Parameter] public EventCallback OnSave { get; set; }

    [Parameter] public string SaveLabel { get; set; } = "Save";

    [Parameter] public bool ShowCloseButton { get; set; }

    [Parameter] public string? Title { get; set; }

    private string? DialogInlineStyle
    {
        get
        {
            List<string>? parts = null;

            if (!string.IsNullOrEmpty(Height)) { (parts ??= []).Add($"--modal-height: {Height};"); }

            if (!string.IsNullOrEmpty(MinWidth)) { (parts ??= []).Add($"--modal-min-width: {MinWidth};"); }

            if (!string.IsNullOrEmpty(MaxWidth)) { (parts ??= []).Add($"--modal-max-width: {MaxWidth};"); }

            return parts is null ? null : string.Join(" ", parts);
        }
    }

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
            // Best-effort: JS runtime may be gone or element detached during teardown.
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync();

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
                // Best-effort: throwing here would tear down the host.
            }
        }

        // Move focus into a newly-appeared inline alert; reference-compare to avoid re-focusing.
        if (!ReferenceEquals(_previouslyRenderedInlineAlert, InlineAlert))
        {
            _previouslyRenderedInlineAlert = InlineAlert;

            if (InlineAlert is not null)
            {
                try
                {
                    if (InlineAlert.IsPrompt)
                    {
                        await _inlineAlertInputRef.FocusAsync(true);
                    }
                    else if (!string.IsNullOrEmpty(InlineAlert.AcceptLabel))
                    {
                        await _inlineAlertAcceptButtonRef.FocusAsync(true);
                    }
                    else
                    {
                        await _inlineAlertCancelButtonRef.FocusAsync(true);
                    }
                }
                catch
                {
                    // Best-effort: element may not be in the DOM if the alert was already canceled.
                }
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnParametersSet()
    {
        // Initialize prompt value before first render so the input paints with the correct value.
        if (!ReferenceEquals(_inlineAlertInitializedFor, InlineAlert))
        {
            _inlineAlertInitializedFor = InlineAlert;

            _inlineAlertPromptValue = InlineAlert is { IsPrompt: true }
                ? InlineAlert.PromptInitialValue ?? string.Empty
                : string.Empty;
        }

        base.OnParametersSet();
    }

    private Task HandleAcceptAsync() => OnAccept.InvokeAsync();

    private Task HandleCancelButtonAsync() => OnCancel.InvokeAsync();

    // Esc fires <dialog> 'cancel'. preventDefault keeps close ordering driven from C# so the
    // parent's OnClosingAsync runs before the dialog is actually closed. _isClosingByCancel
    // debounces repeated Esc presses while the user callback (which may surface an alert) runs.
    private async Task HandleCancelEventAsync()
    {
        if (_isClosed) { return; }

        // Inline alert intercepts Esc; the host modal stays open.
        if (HasInlineAlert)
        {
            await HandleInlineAlertCancelAsync();
            return;
        }

        if (_isClosingByCancel) { return; }

        _isClosingByCancel = true;

        try
        {
            await OnDialogClosedByUser.InvokeAsync();
        }
        finally
        {
            // Only keep the latch while actually closing; allow Esc again if the callback bailed
            // without closing (e.g. surfaced a confirmation alert).
            if (!_isClosed)
            {
                _isClosingByCancel = false;
            }
        }
    }

    private Task HandleCloseButtonAsync() => OnClose.InvokeAsync();

    private Task HandleCloseEventAsync()
    {
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
