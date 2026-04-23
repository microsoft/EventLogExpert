// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Base;

public abstract class BaseFilterRow : ComponentBase
{
    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    protected FilterCategory CategoryBinding
    {
        get => CurrentData.Category;
        set
        {
            CurrentData.Category = value;

            if (IsTextOnlyCategory(value) && CurrentData.Evaluator == FilterEvaluator.MultiSelect)
            {
                CurrentData.Evaluator = FilterEvaluator.Contains;
            }
        }
    }

    protected abstract FilterData CurrentData { get; }

    [Inject] protected IState<EventLogState> EventLogState { get; init; } = null!;

    protected List<string> FilteredItems => Items
        .Where(item => item.Contains(CurrentData.Value ?? string.Empty, StringComparison.CurrentCultureIgnoreCase))
        .ToList();

    protected List<string> Items =>
        CurrentData.Category switch
        {
            FilterCategory.Id => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.Id))
                .Distinct().Order()],
            FilterCategory.ActivityId => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.ActivityId))
                .Distinct().Order()],
            FilterCategory.Level => [.. Enum.GetNames<SeverityLevel>()],
            FilterCategory.Keywords => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.Keywords))
                .Distinct().Order()],
            FilterCategory.Source => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.Source))
                .Distinct().Order()],
            FilterCategory.TaskCategory => [.. EventLogState.Value.ActiveLogs.Values
                .SelectMany(log => log.GetCategoryValues(FilterCategory.TaskCategory))
                .Distinct().Order()],
            _ => []
        };

    protected static bool IsTextOnlyCategory(FilterCategory category) =>
        category is FilterCategory.Description or FilterCategory.Xml;
}
