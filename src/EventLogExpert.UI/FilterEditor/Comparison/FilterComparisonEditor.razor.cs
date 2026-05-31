// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.EventData;
using EventLogExpert.Runtime.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterEditor.Comparison;

public sealed partial class FilterComparisonEditor : ComponentBase
{
    private ImmutableArray<string> _filteredItemsSource = [];
    private string? _filteredItemsValue;

    [Parameter][EditorRequired] public FilterComparisonDraft Comparison { get; set; } = null!;

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    ///     Fired whenever any sub-control mutates <see cref="Comparison" /> (property, operator/match-mode, or value).
    ///     Parents subscribe to re-evaluate completeness-derived UI state (e.g.,
    ///     <see cref="EventLogExpert.Filtering.Drafts.FilterPredicateDraft.IsComplete" />) which Blazor doesn't otherwise
    ///     re-render automatically because the mutated <see cref="Comparison" /> is a reference-stable record bag.
    /// </summary>
    [Parameter] public EventCallback OnChanged { get; set; }

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

            if (IsTextOnlyProperty(value) && Comparison.MatchMode == MatchMode.Many)
            {
                Comparison.Operator = ComparisonOperator.Contains;
                Comparison.MatchMode = MatchMode.Single;
            }

            _ = OnChanged.InvokeAsync();
        }
    }

    private static bool IsTextOnlyProperty(EventProperty property) => FilterPropertyConstraints.IsTextOnly(property);

    private async Task HandleOperatorChanged((ComparisonOperator Op, MatchMode Mode) value)
    {
        Comparison.Operator = value.Op;
        Comparison.MatchMode = value.Mode;
        await OnChanged.InvokeAsync();
    }

    private async Task HandleValueChanged(string? value)
    {
        Comparison.Value = value;
        await OnChanged.InvokeAsync();
    }

    private async Task HandleValuesChanged(List<string> values)
    {
        Comparison.Values = values;
        await OnChanged.InvokeAsync();
    }
}
