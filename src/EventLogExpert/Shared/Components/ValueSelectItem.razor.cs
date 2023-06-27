// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class ValueSelectItem<T>
{
    private ValueSelect<T> _parent = null!;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = null!;

    [Parameter]
    public bool IsDisabled { get; set; }

    public string ItemId { get; } = $"_{Guid.NewGuid().ToString()[..8]}";

    [Parameter]
    public T Value { get; set; } = default!;

    private bool IsSelected => ValueSelect.Value?.Equals(Value) ?? false;

    [CascadingParameter]
    private ValueSelect<T> ValueSelect
    {
        get => _parent;
        set
        {
            _parent = value;
            _parent.AddItem(this);
        }
    }

    private async void SelectItem()
    {
        if (IsDisabled) return;

        await ValueSelect.UpdateValue(this);
        await InvokeAsync(StateHasChanged);
    }
}
