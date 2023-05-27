using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class FilterSelect<InputType> where InputType : Enum
{
    private bool _isDropDownVisible;

    [Parameter]
    public InputType Value { get; set; } = default!;

    [Parameter]
    public EventCallback<InputType> ValueChanged { get; set; }

    private string IsDropDownVisible => _isDropDownVisible.ToString().ToLower();

    private void SetDropDownVisibility(bool visible) => _isDropDownVisible = visible;

    private async Task UpdateValue(InputType value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(Value);
    }
}
