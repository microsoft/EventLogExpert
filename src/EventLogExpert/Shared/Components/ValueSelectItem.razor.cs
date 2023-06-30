// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Shared.Components;

public partial class ValueSelectItem<T> : IDisposable
{
    [Parameter]
    public bool ClearItem { get; set; } = false;

    private bool _isSelected = false;
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
            DisplayConverter<T?, string>? converter = ValueSelect.DisplayConverter;
            return converter is null ? $"{Value}" : converter.Set(Value);
        }
    }

    [CascadingParameter]
    private ValueSelect<T> ValueSelect
    {
        get => _parent;
        set
        {
            _parent = value;
            _isSelected = _parent.AddItem(this);
        }
    }

    [SuppressMessage("Usage",
        "CA1816:Dispose methods should call SuppressFinalize",
        Justification = "Not a redundant GC call since actual item is just removed from parent list")]
    public void Dispose()
    {
        ValueSelect.RemoveItem(this);
    }

    private async void SelectItem()
    {
        if (IsDisabled) { return; }

        if (ClearItem) { ValueSelect.ClearSelected(); }

        await ValueSelect.UpdateValue(Value);
        await InvokeAsync(StateHasChanged);
    }
}
