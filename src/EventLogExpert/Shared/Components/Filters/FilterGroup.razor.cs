// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using System.Text.Json;
using Windows.Storage.Pickers;
using WinRT.Interop;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroup
{
    private readonly HashSet<FilterId> _editingFilters = [];
    private readonly List<FilterEditorModel> _pendingDrafts = [];
    private FilterGroupId? _trackedGroupId;

    [Parameter] public FilterGroupModel Group { get; set; } = null!;

    [Parameter] public FilterGroupModal Parent { get; set; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    protected override void OnParametersSet()
    {
        // Group identity swap: FilterGroupModal renders FilterGroup instances without an @key
        // and the underlying list is sorted by name, so a rename or import can repoint the same
        // component instance at a different group. Drop all per-row state to avoid leaking
        // editing flags or pending drafts across groups.
        if (_trackedGroupId is not null && _trackedGroupId != Group.Id)
        {
            _editingFilters.Clear();
            _pendingDrafts.Clear();
        }

        _trackedGroupId = Group.Id;

        // When the group collapses (Cancel, Save via SetGroup reducer, Rename, Import) the
        // FilterGroupRow children unmount without notifying us, so any in-flight per-row edit
        // state must be dropped here. Otherwise SaveGroup would silently block forever the next
        // time the group is reopened. Pending drafts are dropped for the same reason — collapse
        // means "abandon unsaved work", consistent with the legacy IsEditing-placeholder flow.
        if (!Group.IsEditing)
        {
            _editingFilters.Clear();
            _pendingDrafts.Clear();
        }

        // Prune _editingFilters of any IDs no longer present in Group.Filters so external
        // mutations (ImportGroup replacing the entire filter list, RemoveFilter dispatched
        // from elsewhere, etc.) cannot leave permanent stale entries that would block SaveGroup.
        if (_editingFilters.Count > 0)
        {
            var currentIds = Group.Filters.Select(filter => filter.Id).ToHashSet();
            _editingFilters.RemoveWhere(id => !currentIds.Contains(id));
        }

        base.OnParametersSet();
    }

    private void AddFilter() =>
        _pendingDrafts.Add(new FilterEditorModel { FilterType = FilterType.Advanced });

    private async Task ApplyFilters()
    {
        Dispatcher.Dispatch(new FilterPaneAction.ApplyFilterGroup(Group));
        await Parent.CloseAsync();
    }

    private void CancelGroup() => Dispatcher.Dispatch(new FilterGroupAction.ToggleGroup(Group.Id));

    private void CopyGroup()
    {
        if (Group.Filters.Count <= 0) { return; }

        var text = Group.Filters.Count > 1 ?
            string.Join(" || ", Group.Filters.Select(filter => $"({filter.Comparison.Value})")) :
            Group.Filters[0].Comparison.Value;

        _ = Clipboard.SetTextAsync(text);
    }

    private async Task ExportGroup()
    {
        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Group.DisplayName
        };

        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });

        if (Application.Current?.Windows[0].Handler?.PlatformView is not MauiWinUIWindow window)
        {
            return;
        }

        InitializeWithWindow.Initialize(picker, window.WindowHandle);

        var result = await picker.PickSaveFileAsync();

        if (result is null) { return; }

        try
        {
            using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(Group));

            await using var fileStream = await result.OpenStreamForWriteAsync();

            await stream.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Export Failed",
                $"An exception occurred while exporting saved groups: {ex.Message}",
                "OK");
        }
    }

    private void HandlePendingDiscard(FilterEditorModel draft) => _pendingDrafts.Remove(draft);

    private void HandlePendingSave(FilterEditorModel draft, FilterModel filter)
    {
        _pendingDrafts.Remove(draft);

        // SetFilter is upsert (replace-by-Id, append-if-missing) — same defense against a
        // double-click race that the FilterPane pending-draft handler relies on.
        Dispatcher.Dispatch(new FilterGroupAction.SetFilter(Group.Id, filter));
    }

    private async Task ImportGroup()
    {
        PickOptions options = new()
        {
            PickerTitle = "Please select a json file to import",
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>> { { DevicePlatform.WinUI, [".json"] } })
        };

        var result = await FilePicker.Default.PickAsync(options);

        if (result is null) { return; }

        try
        {
            await using var stream = File.OpenRead(result.FullPath);
            var group = await JsonSerializer.DeserializeAsync<FilterGroupModel>(stream);

            if (group is null) { return; }

            var updatedGroup = Group with
            {
                Name = group.Name,
                Filters = group.Filters
            };

            Dispatcher.Dispatch(new FilterGroupAction.SetGroup(updatedGroup));
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

    private void RemoveGroup() => Dispatcher.Dispatch(new FilterGroupAction.RemoveGroup(Group.Id));

    private async Task RenameGroup()
    {
        var newName = await AlertDialogService.DisplayPrompt("Group Name", "What would you like to name this group?", Group.Name);

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

        Dispatcher.Dispatch(new FilterGroupAction.SetGroup(Group with { Name = newName }));
    }

    private void SaveGroup()
    {
        // Block save while any row is mid-edit. Existing-filter edits are tracked locally via
        // _editingFilters (populated by OnRowEditingChanged bubbled up from FilterGroupRow).
        // New-filter rows live in _pendingDrafts (this component owns them; they never hit
        // Fluxor state until the user hits Save on the row).
        if (_editingFilters.Count > 0) { return; }
        if (_pendingDrafts.Count > 0) { return; }

        Dispatcher.Dispatch(new FilterGroupAction.SetGroup(Group));
    }

    private void ToggleGroup() => Dispatcher.Dispatch(new FilterGroupAction.ToggleGroup(Group.Id));
}
