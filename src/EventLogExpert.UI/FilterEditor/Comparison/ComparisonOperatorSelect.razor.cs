// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor.Comparison;

/// <summary>
///     Single-dropdown widget that surfaces the (<see cref="ComparisonOperator" />, <see cref="MatchMode" />) pair as
///     a user-friendly enumeration. Equals + Many maps to "Is Any Of"; "Contains Any" is offered when
///     <see cref="SupportsContainsMany" /> and the negated "Is None Of" / "Contains None" when
///     <see cref="SupportsNoneOfMany" />. All remaining choices map to single-value variants.
/// </summary>
public sealed partial class ComparisonOperatorSelect
{
    internal enum ComparisonKind
    {
        Equals,
        Contains,
        NotEqual,
        NotContains,
        MultiSelect,
        MultiContains,
        MultiNotEqual,
        MultiNotContains
    }

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string? AriaLabelledBy { get; set; }

    [Parameter] public string CssClass { get; set; } = "input filter-dropdown";

    [Parameter] public MatchMode MatchMode { get; set; }

    [Parameter] public EventCallback<(ComparisonOperator Op, MatchMode Mode)> OnChanged { get; set; }

    [Parameter] public ComparisonOperator Operator { get; set; }

    /// <summary>Set <c>true</c> for fields that also offer "Contains Any" multi-select (scalar strings + EventData/UserData).</summary>
    [Parameter] public bool SupportsContainsMany { get; set; }

    /// <summary>Set <c>false</c> for fields that do not support multi-value selection (e.g., free-text Description/Xml).</summary>
    [Parameter] public bool SupportsMany { get; set; } = true;

    /// <summary>
    ///     Set <c>true</c> for fields that also offer the negated "Is None Of" / "Contains None" multi kinds (scalar
    ///     strings).
    /// </summary>
    [Parameter] public bool SupportsNoneOfMany { get; set; }

    /// <summary>Set <c>false</c> for non-text fields where substring comparison is meaningless (e.g., an enum field).</summary>
    [Parameter] public bool SupportsText { get; set; } = true;

    private ComparisonKind Current =>
        (Operator, MatchMode) switch
        {
            (ComparisonOperator.Equals, MatchMode.Many) => ComparisonKind.MultiSelect,
            (ComparisonOperator.Contains, MatchMode.Many) => ComparisonKind.MultiContains,
            (ComparisonOperator.NotEqual, MatchMode.Many) => ComparisonKind.MultiNotEqual,
            (ComparisonOperator.NotContains, MatchMode.Many) => ComparisonKind.MultiNotContains,
            (ComparisonOperator.Contains, _) => ComparisonKind.Contains,
            (ComparisonOperator.NotEqual, _) => ComparisonKind.NotEqual,
            (ComparisonOperator.NotContains, _) => ComparisonKind.NotContains,
            _ => ComparisonKind.Equals
        };

    private static string KindLabel(ComparisonKind kind) =>
        kind switch
        {
            ComparisonKind.Equals => "Equals",
            ComparisonKind.Contains => "Contains",
            ComparisonKind.NotEqual => "Not Equal",
            ComparisonKind.NotContains => "Not Contains",
            ComparisonKind.MultiSelect => "Is Any Of",
            ComparisonKind.MultiContains => "Contains Any",
            ComparisonKind.MultiNotEqual => "Is None Of",
            ComparisonKind.MultiNotContains => "Contains None",
            _ => kind.ToString()
        };

    private Task HandleKindChanged(ComparisonKind kind)
    {
        (ComparisonOperator op, MatchMode mode) = kind switch
        {
            ComparisonKind.Equals => (ComparisonOperator.Equals, MatchMode.Single),
            ComparisonKind.Contains => (ComparisonOperator.Contains, MatchMode.Single),
            ComparisonKind.NotEqual => (ComparisonOperator.NotEqual, MatchMode.Single),
            ComparisonKind.NotContains => (ComparisonOperator.NotContains, MatchMode.Single),
            ComparisonKind.MultiSelect => (ComparisonOperator.Equals, MatchMode.Many),
            ComparisonKind.MultiContains => (ComparisonOperator.Contains, MatchMode.Many),
            ComparisonKind.MultiNotEqual => (ComparisonOperator.NotEqual, MatchMode.Many),
            ComparisonKind.MultiNotContains => (ComparisonOperator.NotContains, MatchMode.Many),
            _ => (ComparisonOperator.Equals, MatchMode.Single)
        };

        return OnChanged.InvokeAsync((op, mode));
    }
}
