// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using ValueChangedEventArgs = EventLogExpert.Library.EventArgs.ValueChangedEventArgs;

namespace EventLogExpert.Shared.Components;

public partial class FilterInput
{
    [Parameter]
    public string Value { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<ValueChangedEventArgs> OnValueChangedEvent { get; set; }

    private async Task UpdateValue(ChangeEventArgs args)
    {
        Value = (string)Convert.ChangeType(args.Value?.ToString(), typeof(string))!;
        await OnValueChangedEvent.InvokeAsync(new ValueChangedEventArgs(Value));
    }
}
