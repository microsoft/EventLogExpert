// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class Effects(
    IFilterLibraryStore store,
    IState<FilterLibraryState> state,
    IState<FilterPaneState> filterPaneState,
    ILegacyFilterMigrator legacyMigrator,
    IBackslashNameMigrator backslashMigrator,
    IAnnouncementService announcementService,
    ITraceLogger logger)
{
    private const int MaxAutoTrackedRecents = 50;

    private readonly SemaphoreSlim _migrationGate = new(initialCount: 1, maxCount: 1);

    [EffectMethod]
    public Task HandleAddFilterToExistingFilterSet(AddFilterToExistingFilterSetAction action, IDispatcher dispatcher)
    {
        var filterSet = state.Value.Entries.OfType<LibraryEntryFilterSet>().FirstOrDefault(p => p.Id == action.FilterSetId);

        if (filterSet is null) { return Task.CompletedTask; }

        // Dedup tuple matches the SQL UNIQUE INDEX scope: (lower(ComparisonText), Mode, IsExcluded).
        // Distinct Mode values are intentional per the mode-drift policy.
        var addText = action.Filter.ComparisonText;
        var addMode = action.Filter.Mode;
        var addExcluded = action.Filter.IsExcluded;

        if (filterSet.Filters.Any(f =>
            string.Equals(f.ComparisonText, addText, StringComparison.OrdinalIgnoreCase) &&
            f.Mode == addMode &&
            f.IsExcluded == addExcluded))
        {
            PromoteSourceIfAutoTracked(action.SourceEntryId, dispatcher);
            return Task.CompletedTask;
        }

        var newFilter = action.Filter with { Id = FilterId.Create(), IsEnabled = false };
        var updatedFilterSet = filterSet with { Filters = filterSet.Filters.Add(newFilter) };

        try { store.Update(updatedFilterSet); }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Update (add to filter set) failed for {filterSet.Id}. {ex.Message}");

            return Task.CompletedTask;
        }

        dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(updatedFilterSet));
        PromoteSourceIfAutoTracked(action.SourceEntryId, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleAddFilterToNewFilterSet(AddFilterToNewFilterSetAction action, IDispatcher dispatcher)
    {
        if (string.IsNullOrWhiteSpace(action.NewFilterSetName)) { return Task.CompletedTask; }

        var created = new LibraryEntryFilterSet
        {
            Name = action.NewFilterSetName,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [action.Filter with { Id = FilterId.Create(), IsEnabled = false }],
            Origin = LibraryEntryOrigin.UserSaved,
        };

        try { store.Add(created); }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Add (new filter set) failed for {created.Id}. {ex.Message}");

            return Task.CompletedTask;
        }

        dispatcher.Dispatch(new AddLibraryEntrySuccessAction(created));
        PromoteSourceIfAutoTracked(action.SourceEntryId, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleAddLibraryEntry(AddLibraryEntryAction action, IDispatcher dispatcher)
    {
        try
        {
            store.Add(action.Entry);
            dispatcher.Dispatch(new AddLibraryEntrySuccessAction(action.Entry));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Add failed for entry {action.Entry.Id}. {ex.Message}");
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleApplyLibraryEntry(ApplyLibraryEntryAction action, IDispatcher dispatcher)
    {
        LibraryEntry? entry = state.Value.Entries.FirstOrDefault(e => e.Id == action.EntryId);

        if (entry is null) { return Task.CompletedTask; }

        ImmutableList<SavedFilter> filters = ExtractFilters(entry);

        dispatcher.Dispatch(new MergeFiltersAction(filters));
        dispatcher.Dispatch(new RecordEntryAppliedAction(action.EntryId));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleDeleteLibraryEntry(DeleteLibraryEntryAction action, IDispatcher dispatcher)
    {
        try
        {
            store.Delete(action.EntryId);
            dispatcher.Dispatch(new DeleteLibraryEntrySuccessAction(action.EntryId));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Delete failed for entry {action.EntryId}. {ex.Message}");
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleDeleteTag(DeleteTagAction action, IDispatcher dispatcher)
    {
        var normalized = LibraryEntryTagNormalizer.Normalize([action.Name]).FirstOrDefault();

        if (string.IsNullOrEmpty(normalized)) { return Task.CompletedTask; }

        var updatedEntries = new List<LibraryEntry>();

        foreach (var entry in state.Value.Entries)
        {
            var canonicalTags = LibraryEntryTagNormalizer.Normalize(entry.Tags);
            var index = canonicalTags.IndexOf(normalized);

            if (index < 0) { continue; }

            updatedEntries.Add(ReplaceTagsOnEntry(entry, canonicalTags.RemoveAt(index)));
        }

        ApplyBulkTagUpdate(updatedEntries, dispatcher, count => $"Removed tag '{normalized}' from {count} {EntriesWord(count)}");

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(LoadLibraryAction))]
    public async Task HandleLoadLibrary(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new LoadLibraryStartedAction());

        await _migrationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            var entries = store.LoadAll().ToImmutableList();

            if (legacyMigrator.ShouldRunMigration())
            {
                // AddRange-throws → keep returning currently-loaded entries + return without marking complete
                // (retries next launch). Post-AddRange LoadAll-throws → in-memory fallback + MarkMigrationCompleted
                // as on the happy path.
                var migrationResult = legacyMigrator.BuildEntriesFromLegacy();

                // Dedup against the already-loaded entries before AddRange — the per-section flag check in
                // BuildEntriesFromLegacy short-circuits already-completed sections, but on the bitmask-not-advanced
                // path (e.g., MarkMigrationCompleted SetString throws after a successful AddRange) the next launch
                // would re-read the still-present legacy keys and produce content-duplicate rows because migration
                // entries are Origin=UserSaved and the store's partial UNIQUE INDEX only covers AutoTracked rows.
                var entriesToAdd = DedupMigrationEntriesAgainstExisting(migrationResult.Entries, entries);

                if (entriesToAdd.Count > 0)
                {
                    try
                    {
                        store.AddRange(entriesToAdd);
                    }
                    catch (Exception ex)
                    {
                        logger.Warning($"FilterLibrary migration AddRange failed; legacy preserved. {ex.Message}");
                        dispatcher.Dispatch(new LoadLibrarySuccessAction(entries));

                        return;
                    }

                    try
                    {
                        entries = store.LoadAll().ToImmutableList();
                    }
                    catch (Exception ex)
                    {
                        logger.Warning($"FilterLibrary post-migration reload failed; using in-memory result. {ex.Message}");
                        entries = entries.AddRange(entriesToAdd);
                    }

                    logger.Information($"Migrated {entriesToAdd.Count} legacy entries to filter library (deduped from {migrationResult.Entries.Count}).");
                }

                // Not wrapped: a SetString failure surfaces via the outer catch (LoadLibraryFailure). On the next
                // launch ShouldRunMigration returns true again, BuildEntriesFromLegacy re-emits the same entries,
                // and DedupMigrationEntriesAgainstExisting filters them out against the now-non-empty store —
                // so the SetString-throws path is idempotent rather than duplicating.
                legacyMigrator.MarkMigrationCompleted(migrationResult.SuccessfulSections);
            }

            if (backslashMigrator.ShouldRunMigration())
            {
                var plan = backslashMigrator.BuildMigrationPlan(entries);

                if (plan.UpdatedEntries.Count > 0)
                {
                    int writeFailures = 0;

                    foreach (var updated in plan.UpdatedEntries)
                    {
                        try { store.Update(updated); }
                        catch (Exception ex)
                        {
                            writeFailures++;

                            logger.Warning($"FilterLibrary backslash migration Update failed for entry {updated.Id}: {ex.Message}");
                        }
                    }

                    if (writeFailures < plan.UpdatedEntries.Count)
                    {
                        try { entries = store.LoadAll().ToImmutableList(); }
                        catch (Exception ex)
                        {
                            logger.Warning($"FilterLibrary post-backslash-migration reload failed; using in-memory result. {ex.Message}");
                        }

                        logger.Information($"Backslash migration updated {plan.UpdatedEntries.Count - writeFailures}/{plan.UpdatedEntries.Count} entries.");
                    }

                    if (writeFailures == 0)
                    {
                        backslashMigrator.MarkMigrationCompleted();
                    }
                    else
                    {
                        logger.Warning($"FilterLibrary backslash migration: {writeFailures}/{plan.UpdatedEntries.Count} updates failed; will retry next launch.");
                    }
                }
                else
                {
                    backslashMigrator.MarkMigrationCompleted();
                }
            }

            dispatcher.Dispatch(new LoadLibrarySuccessAction(entries));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary load failed; rendering error state. {ex.Message}");
            dispatcher.Dispatch(new LoadLibraryFailureAction());
        }
        finally
        {
            _migrationGate.Release();
        }
    }

    [EffectMethod]
    public Task HandleRecordEntryApplied(RecordEntryAppliedAction action, IDispatcher dispatcher)
    {
        var entries = state.Value.Entries;
        var entry = entries.FirstOrDefault(e => e.Id == action.EntryId);

        if (entry is not LibraryEntrySavedFilter || entry.IsFavorite) { return Task.CompletedTask; }

        var nowUtc = DateTimeOffset.UtcNow;
        bool bumped;

        try { bumped = store.TryBumpLastUsedIfNotFavorite(entry.Id, nowUtc); }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary TryBumpLastUsed failed for {entry.Id}. {ex.Message}");

            return Task.CompletedTask;
        }

        if (!bumped) { return Task.CompletedTask; }

        LibraryEntry bumpedEntry = entry switch
        {
            LibraryEntrySavedFilter f => f with { LastUsedUtc = nowUtc },
            LibraryEntryFilterSet p => p with { LastUsedUtc = nowUtc },
            _ => entry,
        };

        var projected = entries.Replace(entry, bumpedEntry);
        dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(bumpedEntry));
        PruneFromSnapshot(projected, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleRecordFilterApplied(RecordFilterAppliedAction action, IDispatcher dispatcher)
    {
        var filter = action.Filter;

        if (string.IsNullOrWhiteSpace(filter.ComparisonText)) { return Task.CompletedTask; }

        var entries = state.Value.Entries;

        var existing = entries
            .OfType<LibraryEntrySavedFilter>()
            .FirstOrDefault(e =>
                string.Equals(e.Filter.ComparisonText, filter.ComparisonText, StringComparison.OrdinalIgnoreCase) &&
                e.Filter.Mode == filter.Mode &&
                e.Filter.IsExcluded == filter.IsExcluded);

        if (existing is not null)
        {
            if (existing.IsFavorite) { return Task.CompletedTask; }

            var nowUtc = DateTimeOffset.UtcNow;
            bool bumped;

            try { bumped = store.TryBumpLastUsedIfNotFavorite(existing.Id, nowUtc); }
            catch (Exception ex)
            {
                logger.Warning($"FilterLibrary TryBumpLastUsed failed for {existing.Id}. {ex.Message}");
                return Task.CompletedTask;
            }

            if (!bumped) { return Task.CompletedTask; }

            var bumpedEntry = existing with { LastUsedUtc = nowUtc };
            var projected = entries.Replace(existing, bumpedEntry);
            dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(bumpedEntry));
            PruneFromSnapshot(projected, dispatcher);

            return Task.CompletedTask;
        }

        var candidate = new LibraryEntrySavedFilter
        {
            Name = TruncateForDisplay(filter.ComparisonText),
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter with { IsEnabled = false },
            LastUsedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
        };

        (LibraryEntry Entry, bool WasInserted) result;

        try { result = store.AddOrReturnExistingFilter(candidate); }
        catch (Exception ex)
        {
            logger.Warning(
                $"FilterLibrary AddOrReturnExistingFilter failed for '{filter.ComparisonText}'. {ex.Message}");

            return Task.CompletedTask;
        }

        if (result.WasInserted)
        {
            var projected = entries.Add(candidate);
            dispatcher.Dispatch(new AddLibraryEntrySuccessAction(candidate));
            PruneFromSnapshot(projected, dispatcher);

            return Task.CompletedTask;
        }

        if (result.Entry.IsFavorite) { return Task.CompletedTask; }

        var collisionNowUtc = DateTimeOffset.UtcNow;
        bool collisionBumped;

        try { collisionBumped = store.TryBumpLastUsedIfNotFavorite(result.Entry.Id, collisionNowUtc); }
        catch (Exception ex)
        {
            logger.Warning(
                $"FilterLibrary TryBumpLastUsed (collision path) failed for {result.Entry.Id}. {ex.Message}");

            return Task.CompletedTask;
        }

        if (!collisionBumped) { return Task.CompletedTask; }

        var collisionEntry = (LibraryEntrySavedFilter)result.Entry with { LastUsedUtc = collisionNowUtc };
        var existingIndex = entries.FindIndex(e => e.Id == collisionEntry.Id);
        var collisionProjected = existingIndex >= 0
            ? entries.SetItem(existingIndex, collisionEntry)
            : entries.Add(collisionEntry);

        dispatcher.Dispatch(new AddLibraryEntrySuccessAction(collisionEntry));
        PruneFromSnapshot(collisionProjected, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleRenameTag(RenameTagAction action, IDispatcher dispatcher)
    {
        var oldNormalized = LibraryEntryTagNormalizer.Normalize([action.OldName]).FirstOrDefault();
        var newNormalized = LibraryEntryTagNormalizer.Normalize([action.NewName]).FirstOrDefault();

        if (string.IsNullOrEmpty(oldNormalized) ||
            string.IsNullOrEmpty(newNormalized) ||
            string.Equals(oldNormalized, newNormalized, StringComparison.Ordinal)) { return Task.CompletedTask; }

        var updatedEntries = new List<LibraryEntry>();

        foreach (var entry in state.Value.Entries)
        {
            var canonicalTags = LibraryEntryTagNormalizer.Normalize(entry.Tags);
            var oldIndex = canonicalTags.IndexOf(oldNormalized);

            if (oldIndex < 0) { continue; }

            var without = canonicalTags.RemoveAt(oldIndex);
            var updatedTags = without.Contains(newNormalized)
                ? without
                : without.Insert(oldIndex, newNormalized);

            updatedEntries.Add(ReplaceTagsOnEntry(entry, updatedTags));
        }

        ApplyBulkTagUpdate(updatedEntries, dispatcher, count => $"Renamed tag '{oldNormalized}' to '{newNormalized}' in {count} {EntriesWord(count)}");

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleReplaceWithLibraryEntry(ReplaceWithLibraryEntryAction action, IDispatcher dispatcher)
    {
        LibraryEntry? entry = state.Value.Entries.FirstOrDefault(e => e.Id == action.EntryId);

        if (entry is null) { return Task.CompletedTask; }

        ImmutableList<SavedFilter> filters = ExtractFilters(entry);

        dispatcher.Dispatch(new ReplaceFiltersAction(filters));
        dispatcher.Dispatch(new RecordEntryAppliedAction(action.EntryId));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSaveEntry(SaveEntryAction action, IDispatcher dispatcher)
    {
        var entry = state.Value.Entries.FirstOrDefault(e => e.Id == action.EntryId);

        if (entry is null || entry.Origin == LibraryEntryOrigin.UserSaved) { return Task.CompletedTask; }

        LibraryEntry promoted = entry switch
        {
            LibraryEntrySavedFilter f => f with { Origin = LibraryEntryOrigin.UserSaved },
            LibraryEntryFilterSet p => p with { Origin = LibraryEntryOrigin.UserSaved },
            _ => entry,
        };

        return PersistUpdateAsync(promoted, dispatcher);
    }

    [EffectMethod]
    public Task HandleSaveFilterSet(SaveFilterSetAction action, IDispatcher dispatcher)
    {
        if (string.IsNullOrWhiteSpace(action.Name) || action.Filters.IsEmpty) { return Task.CompletedTask; }

        var created = new LibraryEntryFilterSet
        {
            Name = action.Name,
            CreatedUtc = DateTimeOffset.UtcNow,
            // Regenerate FilterIds so Razor `@key=filter.Id` diffing stays correct when the
            // same pane filters are saved into multiple filter sets.
            Filters = [.. action.Filters.Select(f => f with { Id = FilterId.Create(), IsEnabled = false })],
            Origin = LibraryEntryOrigin.UserSaved,
        };

        return PersistAddAsync(created, dispatcher);
    }

    [EffectMethod]
    public Task HandleSavePaneAsFilterSet(SavePaneAsFilterSetAction action, IDispatcher dispatcher)
    {
        if (string.IsNullOrWhiteSpace(action.Name)) { return Task.CompletedTask; }

        var paneFilters = filterPaneState.Value.Filters;

        if (paneFilters.IsEmpty) { return Task.CompletedTask; }

        dispatcher.Dispatch(new SaveFilterSetAction(action.Name, paneFilters));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSetIsFavorite(SetIsFavoriteAction action, IDispatcher dispatcher)
    {
        var entry = state.Value.Entries.FirstOrDefault(e => e.Id == action.EntryId);

        if (entry is null || entry.IsFavorite == action.IsFavorite) { return Task.CompletedTask; }

        LibraryEntry updated;

        if (action.IsFavorite)
        {
            // Favoriting: mutex (LastUsedUtc=null) + promotion (Origin=UserSaved). Symmetric for filter + filter set.
            updated = entry switch
            {
                LibraryEntrySavedFilter f => f with
                {
                    IsFavorite = true,
                    LastUsedUtc = null,
                    Origin = LibraryEntryOrigin.UserSaved,
                },
                LibraryEntryFilterSet p => p with
                {
                    IsFavorite = true,
                    LastUsedUtc = null,
                    Origin = LibraryEntryOrigin.UserSaved,
                },
                _ => entry,
            };
        }
        else
        {
            // Unfavoriting: filters drop to Recents (matches legacy FilterCache UX); filter sets stay out of Recents.
            updated = entry switch
            {
                LibraryEntrySavedFilter f => f with
                {
                    IsFavorite = false,
                    LastUsedUtc = DateTimeOffset.UtcNow,
                },
                LibraryEntryFilterSet p => p with
                {
                    IsFavorite = false,
                },
                _ => entry,
            };
        }

        return PersistUpdateAsync(updated, dispatcher);
    }

    [EffectMethod]
    public Task HandleUpdateLibraryEntry(UpdateLibraryEntryAction action, IDispatcher dispatcher)
    {
        try
        {
            store.Update(action.Entry);
            dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(action.Entry));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Update failed for entry {action.Entry.Id}. {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static ImmutableList<LibraryEntry> DedupMigrationEntriesAgainstExisting(
        ImmutableList<LibraryEntry> candidates,
        ImmutableList<LibraryEntry> existing)
    {
        if (candidates.IsEmpty || existing.IsEmpty) { return candidates; }

        var existingSavedFilterKeys = new HashSet<string>(
            existing.OfType<LibraryEntrySavedFilter>()
                .Select(e => FilterLibraryDedupKeys.ForSavedFilter((LibraryEntrySavedFilter)LibraryEntryTagNormalizer.MigrateBackslashName(e))),
            StringComparer.Ordinal);

        var existingFilterSetKeys = new HashSet<string>(
            existing.OfType<LibraryEntryFilterSet>()
                .Select(e => FilterLibraryDedupKeys.ForFilterSet((LibraryEntryFilterSet)LibraryEntryTagNormalizer.MigrateBackslashName(e))),
            StringComparer.Ordinal);

        return [.. candidates.Where(entry => entry switch
        {
            LibraryEntrySavedFilter sf =>
                !existingSavedFilterKeys.Contains(FilterLibraryDedupKeys.ForSavedFilter(sf)),
            LibraryEntryFilterSet filterSet =>
                !existingFilterSetKeys.Contains(FilterLibraryDedupKeys.ForFilterSet(filterSet)),
            _ => true,
        })];
    }

    private static string EntriesWord(int count) => count == 1 ? "entry" : "entries";

    private static ImmutableList<SavedFilter> ExtractFilters(LibraryEntry entry) =>
        entry switch
        {
            LibraryEntrySavedFilter f => [f.Filter],
            LibraryEntryFilterSet p => p.Filters,
            _ => throw new InvalidOperationException($"Unhandled LibraryEntry type '{entry.GetType().FullName}'."),
        };

    private static LibraryEntry ReplaceTagsOnEntry(LibraryEntry entry, ImmutableList<string> tags) =>
        entry switch
        {
            LibraryEntrySavedFilter f => f with { Tags = tags },
            LibraryEntryFilterSet fs => fs with { Tags = tags },
            _ => entry,
        };

    private static string TruncateForDisplay(string text)
    {
        if (text.Length <= 80) { return text; }

        var cut = char.IsHighSurrogate(text[76]) ? 76 : 77;

        return text[..cut] + "...";
    }

    private void ApplyBulkTagUpdate(
        IReadOnlyList<LibraryEntry> updatedEntries,
        IDispatcher dispatcher,
        Func<int, string> buildAnnouncement)
    {
        if (updatedEntries.Count == 0) { return; }

        IReadOnlyList<LibraryEntryId> updatedIds;

        try
        {
            updatedIds = store.UpdateRange(updatedEntries);
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary bulk tag update failed; reloading library. {ex.Message}");
            announcementService.Announce("Couldn't update tags. The library was reloaded.");
            dispatcher.Dispatch(new LoadLibraryAction());
            dispatcher.Dispatch(new TagBulkUpdateFailedAction());

            return;
        }

        if (updatedIds.Count == 0)
        {
            announcementService.Announce("Couldn't update tags. The library was reloaded.");
            dispatcher.Dispatch(new LoadLibraryAction());
            dispatcher.Dispatch(new TagBulkUpdateFailedAction());

            return;
        }

        var updatedIdSet = updatedIds.ToHashSet();

        foreach (var entry in updatedEntries)
        {
            if (updatedIdSet.Contains(entry.Id))
            {
                dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(entry));
            }
        }

        if (updatedIds.Count < updatedEntries.Count)
        {
            dispatcher.Dispatch(new LoadLibraryAction());
        }

        announcementService.Announce(buildAnnouncement(updatedIds.Count));
    }

    private Task PersistAddAsync(LibraryEntry entry, IDispatcher dispatcher)
    {
        try { store.Add(entry); }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Add failed for {entry.Id}. {ex.Message}");
            return Task.CompletedTask;
        }

        dispatcher.Dispatch(new AddLibraryEntrySuccessAction(entry));

        return Task.CompletedTask;
    }

    private Task PersistUpdateAsync(LibraryEntry entry, IDispatcher dispatcher)
    {
        try { store.Update(entry); }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Update failed for {entry.Id}. {ex.Message}");

            return Task.CompletedTask;
        }

        dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(entry));

        return Task.CompletedTask;
    }

    private void PromoteSourceIfAutoTracked(LibraryEntryId? sourceEntryId, IDispatcher dispatcher)
    {
        if (sourceEntryId is not { } id) { return; }

        var source = state.Value.Entries.FirstOrDefault(e => e.Id == id);

        if (source is null || source.Origin == LibraryEntryOrigin.UserSaved) { return; }

        LibraryEntry promoted = source switch
        {
            LibraryEntrySavedFilter f => f with { Origin = LibraryEntryOrigin.UserSaved },
            LibraryEntryFilterSet p => p with { Origin = LibraryEntryOrigin.UserSaved },
            _ => source,
        };

        try { store.Update(promoted); }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Update (promote source) failed for {promoted.Id}. {ex.Message}");

            return;
        }

        dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(promoted));
    }

    private void PruneFromSnapshot(ImmutableList<LibraryEntry> snapshot, IDispatcher dispatcher)
    {
        var autoTrackedRecents = snapshot
            .OfType<LibraryEntrySavedFilter>()
            .Where(e => e is { Origin: LibraryEntryOrigin.AutoTracked, LastUsedUtc: not null })
            .ToList();

        if (autoTrackedRecents.Count <= MaxAutoTrackedRecents) { return; }

        // CreatedUtc tie-break keeps prune deterministic when entries share LastUsedUtc.
        var toDelete = autoTrackedRecents
            .OrderBy(e => e.LastUsedUtc!.Value)
            .ThenBy(e => e.CreatedUtc)
            .Take(autoTrackedRecents.Count - MaxAutoTrackedRecents)
            .ToList();

        foreach (var entry in toDelete)
        {
            // SQL guard no-ops the delete if a concurrent SetIsFavorite/SaveEntry promoted the row
            // (Origin=UserSaved or IsFavorite=true) after the snapshot was projected.
            bool deleted;

            try { deleted = store.TryDeleteAutoTrackedIfNotFavorite(entry.Id); }
            catch (Exception ex)
            {
                logger.Warning($"FilterLibrary TryDeleteAutoTrackedIfNotFavorite (prune) failed for {entry.Id}. {ex.Message}");

                continue;
            }

            if (!deleted) { continue; }

            dispatcher.Dispatch(new DeleteLibraryEntrySuccessAction(entry.Id));
        }
    }
}
