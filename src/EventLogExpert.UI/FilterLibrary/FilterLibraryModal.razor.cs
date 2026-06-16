// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Modal;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Collections.Immutable;
using System.Security;

namespace EventLogExpert.UI.FilterLibrary;

public sealed partial class FilterLibraryModal : ModalBase<bool>
{
    private const int TagFilterBarMaxVisible = 10;

    private static readonly (LibraryTab Tab, string Label)[] s_tabs =
    [
        (LibraryTab.Saved, "Saved"),
        (LibraryTab.Favorites, "Favorites"),
        (LibraryTab.PreviouslyUsed, "Previously Used"),
    ];

    private readonly Dictionary<(LibraryTab Tab, LibraryEntryId Id), LibraryEntryRow?> _rowRefs = new();

    private readonly Dictionary<LibraryTab, ImmutableList<string>> _selectedTagsByTab = new()
    {
        [LibraryTab.Saved] = [],
        [LibraryTab.Favorites] = [],
        [LibraryTab.PreviouslyUsed] = [],
    };

    private readonly string _tagManagementPanelId = ComponentId.NewUnique("tag-mgmt").Value;
    private readonly string _tagOverflowRegionId = ComponentId.NewUnique("tag-overflow").Value;

    private LibraryTab _activeTab = LibraryTab.Saved;
    private bool _isTagManagementExpanded;
    private bool _isTagOverflowExpanded;
    private bool _justClearedTags;
    private LibraryTab? _pendingFocusSourceTab;
    private LibraryEntryId? _pendingFocusTargetEntryId;
    private bool _pendingFocusToActiveTab;

    private Dictionary<LibraryTab, ImmutableList<string>>? _selectedTagsBeforeTagOp;
    private SidebarTabs<LibraryTab>? _sidebarTabsRef;

    [Parameter] public LibraryTab? InitialTab { get; set; }

    private IReadOnlyList<LibraryEntryFilterSet> AllFilterSets =>
        [.. FilterLibraryState.Value.Entries
            .OfType<LibraryEntryFilterSet>()
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];

    private IReadOnlyList<string> AllLibraryTags =>
        [.. FilterLibraryState.Value.Entries
            .SelectMany(e => e.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)];

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    private IReadOnlyList<(LibraryTab Tab, string Label)> CurrentTabLabels =>
    [
        (LibraryTab.Saved, $"Saved ({SavedEntries.Count})"),
        (LibraryTab.Favorites, $"Favorites ({FavoriteEntries.Count})"),
        (LibraryTab.PreviouslyUsed, $"Previously Used ({PreviouslyUsedEntries.Count})"),
    ];

    [Inject] private IFilterLibraryExportService ExportService { get; init; } = null!;

