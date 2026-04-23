// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class AdvancedFilterRow : IDisposable
{
    private CancellationTokenSource? _debounceCts;
    private string _errorMessage = string.Empty;
    private FilterEditorModel? _filter;

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IFilterService FilterService { get; init; } = null!;

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }

    protected override void OnParametersSet()
    {
        // Auto-create a draft when the row mounts in edit mode (e.g. AddAdvancedFilter dispatches
        // AddFilter with IsEditing=true). The `_filter is null` guard prevents overwriting an
        // in-flight draft when the parent re-renders due to unrelated state changes.
        if (Value.IsEditing && _filter is null)
        {
            _filter = FilterEditorModel.FromFilterModel(Value);
        }

        base.OnParametersSet();
    }

    private void CancelFilter()
    {
        _filter = null;
        _errorMessage = string.Empty;
        _debounceCts?.Cancel();

        // A new advanced filter has no saved comparison string — Cancel removes it entirely.
        if (string.IsNullOrEmpty(Value.Comparison.Value))
        {
            Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));
        }
        else
        {
            Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEditing(Value.Id));
        }
    }

    private void EditFilter()
    {
        _filter = FilterEditorModel.FromFilterModel(Value);
        _errorMessage = string.Empty;
        Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEditing(Value.Id));
    }

    private void OnInputChanged(ChangeEventArgs eventArgs)
    {
        if (_filter is null) { return; }

        var rawText = eventArgs.Value as string ?? string.Empty;

        // Always persist the raw text into the draft so partial / invalid input survives
        // re-renders. Validation runs on a debounce and only updates the error banner.
        _filter.ComparisonText = rawText;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();

        var sessionToken = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, sessionToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            // The session token guards against stale callbacks writing into a draft that was
            // discarded (Cancel) or replaced (new edit session) before the debounce elapsed.
            if (sessionToken.IsCancellationRequested || _filter is null) { return; }

            var isValid = FilterService.TryParseExpression(rawText, out var message);

            try
            {
                await InvokeAsync(() =>
                {
                    if (sessionToken.IsCancellationRequested || _filter is null) { return; }

                    _errorMessage = isValid ? string.Empty : message;
                    StateHasChanged();
                }).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Component disposed mid-debounce; safe to ignore.
            }
        }, sessionToken);
    }

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveFilter(Value.Id));

    private void SaveFilter()
    {
        if (_filter is null) { return; }

        if (!string.IsNullOrEmpty(_errorMessage)) { return; }

        if (string.IsNullOrWhiteSpace(_filter.ComparisonText))
        {
            _errorMessage = "Cannot save an empty filter";

            return;
        }

        var newFilter = _filter.ToFilterModel() with
        {
            IsEditing = false,
            IsEnabled = true
        };

        _filter = null;
        _errorMessage = string.Empty;
        _debounceCts?.Cancel();

        Dispatcher.Dispatch(new FilterPaneAction.SetFilter(newFilter));
    }

    private void ToggleFilter() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterEnabled(Value.Id));

    private void ToggleFilterExclusion() => Dispatcher.Dispatch(new FilterPaneAction.ToggleFilterExcluded(Value.Id));
}
