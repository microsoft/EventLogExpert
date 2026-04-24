// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

/// <summary>Category + evaluator + value input shared by <see cref="FilterRow" /> and <see cref="SubFilterRow" />.</summary>
public sealed partial class FilterCategoryEditor : ComponentBase
{
    /// <summary>Optional id of the external label for the category dropdown; falls back to a built-in aria-label.</summary>
    [Parameter] public string? CategoryAriaLabelledBy { get; set; }

    [Parameter][EditorRequired] public FilterData Data { get; set; } = null!;

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    private FilterCategory CategoryBinding
    {
        get => Data.Category;
        set
        {
            Data.Category = value;

            if (IsTextOnlyCategory(value) && Data.Evaluator == FilterEvaluator.MultiSelect)
            {
                Data.Evaluator = FilterEvaluator.Contains;
            }
        }
    }

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    private List<string> FilteredItems =>
        Items
            .Where(item => item.Contains(Data.Value ?? string.Empty, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

    // TODO: This should be added to the EventLogState and updated as logs are added/removed
    private List<string> Items =>
        Data.Category switch
        {
            FilterCategory.Id =>
            [
                .. EventLogState.Value.ActiveLogs.Values
                    .SelectMany(log => log.GetCategoryValues(FilterCategory.Id))
                    .Distinct().Order()
            ],
            FilterCategory.ActivityId =>
            [
                .. EventLogState.Value.ActiveLogs.Values
                    .SelectMany(log => log.GetCategoryValues(FilterCategory.ActivityId))
                    .Distinct().Order()
            ],
            FilterCategory.Level => [.. Enum.GetNames<SeverityLevel>()],
            FilterCategory.Keywords =>
            [
                .. EventLogState.Value.ActiveLogs.Values
                    .SelectMany(log => log.GetCategoryValues(FilterCategory.Keywords))
                    .Distinct().Order()
            ],
            FilterCategory.Source =>
            [
                .. EventLogState.Value.ActiveLogs.Values
                    .SelectMany(log => log.GetCategoryValues(FilterCategory.Source))
                    .Distinct().Order()
            ],
            FilterCategory.TaskCategory =>
            [
                .. EventLogState.Value.ActiveLogs.Values
                    .SelectMany(log => log.GetCategoryValues(FilterCategory.TaskCategory))
                    .Distinct().Order()
            ],
            _ => []
        };

    private static bool IsTextOnlyCategory(FilterCategory category) =>
        category is FilterCategory.Description or FilterCategory.Xml;
}
