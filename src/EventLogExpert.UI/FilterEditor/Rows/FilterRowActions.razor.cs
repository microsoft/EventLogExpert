// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterEditor.Rows;

public sealed partial class FilterRowActions : ComponentBase
{
    private readonly string _enableToggleLabelId = ComponentId.NewUnique("filter-row-toggle").Value;

    private Button? _editButton;

    [CascadingParameter] public ScenarioAuthoringRowContext? AuthoringContext { get; set; }

    [Parameter] public string? FilterLabelId { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnToggleEnabled { get; set; }

    [Parameter] public SavedFilter? Value { get; set; }

    private string EnableToggleAriaLabelledBy =>
        string.IsNullOrEmpty(FilterLabelId) ?
            _enableToggleLabelId :
            $"{FilterLabelId} {_enableToggleLabelId}";

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    private bool ShowScenarioCopy => AuthoringContext is { Enabled: true };

    internal ValueTask FocusEditAsync() => _editButton is { } button ? ElementFocus.SafelyAsync(button.Element) : ValueTask.CompletedTask;

    private string CopyScenarioAriaLabel(SavedFilter filter) =>
        string.IsNullOrWhiteSpace(filter.ComparisonText) ?
            Localizer["FilterEditor_RowAction_CopyScenarioAria_Unnamed"] :
            Localizer["FilterEditor_RowAction_CopyScenarioAria_Named", filter.ComparisonText];

    private Task CopyScenarioJsonAsync(SavedFilter savedFilter) =>
        AuthoringContext?.CopyAsync(savedFilter) ?? Task.CompletedTask;

    private string EditAriaLabel(SavedFilter filter) =>
        string.IsNullOrWhiteSpace(filter.ComparisonText) ?
            Localizer["FilterEditor_RowAction_EditAria_Unnamed"] :
            Localizer["FilterEditor_RowAction_EditAria_Named", filter.ComparisonText];

    private string RemoveAriaLabel(SavedFilter filter) =>
        string.IsNullOrWhiteSpace(filter.ComparisonText) ?
            Localizer["FilterEditor_RowAction_RemoveAria_Unnamed"] :
            Localizer["FilterEditor_RowAction_RemoveAria_Named", filter.ComparisonText];
}