    private IReadOnlyList<LibraryEntry> FavoriteEntries
    {
        get
        {
            var selected = SelectedTagsInLibrary(LibraryTab.Favorites);

            return [.. FilterLibraryState.Value.Entries
                .Where(e => e is LibraryEntrySavedFilter && e.IsFavorite)
                .Where(e => MatchesTagFilter(e, selected))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
        }
    }

    [Inject] private IFilePickerService FilePickerService { get; init; } = null!;

    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    [Inject] private IState<FilterLibraryState> FilterLibraryState { get; init; } = null!;

    private IReadOnlyList<LibraryEntry> PreviouslyUsedEntries
    {
        get
        {
            var selected = SelectedTagsInLibrary(LibraryTab.PreviouslyUsed);

            return [.. FilterLibraryState.Value.Entries
                .Where(e => e is { Origin: LibraryEntryOrigin.AutoTracked, IsFavorite: false })
                .Where(e => e.LastUsedUtc.HasValue)
                .Where(e => MatchesTagFilter(e, selected))
                .OrderByDescending(e => e.LastUsedUtc.GetValueOrDefault())
                .Take(50)];
        }
    }

    private IReadOnlyList<LibraryEntry> SavedEntries
    {
        get
        {
            var selected = SelectedTagsInLibrary(LibraryTab.Saved);

            return [.. FilterLibraryState.Value.Entries
                .Where(e => e is { Origin: LibraryEntryOrigin.UserSaved, IsFavorite: false })
                .Where(e => MatchesTagFilter(e, selected))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
        }
    }

    private IReadOnlyList<LibraryEntrySavedFilter> SavedFilterEntries =>
        [.. FilterLibraryState.Value.Entries.OfType<LibraryEntrySavedFilter>()];

    internal static void ApplyImportPreflight(
        ImportPreflight preflight,
        IFilterLibraryCommands commands,
        IReadOnlyList<LibraryEntry> existingEntries)
    {
        var knownIds = existingEntries.Select(e => e.Id).ToHashSet();

        foreach (var entry in preflight.ToAdd)
        {
            commands.AddEntry(PrepareEntryForAdd(entry, knownIds));
        }

        foreach (var (existing, incoming) in preflight.ToReplace)
        {
            var updated = incoming with
            {
                Id = existing.Id,
                IsFavorite = existing.IsFavorite,
                LastUsedUtc = existing.LastUsedUtc,
                Origin = existing.Origin,
                Tags = LibraryEntryTagNormalizer.Normalize(incoming.Tags),
            };

            commands.UpdateEntry(updated);
        }

        foreach (var group in preflight.ToUpdate.GroupBy(t => t.Existing.Id))
        {
            var existing = group.First().Existing;
            var existingMigrated = LibraryEntryTagNormalizer.MigrateBackslashName(existing);
            var incomingTags = group.SelectMany(t => t.Incoming.Tags);
            var unionedTags = LibraryEntryTagNormalizer.Normalize(existingMigrated.Tags.Concat(incomingTags));

            var updated = existing switch
            {
                LibraryEntrySavedFilter f => f with { Name = existingMigrated.Name, Tags = unionedTags },
                LibraryEntryFilterSet fs => fs with { Name = existingMigrated.Name, Tags = unionedTags },
                _ => existing,
            };

            commands.UpdateEntry(updated);
        }

        foreach (var ambiguous in preflight.AmbiguousMatches)
        {
            commands.AddEntry(PrepareEntryForAdd(ambiguous.Incoming, knownIds));
        }
    }

    internal static string BuildPreflightSummary(ImportPreflight preflight)
    {
        if (preflight.ImportBlocked)
        {
            var preview = string.Join("\n  \u2022 ", preflight.InvalidLegacyNames.Take(10));
            var more = preflight.InvalidLegacyNames.Count > 10
                ? $"\n  \u2022 ...and {preflight.InvalidLegacyNames.Count - 10} more"
                : string.Empty;

            return "This file contains entries with names that cannot be imported:\n  \u2022 " +
                preview + more;
        }

        var lines = new List<string>
        {
            $"  \u2022 {preflight.ToAdd.Count} new entries will be added",
        };

        if (preflight.ToReplace.Count > 0)
        {
            var conflictList = "\nNames being overwritten:\n  \u2022 " +
                string.Join("\n  \u2022 ", preflight.ToReplace.Select(p => p.Incoming.Name).Take(10)) +
                (preflight.ToReplace.Count > 10
                    ? $"\n  \u2022 ...and {preflight.ToReplace.Count - 10} more"
                    : string.Empty);

            lines.Add($"  \u2022 {preflight.ToReplace.Count} existing entries WILL BE OVERWRITTEN (current filter content will be lost){conflictList}");
        }

        if (preflight.ToUpdate.Count > 0)
        {
            var renameCount = preflight.ToUpdate.Count(t => t.Existing.Name.Contains('\\'));
            lines.Add($"  \u2022 {CountDistinctUpdates(preflight)} entries will be updated with tag changes");
            if (renameCount > 0)
            {
                lines.Add($"  \u2022 {renameCount} existing entries will also be renamed (folder paths \u2192 tags)");
            }
        }

        if (preflight.AmbiguousMatches.Count > 0)
        {
            lines.Add($"  \u2022 {preflight.AmbiguousMatches.Count} ambiguous entries will be imported as new");
        }

        lines.Add($"  \u2022 {preflight.SkippedDuplicates.Count} exact duplicates will be skipped");

        return "Import preview:\n" + string.Join('\n', lines);
    }

    internal static int CountDistinctUpdates(ImportPreflight preflight) =>
        preflight.ToUpdate.Select(t => t.Existing.Id).Distinct().Count();

    internal static (LibraryEntryId? TargetId, bool FallbackToActiveTab) DecidePendingFocusAfterRemoval(
        IReadOnlyList<LibraryEntry> snapshot,
        LibraryEntryId removedEntryId)
    {
        var idx = -1;

        for (int i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i].Id.Equals(removedEntryId)) { idx = i; break; }
        }

        if (idx < 0) { return (null, true); }

        if (idx + 1 < snapshot.Count) { return (snapshot[idx + 1].Id, false); }

        if (idx > 0) { return (snapshot[idx - 1].Id, false); }

        return (null, true);
    }

