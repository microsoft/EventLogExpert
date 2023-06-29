// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Shared.Components;

public partial class ValueSelectItem<T> : IDisposable
{
    private bool _disposed = false;
    private ValueSelect<T> _parent = null!;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public bool IsDisabled { get; set; }

    public string ItemId { get; } = $"_{Guid.NewGuid().ToString()[..8]}";

    [Parameter]
    public T Value { get; set; } = default!;

    private string? DisplayString
    {
        get
        {
            var converter = ValueSelect.DisplayConverter;
            return converter is null ? $"{Value}" : converter.Set(Value);
        }
    }

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

    [SuppressMessage("Usage",
        "CA1816:Dispose methods should call SuppressFinalize",
        Justification = "Not a redundant GC call since actual item is just removed from parent list")]
    public void Dispose() => ValueSelect.RemoveItem(this);

    private async void SelectItem()
    {
        if (IsDisabled) { return; }

        await ValueSelect.UpdateValue(this);
        await InvokeAsync(StateHasChanged);
    }
}
