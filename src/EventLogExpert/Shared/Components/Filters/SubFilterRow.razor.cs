// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class SubFilterRow
{
    [Parameter] public FilterId ParentId { get; set; }

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    [Inject] private IFilterService FilterService { get; init; } = null!;

    private List<string> FilteredItems => Items
        .Where(x => x.Contains(Value.Data.Value?.ToLower() ?? string.Empty, StringComparison.CurrentCultureIgnoreCase))
        .ToList();

    private List<string> Items =>
        Value.Data.Category switch
        {
            FilterCategory.Id => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.Id))
                .Distinct().Order()],
            FilterCategory.ActivityId => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.ActivityId))
                .Distinct().Order()],
            FilterCategory.Level => [.. Enum.GetNames<SeverityLevel>()],
            FilterCategory.KeywordsDisplayNames => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.KeywordsDisplayNames))
                .Distinct().Order()],
            FilterCategory.Source => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.Source))
                .Distinct().Order()],
            FilterCategory.TaskCategory => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.TaskCategory))
                .Distinct().Order()],
            _ => []
        };

    private void RemoveSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveSubFilter(ParentId, Value.Id));
}
