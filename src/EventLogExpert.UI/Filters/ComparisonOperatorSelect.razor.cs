// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Filters;

/// <summary>
///     Single-dropdown widget that surfaces the (<see cref="ComparisonOperator" />, <see cref="MatchMode" />) pair as
///     a user-friendly enumeration. Equals + Many maps to the "Multi Select" kind (any-of); all other choices map to
///     single-value variants.
/// </summary>
public sealed partial class ComparisonOperatorSelect
{
    public enum ComparisonKind
    {
        Equals,
        Contains,
        NotEqual,
        NotContains,
        MultiSelect
    }

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string? AriaLabelledBy { get; set; }

    [Parameter] public string CssClass { get; set; } = "input filter-dropdown";

    [Parameter] public MatchMode MatchMode { get; set; }

    [Parameter] public EventCallback<(ComparisonOperator Op, MatchMode Mode)> OnChanged { get; set; }

    [Parameter] public ComparisonOperator Operator { get; set; }

    /// <summary>Set <c>false</c> for fields that do not support multi-value selection (e.g., free-text Description/Xml).</summary>
    [Parameter] public bool SupportsMany { get; set; } = true;

    private ComparisonKind Current =>
        (Operator, MatchMode) switch
        {
            (ComparisonOperator.Equals, MatchMode.Many) => ComparisonKind.MultiSelect,
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
            ComparisonKind.MultiSelect => "Multi Select",
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
            _ => (ComparisonOperator.Equals, MatchMode.Single)
        };

        return OnChanged.InvokeAsync((op, mode));
    }
}
