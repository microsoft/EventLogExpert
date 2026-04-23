// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterGroup;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroupRow : IDisposable
{
    private CancellationTokenSource? _debounceCts;
    private string _errorMessage = string.Empty;
    private FilterEditorModel? _filter;

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public FilterGroupId ParentId { get; set; }

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
        // Auto-create a draft when the row mounts in edit mode (e.g. AddFilter dispatches a
        // FilterModel with IsEditing=true). The `_filter is null` guard ensures we don't
        // overwrite an in-flight draft when the parent re-renders due to unrelated state changes.
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
        _debounceCts?.Dispose();
        _debounceCts = null;

        // A new filter has no saved comparison string — Cancel removes it entirely. An existing
        // filter just exits edit mode; the saved Value is untouched because the draft was a copy.
        if (string.IsNullOrEmpty(Value.Comparison.Value))
        {
            Dispatcher.Dispatch(new FilterGroupAction.RemoveFilter(ParentId, Value.Id));
        }
        else
        {
            Dispatcher.Dispatch(new FilterGroupAction.ToggleFilter(ParentId, Value.Id));
        }
    }

    private void EditFilter()
    {
        _filter = FilterEditorModel.FromFilterModel(Value);
        _errorMessage = string.Empty;

        Dispatcher.Dispatch(new FilterGroupAction.ToggleFilter(ParentId, Value.Id));
    }

    private void OnInputChanged(ChangeEventArgs e)
    {
        if (_filter is null) { return; }

        var rawText = e.Value as string ?? string.Empty;

        // Persist raw text into the draft synchronously so invalid/partial input survives
        // re-renders. Validation runs separately on the debounce timer.
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

    private void RemoveFilter() => Dispatcher.Dispatch(new FilterGroupAction.RemoveFilter(ParentId, Value.Id));

    private void SaveFilter()
    {
        if (_filter is null) { return; }

        if (!FilterService.TryParseExpression(_filter.ComparisonText, out var message))
        {
            _errorMessage = message;
            return;
        }

        var newFilter = _filter.ToFilterModel() with
        {
            Comparison = new FilterComparison { Value = _filter.ComparisonText },
            IsEditing = false,
            IsEnabled = true
        };

        _filter = null;
        _errorMessage = string.Empty;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        Dispatcher.Dispatch(new FilterGroupAction.SetFilter(ParentId, newFilter));
    }

    private void ToggleFilterExclusion() =>
        Dispatcher.Dispatch(new FilterGroupAction.ToggleFilterExcluded(ParentId, Value.Id));
}
