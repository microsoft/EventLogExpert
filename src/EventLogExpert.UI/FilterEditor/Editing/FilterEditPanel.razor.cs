// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor.Editing;

public sealed partial class FilterEditPanel : ComponentBase
{
    private readonly string _colorDescriptionId = $"filter-edit-panel-color-desc-{Guid.NewGuid():N}";

    [Parameter] public string CssClass { get; set; } = string.Empty;

    /// <summary>Optional slot for an "Add Condition" or similar contextual action rendered left of Save/Cancel/Remove.</summary>
    [Parameter] public RenderFragment? ExtraActions { get; set; }

    [Parameter][EditorRequired] public FilterDraft Filter { get; set; } = null!;

    [Parameter] public bool IsPending { get; set; }

    /// <summary>
    ///     Main editor content (the comparison editor for Basic, free-text input for Advanced, or cached dropdown for
    ///     Cached).
    /// </summary>
    [Parameter] public RenderFragment? MainContent { get; set; }

    /// <summary>Optional slot for the Mode <c>ValueSelect</c> dropdown in the settings band.</summary>
    [Parameter] public RenderFragment? ModeSelector { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    [Parameter] public EventCallback<bool> OnExclusionChanged { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnSave { get; set; }

    /// <summary>Optional slot for condition rows (rendered only when supplied and Basic mode populates them).</summary>
    [Parameter] public RenderFragment? SubFiltersContent { get; set; }
}
