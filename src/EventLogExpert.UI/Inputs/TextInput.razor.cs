// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.Inputs;

public partial class TextInput : InputComponent<string>
{
    private ElementReference _element;
    private Dictionary<string, object> _valueChangeBinding = [];

    [Parameter] public bool AriaInvalid { get; set; }

    [Parameter] public string? Id { get; set; }

    [Parameter] public EventCallback OnEnter { get; set; }

    [Parameter] public bool UpdateOnInput { get; set; }

    internal ValueTask FocusAsync(bool preventScroll = false) => _element.FocusAsync(preventScroll);

    protected override void OnParametersSet()
    {
        string eventName = UpdateOnInput ? "oninput" : "onchange";

        if (!_valueChangeBinding.ContainsKey(eventName))
        {
            _valueChangeBinding = new Dictionary<string, object>
            {
                [eventName] = EventCallback.Factory.Create<ChangeEventArgs>(this, UpdateValue),
            };
        }

        base.OnParametersSet();
    }

    private Task HandleKeyDownAsync(KeyboardEventArgs args) =>
        // Skip the Enter that commits an IME composition candidate (Chromium reports IsComposing on it) so a
        // composition commit does not prematurely confirm the dialog.
        args is { Key: "Enter", IsComposing: false } && OnEnter.HasDelegate ? OnEnter.InvokeAsync() : Task.CompletedTask;

    private async Task UpdateValue(ChangeEventArgs args)
    {
        Value = args.Value?.ToString() ?? string.Empty;
        await ValueChanged.InvokeAsync(Value);
    }
}