    internal static LibraryEntry PrepareEntryForAdd(LibraryEntry entry, ISet<LibraryEntryId> knownIds)
    {
        var id = entry.Id;

        while (!knownIds.Add(id))
        {
            id = new LibraryEntryId(Guid.CreateVersion7());
        }

        var tags = LibraryEntryTagNormalizer.Normalize(entry.Tags);

        return entry switch
        {
            LibraryEntrySavedFilter f => f with { Id = id, Tags = tags },
            LibraryEntryFilterSet fs => fs with { Id = id, Tags = tags },
            _ => entry,
        };
    }

    internal void RecordPendingFocusAfterRemoval(LibraryTab sourceTab, LibraryEntryId removedEntryId)
    {
        var sourceTabEntries = GetEntriesForTab(sourceTab);
        var (targetId, fallback) = DecidePendingFocusAfterRemoval(sourceTabEntries, removedEntryId);

        _pendingFocusSourceTab = sourceTab;
        _pendingFocusTargetEntryId = targetId;
        _pendingFocusToActiveTab = fallback;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        PruneStaleRowRefs();

        bool focused = false;

        if (_pendingFocusTargetEntryId is { } entryId && _pendingFocusSourceTab is { } sourceTab)
        {
            _pendingFocusTargetEntryId = null;
            _pendingFocusSourceTab = null;

            if (_rowRefs.TryGetValue((sourceTab, entryId), out var rowRef) && rowRef is not null)
            {
                focused = await rowRef.FocusMoreActionsButtonAsync();

                if (!focused) { _pendingFocusToActiveTab = true; }
            }
            else
            {
                _pendingFocusToActiveTab = true;
            }
        }

        if (!focused && _pendingFocusToActiveTab)
        {
            _pendingFocusToActiveTab = false;

            if (_sidebarTabsRef is not null)
            {
                await _sidebarTabsRef.FocusActiveTabAsync();
            }
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override async Task OnExportAsync()
    {
        try
        {
            var entries = FilterLibraryState.Value.Entries;
            var json = ExportService.Serialize(entries);
            var path = await FilePickerService.PickSaveAsync(
                "Export Filter Library",
                [".json"],
                suggestedFileName: $"filter-library-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");

            if (string.IsNullOrEmpty(path)) { return; }

            await File.WriteAllTextAsync(path, json);
            AnnouncementService.Announce($"Exported {entries.Count} entries");
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or SecurityException or
                PathTooLongException or DirectoryNotFoundException or IOException)
        {
            await ShowImportExportErrorAsync("Export failed", ex.Message);
        }
    }

    protected override async Task OnImportAsync()
    {
        string? path;
        string json;

        try
        {
            path = await FilePickerService.PickAsync("Import Filter Library", [".json"]);
            if (string.IsNullOrEmpty(path)) { return; }

            json = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or SecurityException or
                PathTooLongException or DirectoryNotFoundException or IOException)
        {
            await ShowImportExportErrorAsync("Import failed", ex.Message);
            return;
        }

        var preflight = ExportService.Deserialize(json, FilterLibraryState.Value.Entries);

        if (preflight.Error is not null)
        {
            await ShowImportExportErrorAsync("Import error", preflight.Error);
            return;
        }

        await PromptAndApplyImportAsync(preflight);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        SubscribeToAction<TagBulkUpdateFailedAction>(_ => RevertOptimisticTagSelection());

        if (InitialTab is { } tab)
        {
            _activeTab = tab;
        }

        if (!FilterLibraryState.Value.IsLoaded || FilterLibraryState.Value.LoadError)
        {
            FilterLibraryCommands.LoadLibrary();
        }
    }

    protected override Task<bool> OnRequestCloseAsync(ModalCloseRequest request)
    {
        if (request.Reason != ModalCloseReason.UserDismiss || !_justClearedTags)
        {
            return base.OnRequestCloseAsync(request);
        }

        _justClearedTags = false;

        return Task.FromResult(false);
    }

    private static bool MatchesTagFilter(LibraryEntry entry, ImmutableList<string> selectedTags) =>
        selectedTags.Count == 0 || selectedTags.All(t => entry.Tags.Contains(t, StringComparer.OrdinalIgnoreCase));

    private static string SanitizeForFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return sanitized.Length > 100 ? sanitized[..100] : sanitized;
    }

    private string GetEmptyStateMessage(LibraryTab tab)
    {
        if (SelectedTagsInLibrary(tab).Count > 0) { return "No entries match the selected tags."; }

        return tab switch
        {
            LibraryTab.Favorites => "No favorited filters or filter sets yet. Star an entry to add it here.",
            LibraryTab.PreviouslyUsed => "No filters have been applied recently.",
            _ => "No saved filters or filter sets yet. Use \"Save as Filter Set\" from the filter pane.",
        };
    }

    private IReadOnlyList<LibraryEntry> GetEntriesForTab(LibraryTab tab) => tab switch
    {
        LibraryTab.Favorites => FavoriteEntries,
        LibraryTab.PreviouslyUsed => PreviouslyUsedEntries,
        _ => SavedEntries,
    };

    private string GetTagManagementPanelId(LibraryTab tab) => $"{_tagManagementPanelId}-{tab}";

    private string GetTagOverflowRegionId(LibraryTab tab) => $"{_tagOverflowRegionId}-{tab}";

    private Task HandleAddToFilterSetAsync(AddToFilterSetIntent intent)
    {
        if (intent.FilterSetId is { } filterSetId)
        {
            FilterLibraryCommands.AddFilterToExistingFilterSet(filterSetId, intent.Filter, intent.SourceEntryId);
        }
        else if (!string.IsNullOrWhiteSpace(intent.NewFilterSetName))
        {
            FilterLibraryCommands.AddFilterToNewFilterSet(intent.NewFilterSetName!, intent.Filter, intent.SourceEntryId);
        }

        return Task.CompletedTask;
    }

    private async Task HandleApplyAsync(LibraryEntryId id)
    {
        FilterLibraryCommands.ApplyEntry(id);
        var entry = FilterLibraryState.Value.Entries.FirstOrDefault(e => e.Id.Equals(id));
        if (entry is not null) { AnnouncementService.Announce($"Applied {entry.Name}"); }
        await CompleteAsync(true);
    }

    private void HandleDelete(LibraryEntryId id) => FilterLibraryCommands.DeleteEntry(id);

    private async Task HandleExportEntryAsync(LibraryEntryId entryId)
    {
        var entry = FilterLibraryState.Value.Entries.FirstOrDefault(e => e.Id.Equals(entryId));
        if (entry is null) { return; }

        try
        {
            var json = ExportService.Serialize([entry]);
            var suggested = $"{SanitizeForFileName(entry.Name)}-{DateTimeOffset.Now:yyyyMMdd}.json";
            var path = await FilePickerService.PickSaveAsync(
                "Export Filter Entry",
                [".json"],
                suggestedFileName: suggested);

            if (string.IsNullOrEmpty(path)) { return; }

            await File.WriteAllTextAsync(path, json);
            AnnouncementService.Announce($"Exported '{entry.Name}'");
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or SecurityException or
                PathTooLongException or DirectoryNotFoundException or IOException)
        {
            await ShowImportExportErrorAsync("Export failed", ex.Message);
        }
    }

    private async Task HandleReplaceAsync(LibraryEntryId id)
    {
        FilterLibraryCommands.ReplaceWithEntry(id);
        var entry = FilterLibraryState.Value.Entries.FirstOrDefault(e => e.Id.Equals(id));
        if (entry is not null) { AnnouncementService.Announce($"Replaced filters with {entry.Name}"); }
        await CompleteAsync(true);
    }

    private Task HandleRequestPendingFocusAsync(LibraryTab sourceTab, LibraryEntryId entryId)
    {
        RecordPendingFocusAfterRemoval(sourceTab, entryId);

        return Task.CompletedTask;
    }

    private void HandleSaveToLibrary(LibraryEntryId id) => FilterLibraryCommands.SaveEntry(id);

    private void HandleTagDeleted(string name)
    {
        SnapshotSelectedTags();

        foreach (var tab in _selectedTagsByTab.Keys.ToList())
        {
            _selectedTagsByTab[tab] = _selectedTagsByTab[tab]
                .RemoveAll(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void HandleTagFilterBarKeyDown(KeyboardEventArgs e)
    {
        if (e.Key != "Escape") { return; }

        if (!_selectedTagsByTab.TryGetValue(_activeTab, out var current) || current.Count == 0) { return; }

        _selectedTagsByTab[_activeTab] = ImmutableList<string>.Empty;
        _justClearedTags = true;
        AnnouncementService.Announce("Tag filters cleared");
        StateHasChanged();
    }

    private void HandleTagRenamed((string OldName, string NewName) e)
    {
        SnapshotSelectedTags();

        foreach (var tab in _selectedTagsByTab.Keys.ToList())
        {
            var current = _selectedTagsByTab[tab];
            var index = current.IndexOf(e.OldName, StringComparer.OrdinalIgnoreCase);

            if (index < 0) { continue; }

            var without = current.RemoveAt(index);
            _selectedTagsByTab[tab] = without.Contains(e.NewName, StringComparer.OrdinalIgnoreCase)
                ? without
                : without.Insert(index, e.NewName);
        }
    }

    private void HandleToggleFavorite(FavoriteToggleIntent intent) =>
        FilterLibraryCommands.SetIsFavorite(intent.EntryId, intent.NewIsFavorite);

    private bool IsTabpanelEmpty(LibraryTab tab) => GetEntriesForTab(tab).Count == 0;

    private void OnRowDisposed((LibraryTab Tab, LibraryEntryId Id) key) => _rowRefs.Remove(key);

    private async Task PromptAndApplyImportAsync(ImportPreflight preflight)
    {
        if (preflight.ImportBlocked)
        {
            await ShowImportExportErrorAsync("Import blocked", BuildPreflightSummary(preflight));

            return;
        }

        var summary = BuildPreflightSummary(preflight);
        var request = new InlineAlertRequest(
            Title: "Confirm import",
            Message: summary,
            AcceptLabel: "Import",
            CancelLabel: "Cancel",
            IsPrompt: false,
            PromptInitialValue: null);

        InlineAlertResult result;

        try
        {
            result = await ((IInlineAlertHost)this).ShowInlineAlertAsync(request, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!result.Accepted) { return; }

        ApplyImportPreflight(preflight, FilterLibraryCommands, FilterLibraryState.Value.Entries);

        var ambiguousCount = preflight.AmbiguousMatches.Count;
        var updatedCount = CountDistinctUpdates(preflight);

        AnnouncementService.Announce(
            $"Imported {preflight.ToAdd.Count} new, " +
            $"replaced {preflight.ToReplace.Count}, " +
            $"updated {updatedCount} tags, " +
            $"skipped {preflight.SkippedDuplicates.Count}" +
            (ambiguousCount > 0 ? $", imported {ambiguousCount} ambiguous as new" : string.Empty));
    }

    private void PruneStaleRowRefs()
    {
        if (_rowRefs.Count == 0) { return; }

        var liveIds = FilterLibraryState.Value.Entries.Select(e => e.Id).ToHashSet();

        List<(LibraryTab Tab, LibraryEntryId Id)>? stale = null;

        foreach (var (key, value) in _rowRefs.ToList())
        {
            if (value is null || !liveIds.Contains(key.Id)) { (stale ??= []).Add(key); }
        }

        if (stale is null) { return; }

        foreach (var key in stale) { _rowRefs.Remove(key); }
    }

    private void RetryLoad() => FilterLibraryCommands.LoadLibrary();

    private void RevertOptimisticTagSelection()
    {
        if (_selectedTagsBeforeTagOp is null) { return; }

        foreach (var (tab, tags) in _selectedTagsBeforeTagOp)
        {
            _selectedTagsByTab[tab] = tags;
        }

        _selectedTagsBeforeTagOp = null;
        StateHasChanged();
    }

    private ImmutableList<string> SelectedTagsInLibrary(LibraryTab tab)
    {
        var available = FilterLibraryState.Value.Entries
            .SelectMany(e => e.Tags)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. _selectedTagsByTab[tab].Where(available.Contains)];
    }

    private async Task ShowImportExportErrorAsync(string title, string message)
    {
        var request = new InlineAlertRequest(
            Title: title,
            Message: message,
            AcceptLabel: null,
            CancelLabel: "OK",
            IsPrompt: false,
            PromptInitialValue: null);

        try
        {
            await ((IInlineAlertHost)this).ShowInlineAlertAsync(request, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SnapshotSelectedTags() => _selectedTagsBeforeTagOp = new(_selectedTagsByTab);

    private void ToggleTagFilter(string tag)
    {
        var current = _selectedTagsByTab[_activeTab];
        _selectedTagsByTab[_activeTab] = current.Contains(tag, StringComparer.OrdinalIgnoreCase)
            ? current.Remove(tag, StringComparer.OrdinalIgnoreCase)
            : current.Add(tag);

        StateHasChanged();
    }

    private void ToggleTagManagement()
    {
        _isTagManagementExpanded = !_isTagManagementExpanded;
    }

    private void ToggleTagOverflowExpanded()
    {
        _isTagOverflowExpanded = !_isTagOverflowExpanded;
    }
}
