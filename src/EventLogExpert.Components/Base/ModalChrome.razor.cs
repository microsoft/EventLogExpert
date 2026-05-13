// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Alerts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Components.Base;

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

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string CancelLabel { get; set; } = "Cancel";

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string CloseButtonAriaLabel { get; set; } = "Close";

    [Parameter] public string CloseLabel { get; set; } = "Close";

    [Parameter] public string? DialogClass { get; set; }

    [Parameter] public string ExportLabel { get; set; } = "Export";

    [Parameter] public RenderFragment? ExtraFooterContent { get; set; }

    [Parameter] public FooterPreset Footer { get; set; } = FooterPreset.CloseOnly;

    [Parameter] public bool FooterDisabled { get; set; }

    [Parameter] public string? Height { get; set; }

    [Parameter] public string ImportLabel { get; set; } = "Import";

    [Parameter] public InlineAlertRequest? InlineAlert { get; set; }

    [Parameter] public string? MaxWidth { get; set; }

    [Parameter] public string? MinWidth { get; set; }

    [Parameter] public EventCallback OnAccept { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    [Parameter] public EventCallback OnDialogClosedByUser { get; set; }

    [Parameter] public EventCallback OnExport { get; set; }

    [Parameter] public EventCallback OnImport { get; set; }

    [Parameter] public EventCallback<InlineAlertResult> OnInlineAlertResolved { get; set; }

    [Parameter] public EventCallback OnSave { get; set; }

    [Parameter] public string SaveLabel { get; set; } = "Save";

    [Parameter] public bool ShowCloseButton { get; set; }

    /// <summary>Opt the footer-extra slot into a full-width row above the action
    /// buttons, instead of sharing the row with them. Use when the extra content
    /// is a label/control pair that should align with the dialog body rather than
    /// compete with the button row for horizontal space.</summary>
    [Parameter] public bool StackFooterExtra { get; set; }

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

        if (!ReferenceEquals(_previouslyRenderedInlineAlert, InlineAlert))
        {
            _previouslyRenderedInlineAlert = InlineAlert;

            await FocusInlineAlertElementAsync();
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnParametersSet()
    {
        ResetInlineAlertPromptValueIfChanged();

        base.OnParametersSet();
    }

    private async Task FocusInlineAlertElementAsync()
    {
        if (InlineAlert is null) { return; }

        try
        {
            if (InlineAlert.IsPrompt)
            {
                await _inlineAlertInputRef.FocusAsync(true);

                return;
            }

            if (!string.IsNullOrEmpty(InlineAlert.AcceptLabel))
            {
                await _inlineAlertAcceptButtonRef.FocusAsync(true);

                return;
            }

            await _inlineAlertCancelButtonRef.FocusAsync(true);
        }
        catch
        {
            // Best-effort: element may not be in the DOM if the alert was already canceled.
        }
    }

    private Task HandleAcceptAsync() => OnAccept.InvokeAsync();

    private Task HandleCancelButtonAsync() => OnCancel.InvokeAsync();

    private async Task HandleCancelEventAsync()
    {
        if (_isClosed) { return; }

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

    private void ResetInlineAlertPromptValueIfChanged()
    {
        if (ReferenceEquals(_inlineAlertInitializedFor, InlineAlert)) { return; }

        _inlineAlertInitializedFor = InlineAlert;

        _inlineAlertPromptValue = InlineAlert is { IsPrompt: true }
            ? InlineAlert.PromptInitialValue ?? string.Empty
            : string.Empty;
    }
}
