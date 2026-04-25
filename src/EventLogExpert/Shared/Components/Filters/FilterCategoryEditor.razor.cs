// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterCategoryEditor : ComponentBase
{
    private List<string> _filteredItemsCache = [];
    private ImmutableArray<string> _filteredItemsSource = ImmutableArray<string>.Empty;
    private string? _filteredItemsValue;

    [Parameter] public string? CategoryAriaLabelledBy { get; set; }

    [Parameter][EditorRequired] public FilterDataDraft Data { get; set; } = null!;

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    private FilterCategory CategoryBinding
    {
        get => Data.Category;
        set
        {
            Data.ChangeCategory(value);

            if (IsTextOnlyCategory(value) && Data.Evaluator == FilterEvaluator.MultiSelect)
            {
                Data.Evaluator = FilterEvaluator.Contains;
            }
        }
    }

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    private List<string> FilteredItems
    {
        get
        {
            var items = Items;
            var value = Data.Value ?? string.Empty;

            if (_filteredItemsSource.Equals(items) && _filteredItemsValue == value)
            {
                return _filteredItemsCache;
            }

            _filteredItemsSource = items;
            _filteredItemsValue = value;
            _filteredItemsCache = [.. items.Where(item =>
                item.Contains(value, StringComparison.CurrentCultureIgnoreCase))];

            return _filteredItemsCache;
        }
    }

    private ImmutableArray<string> Items =>
        FilterCategoryItemsCache.GetItems(EventLogState.Value.ActiveLogs, Data.Category);

    private static bool IsTextOnlyCategory(FilterCategory category) =>
        category is FilterCategory.Description or FilterCategory.Xml;
}
