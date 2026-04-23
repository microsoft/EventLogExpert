// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Alerts;

/// <summary>
///     Standalone prompt modal used by <c>ModalAlertDialogService</c> when no host modal is active. Returns the input
///     value on Accept, or <see cref="string.Empty" /> on Cancel/Esc to match the existing
///     <c>IAlertDialogService.DisplayPrompt</c> non-null contract.
/// </summary>
public sealed partial class PromptModal : ModalBase<string>
{
    private readonly string _messageId = $"prompt-modal-message-{Guid.NewGuid():N}";

    private bool _focusOnNextRender = true;
    private ElementReference _inputRef;
    private string _value = string.Empty;

    [Parameter] public string InitialValue { get; set; } = string.Empty;

    [Parameter] public string Message { get; set; } = string.Empty;

    [Parameter] public string Title { get; set; } = string.Empty;

    private string AriaLabelText => string.IsNullOrEmpty(Title) ? "Prompt" : Title;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusOnNextRender && CurrentInlineAlert is null)
        {
            _focusOnNextRender = false;

            try
            {
                await _inputRef.FocusAsync(true);
            }
            catch
            {
                // Best-effort: input may not be in the DOM yet during teardown.
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        _value = InitialValue;
        base.OnInitialized();
    }

    private Task HandleAcceptClickedAsync() => CompleteAsync(_value);

    // Match existing IAlertDialogService.DisplayPrompt contract (non-null string; callers check
    // IsNullOrEmpty). Override so Esc/native-close returns the same value as the Cancel button.
    protected override Task OnCancelAsync() => CompleteAsync(string.Empty);
}
