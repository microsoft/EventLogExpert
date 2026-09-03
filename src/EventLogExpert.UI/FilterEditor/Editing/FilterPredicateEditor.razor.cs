// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Localization;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterEditor.Editing;

/// <summary>
///     Dual-mode predicate view: compact summary chip when <see cref="IsEditing" /> is false, inline editor (AND/OR
///     toggle + comparison editor + Done + Remove) when true. The parent <see cref="FilterPredicateList" /> owns the
///     editing-state flag; this component just renders the right mode based on the flag.
/// </summary>
public sealed partial class FilterPredicateEditor : ComponentBase
{
    private ChromelessButton? _chipEditButton;
    private ChromelessButton? _editorFirstInput;

    [Parameter] public bool IsEditing { get; set; }

    [Parameter] public EventCallback OnChanged { get; set; }

    [Parameter] public EventCallback OnDone { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter][EditorRequired] public FilterPredicateDraft Value { get; set; } = null!;

    private string JoinerAriaLabel =>
        Value.JoinWithAny ?
            Localizer["FilterEditor_PredicateJoin_OrAria"] :
            Localizer["FilterEditor_PredicateJoin_AndAria"];

    private string JoinerLabel =>
        Value.JoinWithAny ?
            Localizer["FilterEditor_PredicateJoin_OrLabel"] :
            Localizer["FilterEditor_PredicateJoin_AndLabel"];

    private string JoinerTitle =>
        Value.JoinWithAny ?
            Localizer["FilterEditor_PredicateJoin_OrTitle"] :
            Localizer["FilterEditor_PredicateJoin_AndTitle"];

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private string SummaryText
    {
        get
        {
            var cmp = Value.Comparison;

            string opLabel = (cmp.Operator, cmp.MatchMode) switch
            {
                (ComparisonOperator.Equals, MatchMode.Many) => Localizer["FilterEditor_PredicateSummary_Operator_In"],
                (ComparisonOperator.Equals, _) => "==",
                (ComparisonOperator.Contains, _) => Localizer["FilterEditor_PredicateSummary_Operator_Contains"],
                (ComparisonOperator.NotEqual, _) => "!=",
                (ComparisonOperator.NotContains, _) => Localizer["FilterEditor_PredicateSummary_Operator_NotContains"],
                _ => "?"
            };

            string valueLabel = cmp.MatchMode == MatchMode.Many ?
                cmp.Values.Count switch
                {
                    0 => "?",
                    1 => cmp.Values[0],
                    var count => Localizer["FilterEditor_PredicateSummary_ValueCount_Many", count]
                } :
                string.IsNullOrEmpty(cmp.Value) ? "?" : cmp.Value;

            return Localizer["FilterEditor_PredicateSummary", cmp.Property.ToString(), opLabel, valueLabel];
        }
    }

    internal ValueTask FocusChipEditButtonAsync() =>
        _chipEditButton is { } button ? ElementFocus.SafelyAsync(button.Element) : ValueTask.CompletedTask;

    internal ValueTask FocusEditorFirstInputAsync() =>
        _editorFirstInput is { } button ? ElementFocus.SafelyAsync(button.Element) : ValueTask.CompletedTask;

    /// <summary>
    ///     AND/OR joiner click handler. Toggles <see cref="FilterPredicateDraft.JoinWithAny" /> and bubbles the change up
    ///     via <see cref="OnChanged" /> so the parent list re-evaluates Done / Add gating.
    /// </summary>
    private async Task OnAndOrClick()
    {
        Value.JoinWithAny = !Value.JoinWithAny;
        await OnChanged.InvokeAsync();
    }

    /// <summary>
    ///     Re-render this editor when the child comparison editor mutates the predicate, then bubble up so the parent
    ///     <see cref="FilterPredicateList" /> can re-evaluate <c>CanAddPredicate</c> / Done-button enablement.
    /// </summary>
    private async Task OnComparisonChanged()
    {
        await InvokeAsync(StateHasChanged);
        await OnChanged.InvokeAsync();
    }

    private Task OnDoneClick() => OnDone.InvokeAsync();

    private Task OnEditChip() => OnEdit.InvokeAsync();

    private Task OnRemoveChip() => OnRemove.InvokeAsync();
}
