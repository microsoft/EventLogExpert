﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public partial class SubFilterRow
{
    [Parameter] public Guid ParentId { get; set; }

    [Parameter] public SubFilterModel Value { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    private List<string> Items
    {
        get
        {
            switch (Value.FilterType)
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
                case FilterType.Description :
                default :
                    return new List<string>();
            }
        }
    }

    private List<string> FilteredItems =>
        Items.Where(x => x.ToLower().Contains(Value.FilterValue?.ToLower() ?? string.Empty)).ToList();

    private void RemoveSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveSubFilter(ParentId, Value.Id));
}
