// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Drafts;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor.Editing;

/// <summary>
///     Dual-mode predicate view: compact summary chip when <see cref="IsEditing" /> is false, inline editor (AND/OR
///     toggle + comparison editor + Done + Remove) when true. The parent <see cref="FilterPredicateList" /> owns the
///     editing-state flag; this component just renders the right mode based on the flag.
/// </summary>
public sealed partial class FilterPredicateEditor : ComponentBase
{
    [Parameter] public bool IsEditing { get; set; }

    [Parameter] public EventCallback OnDone { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter][EditorRequired] public FilterPredicateDraft Value { get; set; } = null!;

    private string JoinerLabel => Value.JoinWithAny ? "OR" : "AND";

    private string SummaryText
    {
        get
        {
            var cmp = Value.Comparison;

            string opLabel = (cmp.Operator, cmp.MatchMode) switch
            {
                (ComparisonOperator.Equals, MatchMode.Many) => "in",
                (ComparisonOperator.Equals, _) => "==",
                (ComparisonOperator.Contains, _) => "contains",
                (ComparisonOperator.NotEqual, _) => "!=",
                (ComparisonOperator.NotContains, _) => "doesn't contain",
                _ => "?"
            };

            string valueLabel = cmp.MatchMode == MatchMode.Many
                ? cmp.Values.Count switch
                {
                    0 => "?",
                    1 => cmp.Values[0],
                    var count => $"{count} values"
                }
                : string.IsNullOrEmpty(cmp.Value) ? "?" : cmp.Value;

            return $"{cmp.Property} {opLabel} {valueLabel}";
        }
    }

    private Task OnDoneClick() => OnDone.InvokeAsync();

    private Task OnEditChip() => OnEdit.InvokeAsync();

    private Task OnRemoveChip() => OnRemove.InvokeAsync();
}
