// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Focus;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor.Rows;

public sealed partial class FilterRowActions : ComponentBase
{
    private readonly string _enableToggleLabelId = ComponentId.NewUnique("filter-row-toggle").Value;

    private ElementReference _editButtonRef;

    [CascadingParameter] public ScenarioAuthoringRowContext? AuthoringContext { get; set; }

    [Parameter] public string? FilterLabelId { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnToggleEnabled { get; set; }

    [Parameter] public SavedFilter? Value { get; set; }

    private string EnableToggleAriaLabelledBy =>
        string.IsNullOrEmpty(FilterLabelId)
            ? _enableToggleLabelId
            : $"{FilterLabelId} {_enableToggleLabelId}";

    private bool ShowScenarioCopy => AuthoringContext is { Enabled: true };

    internal ValueTask FocusEditAsync() => ElementFocus.SafelyAsync(_editButtonRef);

    private static string DescribeFilter(SavedFilter filter) =>
        string.IsNullOrWhiteSpace(filter.ComparisonText) ? "filter" : $"filter '{filter.ComparisonText}'";

    private Task CopyScenarioJsonAsync(SavedFilter savedFilter) =>
        AuthoringContext?.CopyAsync(savedFilter) ?? Task.CompletedTask;
}
