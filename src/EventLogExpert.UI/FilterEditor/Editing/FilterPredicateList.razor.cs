// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.UI.Focus;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor.Editing;

public sealed partial class FilterPredicateList : ComponentBase
{
    private readonly Dictionary<FilterId, FilterPredicateEditor?> _editorRefs = new();

    private ElementReference _addButtonRef;
    private FilterId? _editingPredicateId;
    private bool _focusAddButtonAfterRender;
    private FilterId? _focusChipAfterRender;
    private FilterId? _focusEditorAfterRender;

    [Parameter][EditorRequired] public List<FilterPredicateDraft> Predicates { get; set; } = null!;

    /// <summary>
    ///     The Add (+) button is gated when ANY existing predicate is incomplete, so the user must finish (or remove)
    ///     in-flight predicates before introducing another. Mirrors the per-row Done gate in
    ///     <see cref="FilterPredicateEditor" /> and keeps the in-UI state in sync with
    ///     <see cref="FilterPredicateDraft.IsComplete" /> / the formatter's strict-mode save guard.
    /// </summary>
    private bool CanAddPredicate => Predicates.TrueForAll(p => p.IsComplete);

    /// <summary>
    ///     Post-render focus restoration: after the chip&lt;-&gt;editor swap unmounts the previously focused button, move
    ///     keyboard focus to the new target so the user doesn't lose focus to <c>&lt;body&gt;</c>. Resolution order:
    ///     newly-opened editor's first input -&gt; collapsed chip's edit button -&gt; add button (fallback). Stale refs are
    ///     pruned first so the lookup matches the current render tree.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        PruneStaleEditorRefs();

        if (_focusEditorAfterRender is { } editingId
            && _editorRefs.TryGetValue(editingId, out var editor)
            && editor is not null)
        {
            _focusEditorAfterRender = null;
            await editor.FocusEditorFirstInputAsync();
        }
        else if (_focusChipAfterRender is { } chipId
            && _editorRefs.TryGetValue(chipId, out var chipEditor)
            && chipEditor is not null)
        {
            _focusChipAfterRender = null;
            await chipEditor.FocusChipEditButtonAsync();
        }
        else if (_focusAddButtonAfterRender)
        {
            _focusAddButtonAfterRender = false;
            await ElementFocus.SafelyAsync(_addButtonRef);
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private void AddPredicate()
    {
        if (!CanAddPredicate) { return; }

        var newDraft = new FilterPredicateDraft();
        Predicates.Add(newDraft);
        _editingPredicateId = newDraft.Id;
        _focusEditorAfterRender = newDraft.Id;
    }

    private void CollapseEditor()
    {
        var previouslyEditing = _editingPredicateId;
        _editingPredicateId = null;
        _focusChipAfterRender = previouslyEditing;
    }

    private void PruneStaleEditorRefs()
    {
        if (_editorRefs.Count == 0) { return; }

        if (Predicates.Count == 0)
        {
            _editorRefs.Clear();
            return;
        }

        var liveIds = Predicates.Select(p => p.Id).ToHashSet();

        var stale = _editorRefs
            .Where(kvp => !liveIds.Contains(kvp.Key) || kvp.Value is null)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in stale) { _editorRefs.Remove(id); }
    }

    private void RemovePredicate(FilterId predicateId)
    {
        if (_editingPredicateId == predicateId) { _editingPredicateId = null; }

        Predicates.RemoveAll(p => p.Id == predicateId);
        _focusAddButtonAfterRender = true;
    }

    private void StartEditing(FilterId predicateId)
    {
        _editingPredicateId = predicateId;
        _focusEditorAfterRender = predicateId;
    }
}
