// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.Common;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class LibrarySavedTabHeader : ComponentBase
{
    private readonly string _filterRowId = ComponentId.NewUnique("saved-new-row").Value;
    private readonly string _nameInputId = ComponentId.NewUnique("saved-new-name").Value;

    private string _draftName = string.Empty;
    private ImmutableList<string> _draftTags = [];
    private bool _isExpanded;
    private ElementReference _nameInputRef;
    private FilterDraft _pendingDraft = new() { Mode = FilterMode.Advanced };
    private bool _pendingFocus;
    private string? _validationError;

    [Parameter][EditorRequired] public required IReadOnlyList<string> AllLibraryTags { get; set; }

    [Parameter][EditorRequired] public required IReadOnlyList<LibraryEntrySavedFilter> ExistingSavedFilters { get; set; }

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    private IEnumerable<LibraryEntry> ExistingLibraryEntries =>
        FilterLibraryState.Value.Entries.Concat(ExistingSavedFilters);

    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    [Inject] private IState<FilterLibraryState> FilterLibraryState { get; init; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocus)
        {
            _pendingFocus = false;

            try { await _nameInputRef.FocusAsync(); }
            catch (Exception) { }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private Task DiscardAsync()
    {
        ResetState();
        return Task.CompletedTask;
    }

    private Task ExpandAsync()
    {
        _isExpanded = true;
        _pendingFocus = true;
        return Task.CompletedTask;
    }

    private Task OnDraftTagsChangedAsync(ImmutableList<string> tags)
    {
        _draftTags = tags;
        return Task.CompletedTask;
    }

    private void OnNameChanged() => ValidateName();

    private Task OnPendingSaveAsync(SavedFilter built)
    {
        ValidateName();
        if (_validationError is not null) { return Task.CompletedTask; }

        var entry = new LibraryEntrySavedFilter
        {
            Name = _draftName.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = built,
            Origin = LibraryEntryOrigin.UserSaved,
            Tags = _draftTags,
        };

        FilterLibraryCommands.AddEntry(entry);
        AnnouncementService.Announce($"Saved new filter '{entry.Name}' to library");
        ResetState();

        return Task.CompletedTask;
    }

    private void ResetState()
    {
        _isExpanded = false;
        _draftName = string.Empty;
        _draftTags = [];
        _pendingDraft = new FilterDraft { Mode = FilterMode.Advanced };
        _validationError = null;
    }

    private void ValidateName()
    {
        var trimmed = _draftName.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            _validationError = "Name cannot be empty.";

            return;
        }

        if (ExistingLibraryEntries.OfType<LibraryEntrySavedFilter>().Any(e => string.Equals(e.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            _validationError = $"A saved filter named '{trimmed}' already exists.";

            return;
        }

        _validationError = null;
    }
}
