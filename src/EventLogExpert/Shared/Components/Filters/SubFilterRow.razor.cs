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
            switch (Value.Data.Type)
            {
                case FilterType.Id :
                    return EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.EventIds)
                        .Distinct().OrderBy(id => id).Select(id => id.ToString()).ToList();
                case FilterType.Level :
                    var items = new List<string>();

                    foreach (SeverityLevel item in Enum.GetValues(typeof(SeverityLevel)))
                    {
                        items.Add(item.ToString());
                    }

                    return items;
                case FilterType.KeywordsDisplayNames :
                    return EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.KeywordNames)
                        .Distinct().OrderBy(name => name).Select(name => name.ToString()).ToList();
                case FilterType.Source :
                    return EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.EventProviderNames)
                        .Distinct().OrderBy(name => name).Select(name => name.ToString()).ToList();
                case FilterType.TaskCategory :
                    return EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.TaskNames)
                        .Distinct().OrderBy(name => name).Select(name => name.ToString()).ToList();
                case FilterType.ActivityId :
                    return EventLogState.Value.ActiveLogs.Values.SelectMany(log => log.EventActivityIds)
                        .Distinct().OrderBy(id => id).Select(activityId => activityId.ToString() ?? string.Empty).ToList();
                case FilterType.Description :
                default :
                    return [];
            }
        }
    }

    private void RemoveSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveSubFilter(ParentId, Value.Id));
}
