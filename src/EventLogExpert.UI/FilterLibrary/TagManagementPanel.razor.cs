// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class TagManagementPanel : ComponentBase
{
    private const int SearchVisibleThreshold = 6;

    private string? _deleteConfirmTag;
    private string _editingNewName = string.Empty;
    private string? _editingTag;
    private ElementReference _editInputRef;
    private string? _mergeTargetTag;
    private bool _pendingFocusEdit;
    private string _searchText = string.Empty;

    [Parameter][EditorRequired] public required IReadOnlyList<LibraryEntry> AllEntries { get; set; }

    [Parameter][EditorRequired] public required IReadOnlyList<string> AllLibraryTags { get; set; }

    [Parameter] public string? Id { get; set; }

    [Parameter] public EventCallback<string> OnTagDeleted { get; set; }

    [Parameter] public EventCallback<(string OldName, string NewName)> OnTagRenamed { get; set; }

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingFocusEdit)
        {
            _pendingFocusEdit = false;

            try { await _editInputRef.FocusAsync(); }
            catch (Exception) { }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private static string NormalizeForCallback(string tag) =>
        LibraryEntryTagNormalizer.Normalize([tag]).FirstOrDefault() ?? string.Empty;

    private void BeginDelete(string tag)
    {
        _deleteConfirmTag = tag;
        CancelEdit();
    }

    private void BeginRename(string tag)
    {
        _editingTag = tag;
        _editingNewName = tag;
        _mergeTargetTag = null;
        _deleteConfirmTag = null;
        _pendingFocusEdit = true;
    }

    private Dictionary<string, int> BuildUsageCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in AllEntries)
        {
            foreach (var tag in entry.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }

        return counts;
    }

    private void CancelDelete() => _deleteConfirmTag = null;

    private void CancelEdit()
    {
        _editingTag = null;
        _editingNewName = string.Empty;
        _mergeTargetTag = null;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_deleteConfirmTag is null) { return; }

        var tag = _deleteConfirmTag;
        FilterLibraryCommands.DeleteTag(tag);
        AnnouncementService.Announce($"Deleted tag '{tag}' from library");

        await OnTagDeleted.InvokeAsync(NormalizeForCallback(tag));
        CancelDelete();
    }

    private async Task ConfirmMergeAsync()
    {
        if (_editingTag is null || _mergeTargetTag is null) { return; }

        var oldName = _editingTag;
        var target = _mergeTargetTag;
        FilterLibraryCommands.RenameTag(oldName, target);
        AnnouncementService.Announce($"Merged tag '{oldName}' into '{target}'");

        await OnTagRenamed.InvokeAsync((NormalizeForCallback(oldName), NormalizeForCallback(target)));
        CancelEdit();
    }

    private async Task ConfirmRenameAsync()
    {
        if (_editingTag is null) { return; }

        var oldName = _editingTag;
        var newName = _editingNewName.Trim();

        if (string.IsNullOrEmpty(newName)) { return; }

        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            CancelEdit();
            return;
        }

        var existing = AllLibraryTags.FirstOrDefault(t =>
            !string.Equals(t, oldName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t, newName, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            _mergeTargetTag = existing;
            return;
        }

        FilterLibraryCommands.RenameTag(oldName, newName);
        AnnouncementService.Announce($"Renamed tag '{oldName}' to '{newName}'");

        await OnTagRenamed.InvokeAsync((NormalizeForCallback(oldName), NormalizeForCallback(newName)));
        CancelEdit();
    }

    private IEnumerable<(string Tag, int Count)> FilteredTags()
    {
        var filter = _searchText.Trim();
        var usageCounts = BuildUsageCounts();

        return AllLibraryTags
            .Where(t => string.IsNullOrEmpty(filter) || t.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(t => (t, usageCounts.GetValueOrDefault(t)));
    }

    private void OnEditNameChanged()
    {
        if (_mergeTargetTag is null) { return; }

        var newName = _editingNewName.Trim();
        if (!string.Equals(_mergeTargetTag, newName, StringComparison.OrdinalIgnoreCase))
        {
            _mergeTargetTag = null;
        }
    }
}
