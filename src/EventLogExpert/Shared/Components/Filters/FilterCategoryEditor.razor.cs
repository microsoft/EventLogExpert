// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

/// <summary>
/// Renders the category dropdown + evaluator dropdown + value input (text / multi-select /
/// single-select) used by both <see cref="FilterRow"/> (via its draft) and <see cref="SubFilterRow"/>.
/// The <see cref="Data"/> parameter is held by reference so child <c>@bind-Value</c>s mutate the
/// parent draft directly.
/// </summary>
public sealed partial class FilterCategoryEditor : ComponentBase
{
    [Parameter, EditorRequired] public FilterData Data { get; set; } = null!;

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Optional id of an external element that labels the category dropdown (e.g. the
    /// "Filter On:" / "Exclude On:" label on <see cref="FilterRow"/>). When null, the dropdown
    /// uses its own "Category" aria-label instead.
    /// </summary>
    [Parameter] public string? CategoryAriaLabelledBy { get; set; }

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

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

    private List<string> FilteredItems => Items
        .Where(item => item.Contains(Data.Value ?? string.Empty, StringComparison.CurrentCultureIgnoreCase))
        .ToList();

    private List<string> Items =>
        Data.Category switch
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

    private static bool IsTextOnlyCategory(FilterCategory category) =>
        category is FilterCategory.Description or FilterCategory.Xml;
}
