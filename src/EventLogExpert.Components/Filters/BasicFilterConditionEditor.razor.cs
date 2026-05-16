// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.UI.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.Components.Filters;

public sealed partial class BasicFilterConditionEditor : ComponentBase
{
    private ImmutableArray<string> _filteredItemsSource = [];
    private string? _filteredItemsValue;

    [Parameter][EditorRequired] public FilterConditionDraft Condition { get; set; } = null!;

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public string? PropertyAriaLabelledBy { get; set; }

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    private List<string> FilteredItems
    {
        get
        {
            var items = Items;
            var value = Condition.Value ?? string.Empty;

            if (_filteredItemsSource.Equals(items) && _filteredItemsValue == value)
            {
                return field;
            }

            _filteredItemsSource = items;
            _filteredItemsValue = value;

            field = [.. items.Where(item => item.Contains(value, StringComparison.CurrentCultureIgnoreCase))];

            return field;
        }
    } = [];

    private ImmutableArray<string> Items =>
        EventPropertyItemsCache.GetItems(EventLogState.Value.ActiveLogs, Condition.Property);

    private EventProperty PropertyBinding
    {
        get => Condition.Property;
        set
        {
            Condition.ChangeProperty(value);

            if (!IsTextOnlyProperty(value) || Condition.MatchMode != MatchMode.Many)
            {
                return;
            }

            Condition.Operator = ComparisonOperator.Contains;
            Condition.MatchMode = MatchMode.Single;
        }
    }

    private static bool IsTextOnlyProperty(EventProperty property) => ComparisonOperatorSets.IsTextOnly(property);

    private void HandleOperatorChanged((ComparisonOperator Op, MatchMode Mode) value)
    {
        Condition.Operator = value.Op;
        Condition.MatchMode = value.Mode;
    }
}
