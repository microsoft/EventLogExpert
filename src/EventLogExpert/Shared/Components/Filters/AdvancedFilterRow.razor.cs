// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class AdvancedFilterRow
{
    private Timer? _advancedFilterDebounceTimer = null;
    private string _advancedFilterErrorMessage = string.Empty;
    private bool _isAdvancedFilterValid;

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    private void EditFilter()
    {
        // Check isn't necessary (should never return false)
        // but we want save button to be visible when applied from filter group
        _isAdvancedFilterValid = FilterMethods.TryParseExpression(Value.Comparison.Value, out var message);
        _advancedFilterErrorMessage = message;

        Dispatcher.Dispatch(new FilterPaneAction.ToggleAdvancedFilterEditing(Value.Id));
    }

    private void OnInputChanged(ChangeEventArgs e)
    {
        _advancedFilterDebounceTimer?.Dispose();

        _advancedFilterDebounceTimer = new Timer(s =>
            {
                _isAdvancedFilterValid = FilterMethods.TryParseExpression(s?.ToString(), out var message);
                _advancedFilterErrorMessage = message;

                if (_isAdvancedFilterValid)
                {
                    Value.Comparison.Value = s?.ToString()!;
                }

                InvokeAsync(StateHasChanged);
            }, e.Value, 250, 0);
    }

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveAdvancedFilter(Value.Id));

    private void SaveFilter() =>
        Dispatcher.Dispatch(new FilterPaneAction.SetAdvancedFilter(Value with { IsEditing = false, IsEnabled = true }));

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleAdvancedFilterEnabled(Value.Id));
}
