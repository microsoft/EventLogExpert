// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.EventData;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.Components.Filters;

public sealed partial class FilterComparisonEditor : ComponentBase
{
    private ImmutableArray<string> _filteredItemsSource = [];
    private string? _filteredItemsValue;

    [Parameter][EditorRequired] public FilterComparisonDraft Comparison { get; set; } = null!;

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public string? PropertyAriaLabelledBy { get; set; }

    [Inject] private IState<EventLogState> EventLogState { get; init; } = null!;

    private List<string> FilteredItems
    {
        get
        {
            var items = Items;
            var value = Comparison.Value ?? string.Empty;

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
        EventPropertyValuesCache.GetValues(EventLogState.Value.ActiveLogs, Comparison.Property);

    private EventProperty PropertyBinding
    {
        get => Comparison.Property;
        set
        {
            Comparison.ChangeProperty(value);

            if (!IsTextOnlyProperty(value) || Comparison.MatchMode != MatchMode.Many)
            {
                return;
            }

            Comparison.Operator = ComparisonOperator.Contains;
            Comparison.MatchMode = MatchMode.Single;
        }
    }

    private static bool IsTextOnlyProperty(EventProperty property) => FilterPropertyConstraints.IsTextOnly(property);

    private void HandleOperatorChanged((ComparisonOperator Op, MatchMode Mode) value)
    {
        Comparison.Operator = value.Op;
        Comparison.MatchMode = value.Mode;
    }
}
