// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterGroup;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroupRow
{
    private Timer? _debounceTimer = null;
    private string _errorMessage = string.Empty;
    private bool _isFilterValid;

    [Parameter] public Guid ParentId { get; set; }

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    private void EditFilter()
    {
        // Check isn't necessary (should never return false)
        // but we want save button to be visible when applied from filter group
        _isFilterValid = FilterMethods.TryParseExpression(Value.Comparison.Value, out var message);
        _errorMessage = message;

        Dispatcher.Dispatch(new FilterGroupAction.ToggleFilter(ParentId, Value.Id));
    }

    private void OnInputChanged(ChangeEventArgs e)
    {
        _debounceTimer?.Dispose();

        _debounceTimer = new Timer(s =>
            {
                _isFilterValid = FilterMethods.TryParseExpression(s?.ToString(), out var message);
                _errorMessage = message;

                if (_isFilterValid)
                {
                    Value.Comparison.Value = s?.ToString()!;
                }

                InvokeAsync(StateHasChanged);
            }, e.Value, 250, 0);
    }

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterGroupAction.RemoveFilter(ParentId, Value.Id));

    private void SaveFilter() => Dispatcher.Dispatch(new FilterGroupAction.SetFilter(ParentId, Value));
}
