// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class AdvancedFilterRow
{
    private Timer? _debounceTimer;
    private string _errorMessage = string.Empty;

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IFilterService FilterService { get; init; } = null!;

    private void EditFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEditing(Value.Id));

    private void OnInputChanged(ChangeEventArgs e)
    {
        _debounceTimer?.Dispose();

        _debounceTimer = new Timer(s =>
            {
                if (s is not string value) { return; }

                if (FilterService.TryParseExpression(value, out var message))
                {
                    Value.Comparison.Value = value;
                    _errorMessage = string.Empty;
                }
                else
                {
                    _errorMessage = message;
                }

                InvokeAsync(StateHasChanged);
            }, e.Value, 250, 0);
    }

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));

    private void SaveFilter()
    {
        if (!string.IsNullOrEmpty(_errorMessage)) { return; }

        if (string.IsNullOrWhiteSpace(Value.Comparison.Value))
        {
            _errorMessage = "Cannot save an empty filter";

            return;
        }

        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(Value with { IsEditing = false, IsEnabled = true }));
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEnabled(Value.Id));

    private void ToggleFilterExclusion() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterExcluded(Value.Id));
}
