// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI;
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

    [Parameter] public FilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    private List<string> FilteredItems => Items
        .Where(x => x.Contains(Value.Data.Value?.ToLower() ?? string.Empty, StringComparison.CurrentCultureIgnoreCase))
        .ToList();

    private List<string> Items
    {
        get
        {
            switch (Value.Data.Category)
            {
                case FilterCategory.Id:
                    return EventLogState.Value.ActiveLogs.Values
                        .SelectMany(log => log.GetCategoryValues(FilterCategory.Id))
                        .Distinct().Order().ToList();
                case FilterCategory.ActivityId:
                    return EventLogState.Value.ActiveLogs.Values
                        .SelectMany(log => log.GetCategoryValues(FilterCategory.ActivityId))
                        .Distinct().Order().ToList();
                case FilterCategory.Level:
                    return Enum.GetNames<SeverityLevel>().ToList();
                case FilterCategory.KeywordsDisplayNames:
                    return EventLogState.Value.ActiveLogs.Values
                        .SelectMany(log => log.GetCategoryValues(FilterCategory.KeywordsDisplayNames))
                        .Distinct().Order().ToList();
                case FilterCategory.Source:
                    return EventLogState.Value.ActiveLogs.Values
                        .SelectMany(log => log.GetCategoryValues(FilterCategory.Source))
                        .Distinct().Order().ToList();
                case FilterCategory.TaskCategory:
                    return EventLogState.Value.ActiveLogs.Values
                        .SelectMany(log => log.GetCategoryValues(FilterCategory.TaskCategory))
                        .Distinct().Order().ToList();
                case FilterCategory.Description:
                default:
                    return [];
            }
        }
    }

    private void RemoveSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveSubFilter(ParentId, Value.Id));
}
