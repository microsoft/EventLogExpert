// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.UI.Common;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterEditor.Comparison;

public sealed partial class FilterComparisonEditor : ComponentBase
{
    private ImmutableArray<string> _filteredItemsSource = [];
    private string? _filteredItemsValue;

    [Parameter][EditorRequired] public FilterComparisonDraft Comparison { get; set; } = null!;

    [Parameter] public string Id { get; set; } = ComponentId.NewUnique().Value;

    /// <summary>
    ///     Fired whenever any sub-control mutates <see cref="Comparison" /> (property, operator/match-mode, or value).
    ///     Parents subscribe to re-evaluate completeness-derived UI state (e.g.,
    ///     <see cref="EventLogExpert.Filtering.Drafts.FilterPredicateDraft.IsComplete" />) which Blazor doesn't otherwise
    ///     re-render automatically because the mutated <see cref="Comparison" /> is a reference-stable record bag.
    /// </summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    [Parameter] public string? PropertyAriaLabelledBy { get; set; }

    private ImmutableArray<string> EventDataFieldNames => EventLogQueries.GetEventDataFieldNames();

    [Inject] private IEventLogQueries EventLogQueries { get; init; } = null!;

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

    private bool IsEventDataProperty => Comparison.Property is EventProperty.EventData;

    private bool IsUserDataProperty => Comparison.Property is EventProperty.UserData;

    private ImmutableArray<string> Items => Comparison.Property switch
    {
        EventProperty.EventData => EventLogQueries.GetEventDataFieldValues(Comparison.EventDataFieldName ?? string.Empty),
        EventProperty.UserData => EventLogQueries.GetUserDataFieldValues(Comparison.UserDataFieldName ?? string.Empty),
        _ => EventLogQueries.GetPropertyValues(Comparison.Property)
    };

    private bool ExcludesEmptyMultiSelectValue =>
        Comparison.Operator is ComparisonOperator.Contains or ComparisonOperator.NotContains
            || FilterPropertyConstraints.IsGuidValued(Comparison.Property);

    private IEnumerable<string> MultiSelectCandidates =>
        ExcludesEmptyMultiSelectValue ? Items.Where(item => !string.IsNullOrEmpty(item)) : Items;

    private EventProperty PropertyBinding
    {
        get => Comparison.Property;
        set
        {
            Comparison.ChangeProperty(value);

            // Normalize a multi-select the new property no longer supports: text-only fields have no multi-select at
            // all; otherwise drop to Single any operator-aware Many kind the new property doesn't offer.
            if (Comparison.MatchMode == MatchMode.Many)
            {
                if (IsTextOnlyProperty(value))
                {
                    Comparison.Operator = ComparisonOperator.Contains;
                    Comparison.MatchMode = MatchMode.Single;
                }
                else
                {
                    var stillSupported = Comparison.Operator switch
                    {
                        ComparisonOperator.Equals => true,
                        ComparisonOperator.Contains => FilterPropertyConstraints.SupportsContainsMany(value),
                        _ => FilterPropertyConstraints.SupportsNoneOfMany(value)
                    };

                    if (!stillSupported) { Comparison.MatchMode = MatchMode.Single; }
                }
            }

            _ = OnChanged.InvokeAsync();
        }
    }

    private ImmutableArray<string> UserDataFieldNames => EventLogQueries.GetUserDataFieldNames();

    private static bool IsTextOnlyProperty(EventProperty property) => FilterPropertyConstraints.IsTextOnly(property);

    private async Task HandleEventDataFieldNameChanged(string? fieldName)
    {
        Comparison.EventDataFieldName = fieldName;

        // The available value space is field-specific, so a field-name change invalidates the current value(s).
        Comparison.Value = null;
        Comparison.Values.Clear();
        await OnChanged.InvokeAsync();
    }

    private async Task HandleOperatorChanged((ComparisonOperator Op, MatchMode Mode) value)
    {
        Comparison.Operator = value.Op;
        Comparison.MatchMode = value.Mode;

        // Switching into a Many Contains/NotContains drops any empty values carried over from a prior Equals selection,
        // so the row never visibly holds an "(Empty)" chip that would be stripped again at save.
        if (value is { Mode: MatchMode.Many, Op: ComparisonOperator.Contains or ComparisonOperator.NotContains })
        {
            Comparison.Values = Comparison.Values.Where(item => !string.IsNullOrEmpty(item)).ToList();
        }

        await OnChanged.InvokeAsync();
    }

    private async Task HandleUserDataFieldNameChanged(string? fieldName)
    {
        Comparison.UserDataFieldName = fieldName;

        // The available value space is field-specific, so a field-name change invalidates the current value(s).
        Comparison.Value = null;
        Comparison.Values.Clear();
        await OnChanged.InvokeAsync();
    }

    private async Task HandleValueChanged(string? value)
    {
        Comparison.Value = value;
        await OnChanged.InvokeAsync();
    }

    private async Task HandleValuesChanged(List<string> values)
    {
        // Drop degenerate empty values for Contains/NotContains (a selected "(Empty)" would turn the row into a
        // match-all) and for GUID-valued properties (an empty value coerces away and can never match); Equals/NotEqual
        // on string fields keep empty values (a valid empty-field match).
        Comparison.Values = ExcludesEmptyMultiSelectValue
            ? values.Where(value => !string.IsNullOrEmpty(value)).ToList()
            : values;

        await OnChanged.InvokeAsync();
    }
}
