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

    [Parameter] public FilterGroupId ParentId { get; set; }

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    private void EditFilter() => Dispatcher.Dispatch(new FilterGroupAction.ToggleFilter(ParentId, Value.Id));

    private void OnInputChanged(ChangeEventArgs e)
    {
        _debounceTimer?.Dispose();

        _debounceTimer = new Timer(s =>
            {
                if (s is not string value) { return; }

                if (FilterMethods.TryParseExpression(value, out var message))
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

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterGroupAction.RemoveFilter(ParentId, Value.Id));

    private void SaveFilter()
    {
        if (!string.IsNullOrEmpty(_errorMessage)) { return; }

        Dispatcher.Dispatch(new FilterGroupAction.SetFilter(ParentId, Value));
    }

    private void ToggleFilterExclusion() =>
        Dispatcher.Dispatch(new FilterGroupAction.ToggleFilterExcluded(ParentId, Value.Id));
}
