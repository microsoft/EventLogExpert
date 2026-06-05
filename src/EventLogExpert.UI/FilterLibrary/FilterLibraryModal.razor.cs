// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.Modal;
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

    private readonly string _tagOverflowRegionId = $"tag-overflow-{Guid.NewGuid():N}";

    private LibraryTab _activeTab = LibraryTab.Saved;
    private bool _isTagOverflowExpanded;
    private bool _justClearedTags;
    private LibraryTab? _pendingFocusSourceTab;
    private LibraryEntryId? _pendingFocusTargetEntryId;
    private bool _pendingFocusToActiveTab;
    private SidebarTabs<LibraryTab>? _sidebarTabsRef;

    [Parameter] public LibraryTab? InitialTab { get; set; }

    private IReadOnlyList<LibraryEntryFilterSet> AllFilterSets =>
        [.. FilterLibraryState.Value.Entries
            .OfType<LibraryEntryFilterSet>()
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];

    private IReadOnlyList<string> AllLibraryTags =>
        [.. FilterLibraryState.Value.Entries
            .SelectMany(e => e.Tags)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)];

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    private IReadOnlyList<(LibraryTab Tab, string Label)> CurrentTabLabels =>
    [
        (LibraryTab.Saved, $"Saved ({SavedEntries.Count})"),
        (LibraryTab.Favorites, $"Favorites ({FavoriteEntries.Count})"),
        (LibraryTab.PreviouslyUsed, $"Previously Used ({PreviouslyUsedEntries.Count})"),
    ];

    [Inject] private IFilterLibraryExportService ExportService { get; init; } = null!;

    private IReadOnlyList<LibraryEntry> FavoriteEntries =>
        [.. FilterLibraryState.Value.Entries
            .Where(e => e is LibraryEntrySavedFilter && e.IsFavorite)
            .Where(e => MatchesTagFilter(e, _selectedTagsByTab[LibraryTab.Favorites]))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];

    [Inject] private IFilePickerService FilePickerService { get; init; } = null!;

    [Inject] private IFilterLibraryCommands FilterLibraryCommands { get; init; } = null!;

    [Inject] private IState<FilterLibraryState> FilterLibraryState { get; init; } = null!;

    private IReadOnlyList<LibraryEntry> PreviouslyUsedEntries =>
        [.. FilterLibraryState.Value.Entries
            .Where(e => e.Origin == LibraryEntryOrigin.AutoTracked && !e.IsFavorite)
            .Where(e => MatchesTagFilter(e, _selectedTagsByTab[LibraryTab.PreviouslyUsed]))
            .OrderByDescending(e => e.LastUsedUtc!.Value)
            .Take(50)];

    private IReadOnlyList<LibraryEntry> SavedEntries =>
        [.. FilterLibraryState.Value.Entries
            .Where(e => e.Origin == LibraryEntryOrigin.UserSaved && !e.IsFavorite)
            .Where(e => MatchesTagFilter(e, _selectedTagsByTab[LibraryTab.Saved]))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];

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
            lines.Add($"  \u2022 {preflight.ToUpdate.Count} entries will be updated with tag changes");
            if (renameCount > 0)
            {
                lines.Add($"  \u2022 {renameCount} existing entries will also be renamed (folder paths \u2192 tags)");
            }
        }

        if (preflight.AmbiguousMatches.Count > 0)
        {
            lines.Add($"  \u2022 {preflight.AmbiguousMatches.Count} entries require manual conflict resolution");
        }

        lines.Add($"  \u2022 {preflight.SkippedDuplicates.Count} exact duplicates will be skipped");

        return "Import preview:\n" + string.Join('\n', lines);
    }

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
        selectedTags.Count == 0 || selectedTags.All(t => entry.Tags.Contains(t, StringComparer.Ordinal));

    private static string SanitizeForFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return sanitized.Length > 100 ? sanitized[..100] : sanitized;
    }

    private string GetEmptyStateMessage(LibraryTab tab)
    {
        if (_selectedTagsByTab[tab].Count > 0) { return "No entries match the selected tags."; }

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

    private void HandleTagFilterBarKeyDown(KeyboardEventArgs e)
    {
        if (e.Key != "Escape") { return; }

        if (!_selectedTagsByTab.TryGetValue(_activeTab, out var current) || current.Count == 0) { return; }

        _selectedTagsByTab[_activeTab] = ImmutableList<string>.Empty;
        _justClearedTags = true;
        AnnouncementService.Announce("Tag filters cleared");
        StateHasChanged();
    }

    private void HandleToggleFavorite(FavoriteToggleIntent intent) =>
        FilterLibraryCommands.SetIsFavorite(intent.EntryId, intent.NewIsFavorite);

    private bool IsTabpanelEmpty(LibraryTab tab) => GetEntriesForTab(tab).Count == 0;

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

        foreach (var entry in preflight.ToAdd)
        {
            FilterLibraryCommands.AddEntry(entry);
        }

        foreach (var (existing, incoming) in preflight.ToReplace)
        {
            FilterLibraryCommands.UpdateEntry(incoming with { Id = existing.Id });
        }

        foreach (var (existing, incoming) in preflight.ToUpdate)
        {
            var existingMigrated = LibraryEntryTagNormalizer.MigrateBackslashName(existing);
            var unionedTags = LibraryEntryTagNormalizer.Normalize(existingMigrated.Tags.Concat(incoming.Tags));

            var updated = existing switch
            {
                LibraryEntrySavedFilter f => f with { Name = existingMigrated.Name, Tags = unionedTags },
                LibraryEntryFilterSet fs => fs with { Name = existingMigrated.Name, Tags = unionedTags },
                _ => existing,
            };

            FilterLibraryCommands.UpdateEntry(updated);
        }

        var ambiguousCount = preflight.AmbiguousMatches.Count;

        AnnouncementService.Announce(
            $"Imported {preflight.ToAdd.Count} new, " +
            $"replaced {preflight.ToReplace.Count}, " +
            $"updated {preflight.ToUpdate.Count} tags, " +
            $"skipped {preflight.SkippedDuplicates.Count}" +
            (ambiguousCount > 0 ? $" ({ambiguousCount} require manual resolution)" : string.Empty));
    }

    private void PruneStaleRowRefs()
    {
        if (_rowRefs.Count == 0) { return; }

        var liveByTab = new Dictionary<LibraryTab, HashSet<LibraryEntryId>>
        {
            [LibraryTab.Saved] = [.. SavedEntries.Select(e => e.Id)],
            [LibraryTab.Favorites] = [.. FavoriteEntries.Select(e => e.Id)],
            [LibraryTab.PreviouslyUsed] = [.. PreviouslyUsedEntries.Select(e => e.Id)],
        };

        var stale = _rowRefs.Keys
            .Where(key => !liveByTab[key.Tab].Contains(key.Id))
            .ToList();

        foreach (var key in stale) { _rowRefs.Remove(key); }
    }

    private void RetryLoad() => FilterLibraryCommands.LoadLibrary();

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

    private void ToggleTagFilter(string tag)
    {
        var current = _selectedTagsByTab[_activeTab];
        _selectedTagsByTab[_activeTab] = current.Contains(tag, StringComparer.Ordinal)
            ? current.Remove(tag, StringComparer.Ordinal)
            : current.Add(tag);

        StateHasChanged();
    }

    private void ToggleTagOverflowExpanded()
    {
        _isTagOverflowExpanded = !_isTagOverflowExpanded;
    }
}
