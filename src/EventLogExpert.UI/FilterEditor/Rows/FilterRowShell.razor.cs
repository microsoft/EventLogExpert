// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor.Rows;

/// <summary>
///     Outer wrapper for a filter row that selects between the saved-view layout (<see cref="FilterRowHeader" /> +
///     <see cref="FilterRowActions" />) and the edit-view layout (<see cref="Editing.FilterEditPanel" />), plus the
///     inline-or-stacked error display.
/// </summary>
public sealed partial class FilterRowShell : ComponentBase
{
    private readonly string _filterLabelId = $"filter-row-label-{Guid.NewGuid():N}";

    private FilterRowActions? _actionsRef;

    [Parameter] public string? ErrorMessage { get; set; }

    [Parameter] public FilterDraft? Filter { get; set; }

    [Parameter] public bool IsPending { get; set; }

    /// <summary>Comparison editor body rendered in the Main band of <see cref="Editing.FilterEditPanel" />.</summary>
    [Parameter] public RenderFragment? MainContent { get; set; }

    /// <summary>Mode dropdown rendered in the Settings band of <see cref="Editing.FilterEditPanel" />.</summary>
    [Parameter] public RenderFragment? ModeSelector { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }

    [Parameter] public EventCallback<bool> OnExclusionChanged { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnSave { get; set; }

    [Parameter] public EventCallback OnToggleEnabled { get; set; }

    [Parameter] public SavedFilter? Value { get; set; }

    internal ValueTask FocusEditAsync() =>
        _actionsRef?.FocusEditAsync() ?? ValueTask.CompletedTask;
}
