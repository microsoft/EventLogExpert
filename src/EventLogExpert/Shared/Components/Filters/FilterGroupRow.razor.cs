// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterGroup;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroupRow : EditableFilterRowBase, IDisposable
{
    private CancellationTokenSource? _debounceCts;
    private string _errorMessage = string.Empty;

    [Parameter] public FilterGroupId ParentId { get; set; }

    [Inject] private IFilterService FilterService { get; init; } = null!;

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }

    protected override void DispatchRemoveFilter()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new FilterGroupAction.RemoveFilter(ParentId, savedFilter.Id));
    }

    /// <summary>
    ///     Cancel pending validation and clear the error banner before the base mutates the draft or bubbles the
    ///     editing-state change. Disposing the CTS prevents a stale debounce continuation from racing with the new edit
    ///     session.
    /// </summary>
    protected override void OnEditSessionResetting()
    {
        _errorMessage = string.Empty;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }

    private void OnInputChanged(ChangeEventArgs e)
    {
        if (Filter is null) { return; }

        var rawText = e.Value as string ?? string.Empty;

        // Persist raw text into the draft synchronously so invalid/partial input survives
        // re-renders. Validation runs separately on the debounce timer.
        Filter.ComparisonText = rawText;

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
                if (sessionToken.IsCancellationRequested || Filter is null) { return; }

                var isValid = FilterService.TryParseExpression(rawText, out var message);

                try
                {
                    await InvokeAsync(() =>
                    {
                        if (sessionToken.IsCancellationRequested || Filter is null) { return; }

                        _errorMessage = isValid ? string.Empty : message;
                        StateHasChanged();
                    }).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // Component disposed mid-debounce; safe to ignore.
                }
            },
            sessionToken);
    }

    private async Task SaveFilter()
    {
        if (Filter is null) { return; }

        if (!FilterService.TryParseExpression(Filter.ComparisonText, out var message))
        {
            _errorMessage = message;
            return;
        }

        var newFilter = Filter.ToFilterModel() with
        {
            Comparison = new FilterComparison { Value = Filter.ComparisonText },
            IsEditing = false,
            IsEnabled = true
        };

        Filter = null;
        _errorMessage = string.Empty;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        if (IsPending)
        {
            await CommitPendingAsync(newFilter);
            return;
        }

        Dispatcher.Dispatch(new FilterGroupAction.SetFilter(ParentId, newFilter));
        await NotifyEditingEndedAsync();
    }

    private void ToggleFilterExclusion()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new FilterGroupAction.ToggleFilterExcluded(ParentId, savedFilter.Id));
    }
}
