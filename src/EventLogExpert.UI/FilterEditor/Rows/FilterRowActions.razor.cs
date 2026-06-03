// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.UI.Focus;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor.Rows;

public sealed partial class FilterRowActions : ComponentBase
{
    private readonly string _enableToggleLabelId = $"filter-row-toggle-{Guid.NewGuid():N}";

    private ElementReference _editButtonRef;

    /// <summary>
    ///     DOM id of the comparison-text element in <see cref="FilterRowHeader" />; chained into the Toggle's
    ///     <c>aria-labelledby</c> so SR users hear the filter context plus the Enable purpose hint.
    /// </summary>
    [Parameter] public string? FilterLabelId { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnToggleEnabled { get; set; }

    [Parameter] public SavedFilter? Value { get; set; }

    private string EnableToggleAriaLabelledBy =>
        string.IsNullOrEmpty(FilterLabelId)
            ? _enableToggleLabelId
            : $"{FilterLabelId} {_enableToggleLabelId}";

    internal ValueTask FocusEditAsync() => ElementFocus.SafelyAsync(_editButtonRef);

    private static string DescribeFilter(SavedFilter filter) =>
        string.IsNullOrWhiteSpace(filter.ComparisonText) ? "filter" : $"filter '{filter.ComparisonText}'";
}
