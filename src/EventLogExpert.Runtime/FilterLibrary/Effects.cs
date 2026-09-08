// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
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
    IErrorBannerService errorBannerService,
    ITraceLogger logger,
    TagBulkUpdateFailedNotifier tagBulkUpdateFailedNotifier)
{
    private const int MaxAutoTrackedRecents = 50;

    private readonly SemaphoreSlim _writeGate = new(initialCount: 1, maxCount: 1);

    [EffectMethod]
    public async Task HandleAddFilterToExistingFilterSet(AddFilterToExistingFilterSetAction action, IDispatcher dispatcher)
    {
        var filterSet = state.Value.Entries.OfType<LibraryEntryFilterSet>().FirstOrDefault(p => p.Id == action.FilterSetId);

        if (filterSet is null) { return; }

        var newFilter = action.Filter with { Id = FilterId.Create(), IsEnabled = false };

        await PersistAndDispatchAsync(
            filterSet.Id,
            e => AppendFilterToSetIfMissing(e, newFilter),
            dispatcher,
            new FilterAddToSetFailed()).ConfigureAwait(false);
        await PromoteSourceIfAutoTracked(action.SourceEntryId, dispatcher).ConfigureAwait(false);
    }

    [EffectMethod]
    public async Task HandleAddFilterToNewFilterSet(AddFilterToNewFilterSetAction action, IDispatcher dispatcher)
    {
        if (string.IsNullOrWhiteSpace(action.NewFilterSetName)) { return; }

        var created = new LibraryEntryFilterSet
        {
            Name = action.NewFilterSetName,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [action.Filter with { Id = FilterId.Create(), IsEnabled = false }],
            Origin = LibraryEntryOrigin.UserSaved,
        };

        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            try { await store.AddAsync(created).ConfigureAwait(false); }
            catch (Exception ex)
            {
                logger.Warning($"FilterLibrary Add (new filter set) failed for {created.Id}. {ex.Message}");
                errorBannerService.ReportError(new FilterSetCreateFailed(created.Name));

                return;
            }

            dispatcher.Dispatch(new AddLibraryEntrySuccessAction(created));
        }
        finally
        {
            _writeGate.Release();
        }

        await PromoteSourceIfAutoTracked(action.SourceEntryId, dispatcher).ConfigureAwait(false);
    }

    [EffectMethod]
    public async Task HandleAddLibraryEntry(AddLibraryEntryAction action, IDispatcher dispatcher)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            await store.AddAsync(action.Entry).ConfigureAwait(false);

            dispatcher.Dispatch(new AddLibraryEntrySuccessAction(action.Entry));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Add failed for entry {action.Entry.Id}. {ex.Message}");
            errorBannerService.ReportError(new LibraryEntrySaveFailed(action.Entry.Name));
        }
        finally
        {
            _writeGate.Release();
        }
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
    public async Task HandleDeleteLibraryEntry(DeleteLibraryEntryAction action, IDispatcher dispatcher)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            await store.DeleteAsync(action.EntryId).ConfigureAwait(false);

            dispatcher.Dispatch(new DeleteLibraryEntrySuccessAction(action.EntryId));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Delete failed for entry {action.EntryId}. {ex.Message}");
            errorBannerService.ReportError(new LibraryEntryDeleteFailed());
        }
        finally
        {
            _writeGate.Release();
        }
    }

    [EffectMethod]
    public async Task HandleDeleteTag(DeleteTagAction action, IDispatcher dispatcher)
    {
        var normalized = LibraryEntryTagNormalizer.Normalize([action.Name]).FirstOrDefault();

        if (string.IsNullOrEmpty(normalized)) { return; }

        var updatedEntries = new List<LibraryEntry>();

        foreach (var entry in state.Value.Entries)
        {
            var canonicalTags = LibraryEntryTagNormalizer.Normalize(entry.Tags);
            var index = canonicalTags.IndexOf(normalized);

            if (index < 0) { continue; }

            updatedEntries.Add(ReplaceTagsOnEntry(entry, canonicalTags.RemoveAt(index)));
        }

        await ApplyBulkTagUpdate(updatedEntries, dispatcher, count => $"Removed tag '{normalized}' from {count} {EntriesWord(count)}").ConfigureAwait(false);
    }

    [EffectMethod]
    public async Task HandleImportLibraryEntries(ImportLibraryEntriesAction action, IDispatcher dispatcher)
    {
        if (action.ToAdd.IsEmpty && action.ToUpdate.IsEmpty)
        {
            announcementService.Announce(FormatImportSummary(action.Summary));

            return;
        }

        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            IReadOnlyList<LibraryEntryId> updatedIds;

            try
            {
                await store.AddRangeAsync(action.ToAdd).ConfigureAwait(false);
                updatedIds = await store.UpdateRangeAsync(action.ToUpdate).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warning($"FilterLibrary import failed; reloading library. {ex.Message}");
                errorBannerService.ReportError(new FilterLibraryImportFailed());
                dispatcher.Dispatch(new LoadLibraryAction());

                return;
            }

            foreach (var entry in action.ToAdd)
            {
                dispatcher.Dispatch(new AddLibraryEntrySuccessAction(entry));
            }

            var updatedIdSet = updatedIds.ToHashSet();

            // The store skips rows that vanished between preflight and commit (a concurrent delete), so the persisted
            // set can be smaller than the preflight totals. Re-derive the replaced / tag-update counts from what
            // actually persisted so the announcement never claims more than happened. action.ToUpdate is the
            // replacement entries (Summary.Replaced of them) followed by the tag-update entries - see
            // FilterLibraryModal.ApplyImportPreflight, whose ordering this boundary relies on.
            var persistedReplaced = 0;
            var persistedUpdatedTags = 0;
            var updateIndex = 0;

            foreach (var entry in action.ToUpdate)
            {
                if (updatedIdSet.Contains(entry.Id))
                {
                    dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(entry));

                    if (updateIndex < action.Summary.Replaced) { persistedReplaced++; }
                    else { persistedUpdatedTags++; }
                }

                updateIndex++;
            }

            if (action.ToUpdate.Count > 0 && updatedIds.Count < action.ToUpdate.Count)
            {
                dispatcher.Dispatch(new LoadLibraryAction());
            }

            var persistedSummary = action.Summary with { Replaced = persistedReplaced, UpdatedTags = persistedUpdatedTags };

            announcementService.Announce(FormatImportSummary(persistedSummary));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    [EffectMethod(typeof(LoadLibraryAction))]
    public async Task HandleLoadLibrary(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new LoadLibraryStartedAction());

        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            var entries = (await store.LoadAllAsync().ConfigureAwait(false)).ToImmutableList();

            if (legacyMigrator.ShouldRunMigration())
            {
                var migrationResult = legacyMigrator.BuildEntriesFromLegacy();

                var entriesToAdd = DedupMigrationEntriesAgainstExisting(migrationResult.Entries, entries);

                if (entriesToAdd.Count > 0)
                {
                    try
                    {
                        await store.AddRangeAsync(entriesToAdd).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.Warning($"FilterLibrary migration AddRange failed; legacy preserved. {ex.Message}");
                        dispatcher.Dispatch(new LoadLibrarySuccessAction(entries));

                        return;
                    }

                    try
                    {
                        entries = (await store.LoadAllAsync().ConfigureAwait(false)).ToImmutableList();
                    }
                    catch (Exception ex)
                    {
                        logger.Warning($"FilterLibrary post-migration reload failed; using in-memory result. {ex.Message}");
                        entries = entries.AddRange(entriesToAdd);
                    }

                    logger.Information($"Migrated {entriesToAdd.Count} legacy entries to filter library (deduped from {migrationResult.Entries.Count}).");
                }

                // launch ShouldRunMigration returns true again, BuildEntriesFromLegacy re-emits the same entries,
                // and DedupMigrationEntriesAgainstExisting filters them out against the now-non-empty store -
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
                        try { await store.UpdateAsync(updated).ConfigureAwait(false); }
                        catch (Exception ex)
                        {
                            writeFailures++;

                            logger.Warning($"FilterLibrary backslash migration Update failed for entry {updated.Id}: {ex.Message}");
                        }
                    }

                    if (writeFailures < plan.UpdatedEntries.Count)
                    {
                        try { entries = (await store.LoadAllAsync().ConfigureAwait(false)).ToImmutableList(); }
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
            _writeGate.Release();
        }
    }

    [EffectMethod]
    public async Task HandleRecordEntryApplied(RecordEntryAppliedAction action, IDispatcher dispatcher)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            var entries = state.Value.Entries;
            var entry = entries.FirstOrDefault(e => e.Id == action.EntryId);

            if (entry is not LibraryEntrySavedFilter || entry.IsFavorite) { return; }

            var nowUtc = DateTimeOffset.UtcNow;
            bool bumped;

            try { bumped = await store.TryBumpLastUsedIfNotFavoriteAsync(entry.Id, nowUtc).ConfigureAwait(false); }
            catch (Exception ex)
            {
                logger.Warning($"FilterLibrary TryBumpLastUsed failed for {entry.Id}. {ex.Message}");

                return;
            }

            if (!bumped) { return; }

            var latestEntries = state.Value.Entries;
            var latest = latestEntries.FirstOrDefault(e => e.Id == action.EntryId);

            if (latest is null) { return; }

            LibraryEntry bumpedEntry = ApplyLastUsedBump(latest, nowUtc);
            var projected = latestEntries.Replace(latest, bumpedEntry);
            dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(bumpedEntry));
            await PruneFromSnapshot(projected, dispatcher).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    [EffectMethod]
    public async Task HandleRecordFilterApplied(RecordFilterAppliedAction action, IDispatcher dispatcher)
    {
        var filter = action.Filter;

        if (string.IsNullOrWhiteSpace(filter.ComparisonText)) { return; }

        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            var entries = state.Value.Entries;

            var existing = entries
                .OfType<LibraryEntrySavedFilter>()
                .FirstOrDefault(e =>
                    string.Equals(e.Filter.ComparisonText, filter.ComparisonText, StringComparison.OrdinalIgnoreCase) &&
                    e.Filter.Mode == filter.Mode &&
                    e.Filter.IsExcluded == filter.IsExcluded);

            if (existing is not null)
            {
                if (existing.IsFavorite) { return; }

                var nowUtc = DateTimeOffset.UtcNow;
                bool bumped;

                try { bumped = await store.TryBumpLastUsedIfNotFavoriteAsync(existing.Id, nowUtc).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    logger.Warning($"FilterLibrary TryBumpLastUsed failed for {existing.Id}. {ex.Message}");
                    return;
                }

                if (!bumped) { return; }

                var latestEntries = state.Value.Entries;
                var latestExisting = latestEntries.FirstOrDefault(e => e.Id == existing.Id) as LibraryEntrySavedFilter;

                if (latestExisting is null) { return; }

                var bumpedEntry = latestExisting with { LastUsedUtc = nowUtc };
                var projected = latestEntries.Replace(latestExisting, bumpedEntry);
                dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(bumpedEntry));
                await PruneFromSnapshot(projected, dispatcher).ConfigureAwait(false);

                return;
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

            try { result = await store.AddOrReturnExistingFilterAsync(candidate).ConfigureAwait(false); }
            catch (Exception ex)
            {
                logger.Warning(
                    $"FilterLibrary AddOrReturnExistingFilter failed for '{filter.ComparisonText}'. {ex.Message}");

                return;
            }

            if (result.WasInserted)
            {
                var projected = entries.Add(candidate);
                dispatcher.Dispatch(new AddLibraryEntrySuccessAction(candidate));
                await PruneFromSnapshot(projected, dispatcher).ConfigureAwait(false);

                return;
            }

            if (result.Entry.IsFavorite) { return; }

            var collisionNowUtc = DateTimeOffset.UtcNow;
            bool collisionBumped;

            try { collisionBumped = await store.TryBumpLastUsedIfNotFavoriteAsync(result.Entry.Id, collisionNowUtc).ConfigureAwait(false); }
            catch (Exception ex)
            {
                logger.Warning(
                    $"FilterLibrary TryBumpLastUsed (collision path) failed for {result.Entry.Id}. {ex.Message}");

                return;
            }

            if (!collisionBumped) { return; }

            var collisionLatestEntries = state.Value.Entries;
            var collisionLatest = collisionLatestEntries.FirstOrDefault(e => e.Id == result.Entry.Id) as LibraryEntrySavedFilter
                ?? (LibraryEntrySavedFilter)result.Entry;
            var collisionEntry = collisionLatest with { LastUsedUtc = collisionNowUtc };
            var existingIndex = collisionLatestEntries.FindIndex(e => e.Id == collisionEntry.Id);
            var collisionProjected = existingIndex >= 0
                ? collisionLatestEntries.SetItem(existingIndex, collisionEntry)
                : collisionLatestEntries.Add(collisionEntry);

            dispatcher.Dispatch(new AddLibraryEntrySuccessAction(collisionEntry));

            await PruneFromSnapshot(collisionProjected, dispatcher).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    [EffectMethod]
    public async Task HandleRenameTag(RenameTagAction action, IDispatcher dispatcher)
    {
        var oldNormalized = LibraryEntryTagNormalizer.Normalize([action.OldName]).FirstOrDefault();
        var newNormalized = LibraryEntryTagNormalizer.Normalize([action.NewName]).FirstOrDefault();

        if (string.IsNullOrEmpty(oldNormalized) ||
            string.IsNullOrEmpty(newNormalized) ||
            string.Equals(oldNormalized, newNormalized, StringComparison.Ordinal)) { return; }

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

        await ApplyBulkTagUpdate(updatedEntries, dispatcher, count => $"Renamed tag '{oldNormalized}' to '{newNormalized}' in {count} {EntriesWord(count)}").ConfigureAwait(false);
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

        return PersistAndDispatchAsync(
            action.EntryId,
            PromoteOriginToUserSaved,
            dispatcher,
            new LibraryEntryPromoteFailed());
    }

    [EffectMethod]
    public async Task HandleSaveFilterSet(SaveFilterSetAction action, IDispatcher dispatcher)
    {
        if (string.IsNullOrWhiteSpace(action.Name) || action.Filters.IsEmpty) { return; }

        var created = new LibraryEntryFilterSet
        {
            Name = action.Name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [.. action.Filters.Select(f => f with { Id = FilterId.Create(), IsEnabled = false })],
            Origin = LibraryEntryOrigin.UserSaved,
        };

        if (await PersistAddAsync(created, dispatcher).ConfigureAwait(false))
        {
            dispatcher.Dispatch(new SaveFilterSetSucceededAction(created.Name, action.Origin, action.LensesToClearOnSuccess));

            return;
        }

        // The write failed and was already logged in PersistAddAsync. Surface it visibly: the error banner is
        // role="alert", so it also reaches screen readers - do not also announce, or it would be spoken twice.
        errorBannerService.ReportError(new FilterSetSaveFailed(created.Name));
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
    public Task HandleSetEntryName(SetEntryNameAction action, IDispatcher dispatcher)
    {
        var newName = action.Name;

        return PersistAndDispatchAsync(
            action.EntryId,
            e => ApplyName(e, newName),
            dispatcher,
            new LibraryEntryRenameFailed());
    }

    [EffectMethod]
    public Task HandleSetEntryTags(SetEntryTagsAction action, IDispatcher dispatcher)
    {
        var newTags = action.Tags;

        return PersistAndDispatchAsync(
            action.EntryId,
            e => ApplyTags(e, newTags),
            dispatcher,
            new LibraryEntryTagsSaveFailed());
    }

    [EffectMethod]
    public Task HandleSetFilterSetFilters(SetFilterSetFiltersAction action, IDispatcher dispatcher)
    {
        var newFilters = action.Filters;

        return PersistAndDispatchAsync(
            action.FilterSetId,
            e => ApplyFilters(e, newFilters),
            dispatcher,
            new FilterSetUpdateFailed());
    }

    [EffectMethod]
    public Task HandleSetIsFavorite(SetIsFavoriteAction action, IDispatcher dispatcher)
    {
        var entry = state.Value.Entries.FirstOrDefault(e => e.Id == action.EntryId);

        if (entry is null || entry.IsFavorite == action.IsFavorite) { return Task.CompletedTask; }

        var setIsFavorite = action.IsFavorite;
        var unfavoriteTimestamp = DateTimeOffset.UtcNow;

        return PersistAndDispatchAsync(
            action.EntryId,
            e => ApplyFavoriteToggle(e, setIsFavorite, unfavoriteTimestamp),
            dispatcher,
            new LibraryEntryFavoriteFailed());
    }

    [EffectMethod(typeof(TagBulkUpdateFailedAction))]
    public Task HandleTagBulkUpdateFailed(IDispatcher dispatcher)
    {
        tagBulkUpdateFailedNotifier.Raise();

        return Task.CompletedTask;
    }

    [EffectMethod]
    public async Task HandleUpdateLibraryEntry(UpdateLibraryEntryAction action, IDispatcher dispatcher)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            await store.UpdateAsync(action.Entry).ConfigureAwait(false);

            dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(action.Entry));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Update failed for entry {action.Entry.Id}. {ex.Message}");
            errorBannerService.ReportError(new LibraryEntryUpdateFailed(action.Entry.Name));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static LibraryEntry AppendFilterToSetIfMissing(LibraryEntry entry, SavedFilter newFilter)
    {
        if (entry is not LibraryEntryFilterSet filterSet || filterSet.Filters.Any(f =>
            string.Equals(f.ComparisonText, newFilter.ComparisonText, StringComparison.OrdinalIgnoreCase) &&
            f.Mode == newFilter.Mode &&
            f.IsExcluded == newFilter.IsExcluded)) { return entry; }

        return filterSet with { Filters = filterSet.Filters.Add(newFilter) };
    }

    private static LibraryEntry ApplyFavoriteToggle(LibraryEntry entry, bool isFavorite, DateTimeOffset unfavoriteTimestamp)
    {
        if (isFavorite)
        {
            return entry switch
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

        return entry switch
        {
            LibraryEntrySavedFilter f => f with
            {
                IsFavorite = false,
                LastUsedUtc = unfavoriteTimestamp,
            },
            LibraryEntryFilterSet p => p with
            {
                IsFavorite = false,
            },
            _ => entry,
        };
    }

    private static LibraryEntry ApplyFilters(LibraryEntry entry, ImmutableList<SavedFilter> newFilters) => entry switch
    {
        LibraryEntryFilterSet p => p with { Filters = newFilters },
        _ => entry,
    };

    private static LibraryEntry ApplyLastUsedBump(LibraryEntry entry, DateTimeOffset nowUtc) => entry switch
    {
        LibraryEntrySavedFilter f => f with { LastUsedUtc = nowUtc },
        LibraryEntryFilterSet p => p with { LastUsedUtc = nowUtc },
        _ => entry,
    };

    private static LibraryEntry ApplyName(LibraryEntry entry, string newName) => entry switch
    {
        LibraryEntrySavedFilter f => f with { Name = newName },
        LibraryEntryFilterSet p => p with { Name = newName },
        _ => entry,
    };

    private static LibraryEntry ApplyTags(LibraryEntry entry, ImmutableList<string> newTags) => entry switch
    {
        LibraryEntrySavedFilter f => f with { Tags = newTags },
        LibraryEntryFilterSet p => p with { Tags = newTags },
        _ => entry,
    };

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

    private static void DispatchUpdateWithLatestSnapshot(
        LibraryEntryId id,
        Func<LibraryEntry, LibraryEntry> mutate,
        IDispatcher dispatcher,
        ImmutableList<LibraryEntry> latestSnapshot)
    {
        var latest = latestSnapshot.FirstOrDefault(e => e.Id == id);

        if (latest is null) { return; }

        dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(mutate(latest)));
    }

    private static string EntriesWord(int count) => count == 1 ? "entry" : "entries";

    private static ImmutableList<SavedFilter> ExtractFilters(LibraryEntry entry) =>
        entry switch
        {
            LibraryEntrySavedFilter f => [f.Filter],
            LibraryEntryFilterSet p => p.Filters,
            _ => throw new InvalidOperationException($"Unhandled LibraryEntry type '{entry.GetType().FullName}'."),
        };

    private static string FormatImportSummary(ImportSummary summary) =>
        $"Imported {summary.Added} new, replaced {summary.Replaced}, updated {summary.UpdatedTags} tags, skipped {summary.Skipped}" +
        (summary.Ambiguous > 0 ? $", imported {summary.Ambiguous} ambiguous as new" : string.Empty);

    private static bool NonTagFieldsDiffer(LibraryEntry latest, LibraryEntry bulkEntry) => (latest, bulkEntry) switch
    {
        (LibraryEntrySavedFilter latestFilter, LibraryEntrySavedFilter bulkFilter) =>
            !Equals(latestFilter with { Tags = bulkFilter.Tags }, bulkFilter),
        (LibraryEntryFilterSet latestSet, LibraryEntryFilterSet bulkSet) =>
            !Equals(latestSet with { Tags = bulkSet.Tags }, bulkSet),
        _ => true,
    };

    private static LibraryEntry PromoteOriginToUserSaved(LibraryEntry entry) => entry switch
    {
        LibraryEntrySavedFilter f => f with { Origin = LibraryEntryOrigin.UserSaved },
        LibraryEntryFilterSet p => p with { Origin = LibraryEntryOrigin.UserSaved },
        _ => entry,
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

    private async Task ApplyBulkTagUpdate(
        IReadOnlyList<LibraryEntry> updatedEntries,
        IDispatcher dispatcher,
        Func<int, string> buildAnnouncement)
    {
        if (updatedEntries.Count == 0) { return; }

        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            IReadOnlyList<LibraryEntryId> updatedIds;

            try
            {
                updatedIds = await store.UpdateRangeAsync(updatedEntries).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Warning($"FilterLibrary bulk tag update failed; reloading library. {ex.Message}");
                errorBannerService.ReportError(new LibraryTagsBulkUpdateFailed());
                dispatcher.Dispatch(new LoadLibraryAction());
                dispatcher.Dispatch(new TagBulkUpdateFailedAction());

                return;
            }

            if (updatedIds.Count == 0)
            {
                errorBannerService.ReportError(new LibraryTagsBulkUpdateFailed());
                dispatcher.Dispatch(new LoadLibraryAction());
                dispatcher.Dispatch(new TagBulkUpdateFailedAction());

                return;
            }

            var updatedIdSet = updatedIds.ToHashSet();
            var latestEntries = state.Value.Entries;
            var projections = new List<LibraryEntry>(updatedIds.Count);
            List<LibraryEntry>? reissueQueue = null;

            foreach (var entry in updatedEntries)
            {
                if (!updatedIdSet.Contains(entry.Id)) { continue; }

                var latest = latestEntries.FirstOrDefault(e => e.Id == entry.Id);

                if (latest is null) { continue; }

                LibraryEntry projected = (latest, entry) switch
                {
                    (LibraryEntrySavedFilter latestFilter, LibraryEntrySavedFilter bulkFilter) =>
                        latestFilter with { Tags = bulkFilter.Tags },
                    (LibraryEntryFilterSet latestSet, LibraryEntryFilterSet bulkSet) =>
                        latestSet with { Tags = bulkSet.Tags },
                    _ => entry,
                };

                projections.Add(projected);

                if (NonTagFieldsDiffer(latest, entry))
                {
                    (reissueQueue ??= []).Add(projected);
                }
            }

            if (reissueQueue is { Count: > 0 })
            {
                try
                {
                    await store.UpdateRangeAsync(reissueQueue).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.Warning($"FilterLibrary bulk tag re-issue against latest snapshot failed; reloading library. {ex.Message}");
                    errorBannerService.ReportError(new LibraryTagsBulkUpdateFailed());
                    dispatcher.Dispatch(new LoadLibraryAction());
                    dispatcher.Dispatch(new TagBulkUpdateFailedAction());

                    return;
                }
            }

            foreach (var projected in projections)
            {
                dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(projected));
            }

            if (updatedIds.Count < updatedEntries.Count)
            {
                dispatcher.Dispatch(new LoadLibraryAction());
            }

            announcementService.Announce(buildAnnouncement(updatedIds.Count));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void DispatchUpdateWithLatestSnapshot(
        LibraryEntryId id,
        Func<LibraryEntry, LibraryEntry> mutate,
        IDispatcher dispatcher) =>
        DispatchUpdateWithLatestSnapshot(id, mutate, dispatcher, state.Value.Entries);

    private async Task<bool> PersistAddAsync(LibraryEntry entry, IDispatcher dispatcher)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            try { await store.AddAsync(entry).ConfigureAwait(false); }
            catch (Exception ex)
            {
                logger.Warning($"FilterLibrary Add failed for {entry.Id}. {ex.Message}");

                return false;
            }

            dispatcher.Dispatch(new AddLibraryEntrySuccessAction(entry));

            return true;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task PersistAndDispatchAsync(
        LibraryEntryId id,
        Func<LibraryEntry, LibraryEntry> mutate,
        IDispatcher dispatcher,
        BannerMessage? failureBanner = null)
    {
        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            var snapshot = state.Value.Entries.FirstOrDefault(e => e.Id == id);

            if (snapshot is null) { return; }

            var mutated = mutate(snapshot);

            if (ReferenceEquals(mutated, snapshot)) { return; }

            try { await store.UpdateAsync(mutated).ConfigureAwait(false); }
            catch (Exception ex)
            {
                logger.Warning($"FilterLibrary Update failed for {id}. {ex.Message}");

                if (failureBanner is { } banner)
                {
                    errorBannerService.ReportError(banner);
                }

                return;
            }

            var latest = state.Value.Entries.FirstOrDefault(e => e.Id == id);

            if (latest is null) { return; }

            var latestMutated = mutate(latest);

            if (!ReferenceEquals(latest, snapshot))
            {
                try { await store.UpdateAsync(latestMutated).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    logger.Warning($"FilterLibrary Update (re-issue against latest snapshot) failed for {id}. {ex.Message}");
                    dispatcher.Dispatch(new LoadLibraryAction());

                    if (failureBanner is { } banner)
                    {
                        errorBannerService.ReportError(banner);
                    }

                    return;
                }
            }

            dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(latestMutated));
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private Task PromoteSourceIfAutoTracked(LibraryEntryId? sourceEntryId, IDispatcher dispatcher)
    {
        if (sourceEntryId is not { } id) { return Task.CompletedTask; }

        var source = state.Value.Entries.FirstOrDefault(e => e.Id == id);

        if (source is null || source.Origin == LibraryEntryOrigin.UserSaved) { return Task.CompletedTask; }

        return PersistAndDispatchAsync(id, PromoteOriginToUserSaved, dispatcher);
    }

    private async Task PruneFromSnapshot(ImmutableList<LibraryEntry> snapshot, IDispatcher dispatcher)
    {
        var autoTrackedRecents = snapshot
            .OfType<LibraryEntrySavedFilter>()
            .Where(e => e is { Origin: LibraryEntryOrigin.AutoTracked, LastUsedUtc: not null })
            .ToList();

        if (autoTrackedRecents.Count <= MaxAutoTrackedRecents) { return; }

        var toDelete = autoTrackedRecents
            .OrderBy(e => e.LastUsedUtc!.Value)
            .ThenBy(e => e.CreatedUtc)
            .Take(autoTrackedRecents.Count - MaxAutoTrackedRecents)
            .ToList();

        foreach (var entry in toDelete)
        {
            bool deleted;

            try { deleted = await store.TryDeleteAutoTrackedIfNotFavoriteAsync(entry.Id).ConfigureAwait(false); }
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
