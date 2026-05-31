// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterEditor.Editing;

public sealed partial class FilterPredicateList : ComponentBase
{
    private FilterId? _editingPredicateId;

    [Parameter][EditorRequired] public List<FilterPredicateDraft> Predicates { get; set; } = null!;

    private bool CanAddPredicate => Predicates.TrueForAll(p => p.IsComplete);

    private void AddPredicate()
    {
        if (!CanAddPredicate) { return; }

        var newDraft = new FilterPredicateDraft();
        Predicates.Add(newDraft);
        _editingPredicateId = newDraft.Id;
    }

    private void CollapseEditor() => _editingPredicateId = null;

    private void RemovePredicate(FilterId predicateId)
    {
        if (_editingPredicateId == predicateId) { _editingPredicateId = null; }

        Predicates.RemoveAll(sf => sf.Id == predicateId);
    }

    private void StartEditing(FilterId predicateId) => _editingPredicateId = predicateId;
}
