// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common;
using EventLogExpert.UI.Inputs;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Alerts;

/// <summary>
///     Standalone prompt modal used by <c>AlertDialogService</c> when no host modal is active. Returns the input
///     value on Accept, or <see cref="string.Empty" /> on Cancel/Esc to match the existing
///     <c>IAlertDialogService.DisplayPrompt</c> non-null contract.
/// </summary>
public sealed partial class PromptModal : ModalBase<string>
{
    private readonly string _errorId = ComponentId.NewUnique("prompt-modal-validation").Value;
    private readonly string _inputId = ComponentId.NewUnique("prompt-modal-input").Value;

    private bool _focusOnNextRender = true;
    private TextInput? _input;
    private string _value = string.Empty;

    [Parameter] public string InitialValue { get; set; } = string.Empty;

    [Parameter] public string Message { get; set; } = string.Empty;

    [Parameter] public string Title { get; set; } = string.Empty;

    [Parameter] public Func<string, string?>? Validate { get; set; }

    private string AriaLabelText => string.IsNullOrEmpty(Title) ? "Prompt" : Title;

    private string? ValidationError => Validate?.Invoke(_value);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusOnNextRender && _input is not null && CurrentInlineAlert is null)
        {
            _focusOnNextRender = false;

            try
            {
                await _input.FocusAsync(true);
            }
            catch
            {
                // Best-effort: input may not be in the DOM yet during teardown.
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override Task OnCancelAsync() => CompleteAsync(string.Empty);

    protected override void OnInitialized()
    {
        _value = InitialValue;
        base.OnInitialized();
    }

    private Task HandleAcceptClickedAsync() =>
        ValidationError is not null ? Task.CompletedTask : CompleteAsync(_value);

    private void HandleValueChanged(string value) => _value = value;
}
