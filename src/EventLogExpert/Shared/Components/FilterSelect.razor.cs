using EventLogExpert.Library.Helpers;
using Microsoft.AspNetCore.Components;
using ValueChangedEventArgs = EventLogExpert.Library.EventArgs.ValueChangedEventArgs;

namespace EventLogExpert.Shared.Components;

public partial class FilterSelect<TInput>
{
    [Parameter]
    public string CssClass { get; set; } = "";

    [Parameter]
    public IEnumerable<TInput>? Items { get; set; }

    [Parameter]
    public EventCallback<ValueChangedEventArgs> OnValueChangedEvent { get; set; }

    [Parameter]
    public TInput Value { get; set; } = default!;

    private async Task UpdateValue(ChangeEventArgs args)
    {
        if (Value is SeverityLevel && Enum.TryParse(args.Value?.ToString(), out SeverityLevel value))
        {
            Value = (TInput)Convert.ChangeType(value, typeof(TInput));
        }
        else
        {
            Value = (TInput)Convert.ChangeType(args.Value, typeof(TInput))!;
        }

        await OnValueChangedEvent.InvokeAsync(new ValueChangedEventArgs(Value));
    }
}
