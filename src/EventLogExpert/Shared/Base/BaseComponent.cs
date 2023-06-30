// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Base;

public abstract class BaseComponent<T> : ComponentBase
{
    private Func<T?, string> _toStringFunc = x => x?.ToString() ?? string.Empty;

    [Parameter]
    public string CssClass { get; set; } = string.Empty;

    public DisplayConverter<T?, string>? DisplayConverter { get; protected set; }

    [Parameter]
    public Func<T?, string> ToStringFunc
    {
        get => _toStringFunc;
        set
        {
            if (_toStringFunc.Equals(value)) { return; }

            _toStringFunc = value;

            DisplayConverter = new DisplayConverter<T?, string> { SetFunc = _toStringFunc };
        }
    }

    [Parameter]
    public T Value { get; set; } = default!;

    [Parameter]
    public EventCallback<T> ValueChanged { get; set; }

    [Parameter]
    public List<T> Values { get; set; } = default!;

    [Parameter]
    public EventCallback<List<T>> ValuesChanged { get; set; }
}
