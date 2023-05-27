// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class TimeZoneSelect
{
    private bool _isDropDownVisible;

    [Parameter]
    public string Value { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    private string IsDropDownVisible => _isDropDownVisible.ToString().ToLower();

    private void SetDropDownVisibility(bool visible) => _isDropDownVisible = visible;

    private async Task UpdateValue(string value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(Value);
    }
}
