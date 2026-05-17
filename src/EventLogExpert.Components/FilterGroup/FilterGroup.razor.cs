// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Common.Clipboard;
using EventLogExpert.UI.Common.Files;
using EventLogExpert.UI.FilterGroup;
using EventLogExpert.UI.FilterPane;
using Microsoft.AspNetCore.Components;
using System.Text.Json;

namespace EventLogExpert.Components.FilterGroup;

public sealed partial class FilterGroup
{
    private readonly HashSet<FilterId> _editingFilters = [];
    private readonly List<FilterDraft> _pendingDrafts = [];

    private FilterGroupId? _trackedGroupId;

    [Parameter] public SavedFilterGroup Group { get; set; } = null!;

    [Parameter] public FilterGroupModal Parent { get; set; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IClipboardService ClipboardService { get; init; } = null!;

    [Inject] private IFilterGroupCommands FilterGroupCommands { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterPaneCommands { get; init; } = null!;

    [Inject] private IFilePickerService FilePickerService { get; init; } = null!;

    [Inject] private IFileSaveService FileSaveService { get; init; } = null!;

    protected override void OnParametersSet()
    {
        // Group identity swap: FilterGroupModal reuses component instances when the sorted list reorders.
        if (_trackedGroupId is not null && _trackedGroupId != Group.Id)
        {
            _editingFilters.Clear();
            _pendingDrafts.Clear();
        }

        _trackedGroupId = Group.Id;

        // Group collapse unmounts row children silently; drop per-row state so SaveGroup isn't blocked next reopen.
        if (!Group.IsEditing)
        {
            _editingFilters.Clear();
            _pendingDrafts.Clear();
        }

        // Drop any tracked IDs that were removed externally (Import/RemoveFilter) so they can't block SaveGroup forever.
        if (_editingFilters.Count > 0)
        {
            var currentIds = Group.Filters.Select(filter => filter.Id).ToHashSet();
            _editingFilters.RemoveWhere(id => !currentIds.Contains(id));
        }

        base.OnParametersSet();
    }

    private void AddFilter() => _pendingDrafts.Add(new FilterDraft());

    private async Task ApplyFilters()
    {
        FilterPaneCommands.ApplyFilterGroup(Group);

        await Parent.CloseAsync();
    }

    private void CancelGroup() => FilterGroupCommands.ToggleGroup(Group.Id);

    private async Task CopyGroup()
    {
        if (Group.Filters.Count <= 0) { return; }

        var text = Group.Filters.Count > 1 ?
            string.Join(" || ", Group.Filters.Select(filter => $"({filter.ComparisonText})")) :
            Group.Filters[0].ComparisonText;

        await ClipboardService.CopyTextAsync(text);
    }

    private async Task ExportGroup()
    {
        var snapshot = Group;

        try
        {
            await FileSaveService.SaveAsync(
                snapshot.DisplayName,
                FileSaveFileTypes.Json,
                stream => JsonSerializer.SerializeAsync(stream, snapshot));
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Export Failed",
                $"An exception occurred while exporting saved groups: {ex.Message}",
                "OK");
        }
    }

    private void HandlePendingDiscard(FilterDraft draft) => _pendingDrafts.Remove(draft);

    private void HandlePendingSave(FilterDraft draft, SavedFilter filter)
    {
        _pendingDrafts.Remove(draft);

        FilterGroupCommands.SetFilter(Group.Id, filter);
    }

    private async Task ImportGroup()
    {
        try
        {
            var path = await FilePickerService.PickAsync(
                "Please select a json file to import",
                FilePickerFileTypes.Json);

            if (path is null) { return; }

            await using var stream = File.OpenRead(path);
            var group = await JsonSerializer.DeserializeAsync<SavedFilterGroup>(stream);

            if (group is null) { return; }

            var updatedGroup = Group with
            {
                Name = group.Name,
                Filters = group.Filters
            };

            FilterGroupCommands.SetGroup(updatedGroup);
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Import Failed",
                $"An exception occurred while importing group: {ex.Message}",
                "OK");
        }
    }

    private void OnRowEditingChanged((FilterId Id, bool IsEditing) change)
    {
        if (change.IsEditing)
        {
            _editingFilters.Add(change.Id);
        }
        else
        {
            _editingFilters.Remove(change.Id);
        }
    }

    private void RemoveGroup() => FilterGroupCommands.RemoveGroup(Group.Id);

    private async Task RenameGroup()
    {
        var newName =
            await AlertDialogService.DisplayPrompt("Group Name", "What would you like to name this group?", Group.Name);

        if (string.IsNullOrEmpty(newName))
        {
            await AlertDialogService.ShowAlert("Rename Failed", "Name cannot be empty", "OK");

            return;
        }

        if (string.Equals(newName, Group.Name))
        {
            await AlertDialogService.ShowAlert("Rename Failed", "Name cannot be the same as previous name", "OK");

            return;
        }

        FilterGroupCommands.SetGroup(Group with { Name = newName });
    }

    private void SaveGroup()
    {
        // Block save while any saved row is mid-edit or any new-filter draft is unsaved.
        if (_editingFilters.Count > 0) { return; }

        if (_pendingDrafts.Count > 0) { return; }

        FilterGroupCommands.SetGroup(Group);
    }

    private void ToggleGroup() => FilterGroupCommands.ToggleGroup(Group.Id);
}
