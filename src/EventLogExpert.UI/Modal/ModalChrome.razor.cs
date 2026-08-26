// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Common.Interop;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace EventLogExpert.UI.Modal;

public sealed partial class ModalChrome : ComponentBase, IAsyncDisposable
{
    private readonly string _inlineAlertMessageId = ComponentId.NewUnique("modal-inline-alert-message").Value;
    private readonly string _inlineAlertTitleId = ComponentId.NewUnique("modal-inline-alert-title").Value;
    private readonly string _inlineAlertValidationErrorId = ComponentId.NewUnique("modal-inline-alert-validation").Value;
    private readonly string _titleId = ComponentId.NewUnique("modal-title").Value;

    private ElementReference _dialogRef;
    private PrimaryButton? _inlineAlertAcceptButton;
    private Button? _inlineAlertCancelButton;
    private InlineAlertRequest? _inlineAlertInitializedFor;
    private TextInput? _inlineAlertInput;
    private string _inlineAlertPromptValue = string.Empty;
    private bool _isClosed;
    private bool _isClosingByCancel;
    private IJSObjectReference? _modalModule;
    private InlineAlertRequest? _previouslyRenderedInlineAlert;
    private InlineAlertRequest? _validationCacheAlert;
    private string? _validationCacheError;
    private string? _validationCacheValue;

    [Parameter] public bool AcceptDisabled { get; set; }

    [Parameter] public string? AcceptLabel { get; set; }

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public ModalBodyLayout BodyLayout { get; set; } = ModalBodyLayout.Content;

    [Parameter] public string? CancelLabel { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string? CloseButtonAriaLabel { get; set; }

    [Parameter] public string? CloseLabel { get; set; }

    [Parameter] public string? DialogClass { get; set; }

    [Parameter] public string? ExportLabel { get; set; }

    [Parameter] public RenderFragment? ExtraFooterContent { get; set; }

    [Parameter] public FooterPreset Footer { get; set; } = FooterPreset.CloseOnly;

    [Parameter] public bool FooterDisabled { get; set; }

    [Parameter] public string? Height { get; set; }

    [Parameter] public string? ImportLabel { get; set; }

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

    [Parameter] public string? SaveLabel { get; set; }

    [Parameter] public bool ShowCloseButton { get; set; }

    [Parameter] public bool StackFooterExtra { get; set; }

    [Parameter] public string? Title { get; set; }

    [Inject] private IBannerCycleStateService CycleState { get; init; } = null!;

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

    private string EffectiveAcceptLabel => string.IsNullOrEmpty(AcceptLabel) ? Localizer["Modal_Accept"] : AcceptLabel;

    private string EffectiveCancelLabel => string.IsNullOrEmpty(CancelLabel) ? Localizer["Modal_Cancel"] : CancelLabel;

    private string EffectiveCloseButtonAriaLabel =>
        string.IsNullOrEmpty(CloseButtonAriaLabel) ? Localizer["Modal_Close"] : CloseButtonAriaLabel;

    private string EffectiveCloseLabel => string.IsNullOrEmpty(CloseLabel) ? Localizer["Modal_Close"] : CloseLabel;

    private string EffectiveExportLabel => string.IsNullOrEmpty(ExportLabel) ? Localizer["Modal_Export"] : ExportLabel;

    private string EffectiveImportLabel => string.IsNullOrEmpty(ImportLabel) ? Localizer["Modal_Import"] : ImportLabel;

    private string EffectiveSaveLabel => string.IsNullOrEmpty(SaveLabel) ? Localizer["Modal_Save"] : SaveLabel;

    private bool HasInlineAlert => InlineAlert is not null;

    [Inject] private IJSRuntime JSRuntime { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private string? ValidationError
    {
        get
        {
            if (InlineAlert is not { IsPrompt: true, Validate: { } validator }) { return null; }

            var value = _inlineAlertPromptValue;

            if (!ReferenceEquals(_validationCacheAlert, InlineAlert) || _validationCacheValue != value)
            {
                _validationCacheAlert = InlineAlert;
                _validationCacheValue = value;
                _validationCacheError = validator(value);
            }

            return _validationCacheError;
        }
    }

    public async Task CloseAsync()
    {
        if (_isClosed) { return; }

        _isClosed = true;

        if (_modalModule is null) { return; }

        try
        {
            await _modalModule.InvokeVoidAsync("closeModal", _dialogRef);
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        CycleState.SetModalContentDisplayed(false);
        await CloseAsync();

        await JsModuleInterop.DisposeModuleSafelyAsync(_modalModule);

        _modalModule = null;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _modalModule = await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/EventLogExpert.UI/Modal/ModalChrome.razor.js");

                if (_isClosed)
                {
                    await _modalModule.DisposeAsync();
                    _modalModule = null;

                    return;
                }

                await _modalModule.InvokeVoidAsync("showModal", _dialogRef);
                CycleState.SetModalContentDisplayed(true);
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
                if (_inlineAlertInput is not null)
                {
                    await _inlineAlertInput.FocusAsync(true);
                }

                return;
            }

            if (!string.IsNullOrEmpty(InlineAlert.AcceptLabel))
            {
                await (_inlineAlertAcceptButton?.FocusAsync(true) ?? ValueTask.CompletedTask);

                return;
            }

            await (_inlineAlertCancelButton?.FocusAsync(true) ?? ValueTask.CompletedTask);
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

    private void HandleInlineAlertPromptValueChanged(string value) => _inlineAlertPromptValue = value;

    private async Task HandleInlineAlertSecondaryAsync()
    {
        if (InlineAlert is null) { return; }

        string? promptValue = InlineAlert.IsPrompt ? _inlineAlertPromptValue : null;

        await OnInlineAlertResolved.InvokeAsync(new InlineAlertResult(false, promptValue) { SecondaryChosen = true });
    }

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
