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
    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public FilterId ParentId { get; set; }

    [Parameter] public FilterModel Value { get; set; } = null!;

    /// <summary>Wraps <c>Value.Data.Category</c> so that a transition into a text-only category
    /// (Description / Xml) auto-corrects an incompatible evaluator. Without this, switching from
    /// e.g. Source (MultiSelect) to Xml would leave Evaluator=MultiSelect with no matching option
    /// in the comparison dropdown — producing an invalid filter on save.</summary>
    private FilterCategory CategoryBinding
    {
        get => Value.Data.Category;
        set
        {
            Value.Data.Category = value;

            if (IsTextOnlyCategory(value) && Value.Data.Evaluator == FilterEvaluator.MultiSelect)
            {
                Value.Data.Evaluator = FilterEvaluator.Contains;
            }
        }
    }

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

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

    /// <summary>Categories whose value is free-form text (no fixed enumerable set of options).
    /// These render as a single text input and disallow MultiSelect comparison.</summary>
    private static bool IsTextOnlyCategory(FilterCategory category) =>
        category is FilterCategory.Description or FilterCategory.Xml;

    private void RemoveSubFilter() => Dispatcher.Dispatch(new FilterPaneAction.RemoveSubFilter(ParentId, Value.Id));
}
