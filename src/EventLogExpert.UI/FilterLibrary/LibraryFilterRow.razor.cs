// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.UI.FilterEditor;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class LibraryFilterRow : ComponentBase
{
    private FilterEditorCore? _coreRef;

    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();

    [Parameter] public EventCallback OnCancel { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }

    [Parameter] public EventCallback<bool> OnExclusionChanged { get; set; }

    [Parameter] public EventCallback OnPendingDiscard { get; set; }

    [Parameter] public EventCallback<SavedFilter> OnPendingSave { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback<SavedFilter> OnSave { get; set; }

    [Parameter] public EventCallback OnToggleEnabled { get; set; }

    [Parameter] public FilterDraft? PendingDraft { get; set; }

    [Parameter] public SavedFilter? Value { get; set; }

    internal bool IsEditing => _coreRef?.IsEditing ?? false;

    internal ValueTask FocusEditAsync() =>
        _coreRef?.FocusEditAsync() ?? ValueTask.CompletedTask;
}
