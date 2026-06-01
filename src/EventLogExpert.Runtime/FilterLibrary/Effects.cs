// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class Effects(
    IFilterLibraryStore store,
    IState<FilterLibraryState> state,
    IState<FilterPaneState> filterPaneState,
    ITraceLogger logger)
{
    private const int MaxAutoTrackedRecents = 50;

    [EffectMethod]
    public Task HandleAddFilterToExistingPreset(AddFilterToExistingPresetAction action, IDispatcher dispatcher)
    {
        var preset = state.Value.Entries.OfType<LibraryEntryPreset>().FirstOrDefault(p => p.Id == action.PresetId);

        if (preset is null) { return Task.CompletedTask; }

        // Dedup tuple matches the SQL UNIQUE INDEX scope: (lower(ComparisonText), Mode, IsExcluded).
        // Distinct Mode values are intentional per the mode-drift policy.
        var addText = action.Filter.ComparisonText;
        var addMode = action.Filter.Mode;
        var addExcluded = action.Filter.IsExcluded;

        if (preset.Filters.Any(f =>
            string.Equals(f.ComparisonText, addText, StringComparison.OrdinalIgnoreCase) &&
            f.Mode == addMode &&
            f.IsExcluded == addExcluded))
        {
            PromoteSourceIfAutoTracked(action.SourceEntryId, dispatcher);
            return Task.CompletedTask;
        }

        var newFilter = action.Filter with { Id = FilterId.Create(), IsEnabled = false };
        var updatedPreset = preset with { Filters = preset.Filters.Add(newFilter) };

        try { store.Update(updatedPreset); }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Update (add to preset) failed for {preset.Id}. {ex.Message}");

            return Task.CompletedTask;
        }

        dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(updatedPreset));
        PromoteSourceIfAutoTracked(action.SourceEntryId, dispatcher);

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleAddFilterToNewPreset(AddFilterToNewPresetAction action, IDispatcher dispatcher)
    {
        if (string.IsNullOrWhiteSpace(action.NewPresetName)) { return Task.CompletedTask; }

        var created = new LibraryEntryPreset
        {
            Name = action.NewPresetName,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [action.Filter with { Id = FilterId.Create(), IsEnabled = false }],
            Origin = LibraryEntryOrigin.UserSaved,
        };

        try { store.Add(created); }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Add (new preset) failed for {created.Id}. {ex.Message}");

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

    [EffectMethod(typeof(LoadLibraryAction))]
    public Task HandleLoadLibrary(IDispatcher dispatcher)
    {
        // LoadLibraryStartedAction is queued (not run inline) per Fluxor 6.9 re-entrancy semantics —
        // it flips IsLoading=true between Started and Success reducer commits for UI subscribers, but
        // it does NOT prevent concurrent loads from inside this effect. The modal's
        // `!IsLoaded || LoadError` check is the effective re-open guard.
        dispatcher.Dispatch(new LoadLibraryStartedAction());

        try
        {
            var entries = store.LoadAll().ToImmutableList();
            dispatcher.Dispatch(new LoadLibrarySuccessAction(entries));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary load failed; rendering error state. {ex.Message}");
            dispatcher.Dispatch(new LoadLibraryFailureAction());
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleRecordEntryApplied(RecordEntryAppliedAction action, IDispatcher dispatcher)
    {
        var entries = state.Value.Entries;
        var entry = entries.FirstOrDefault(e => e.Id == action.EntryId);

        if (entry is null || entry.IsFavorite) { return Task.CompletedTask; }

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
            LibraryEntryPreset p => p with { LastUsedUtc = nowUtc },
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
        var collisionProjected = entries.Add(collisionEntry);
        dispatcher.Dispatch(new AddLibraryEntrySuccessAction(collisionEntry));
        PruneFromSnapshot(collisionProjected, dispatcher);

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
            LibraryEntryPreset p => p with { Origin = LibraryEntryOrigin.UserSaved },
            _ => entry,
        };

        return PersistUpdateAsync(promoted, dispatcher);
    }

    [EffectMethod]
    public Task HandleSavePaneAsPreset(SavePaneAsPresetAction action, IDispatcher dispatcher)
    {
        if (string.IsNullOrWhiteSpace(action.Name)) { return Task.CompletedTask; }

        var paneFilters = filterPaneState.Value.Filters;

        if (paneFilters.IsEmpty) { return Task.CompletedTask; }

        dispatcher.Dispatch(new SavePresetAction(action.Name, paneFilters));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleSavePreset(SavePresetAction action, IDispatcher dispatcher)
    {
        if (string.IsNullOrWhiteSpace(action.Name) || action.Filters.IsEmpty) { return Task.CompletedTask; }

        var created = new LibraryEntryPreset
        {
            Name = action.Name,
            CreatedUtc = DateTimeOffset.UtcNow,
            // Regenerate FilterIds so Razor `@key=filter.Id` diffing stays correct when the
            // same pane filters are saved into multiple presets.
            Filters = [.. action.Filters.Select(f => f with { Id = FilterId.Create(), IsEnabled = false })],
            Origin = LibraryEntryOrigin.UserSaved,
        };

        return PersistAddAsync(created, dispatcher);
    }

    [EffectMethod]
    public Task HandleSetIsFavorite(SetIsFavoriteAction action, IDispatcher dispatcher)
    {
        var entry = state.Value.Entries.FirstOrDefault(e => e.Id == action.EntryId);

        if (entry is null || entry.IsFavorite == action.IsFavorite) { return Task.CompletedTask; }

        LibraryEntry updated;

        if (action.IsFavorite)
        {
            // Favoriting: mutex (LastUsedUtc=null) + promotion (Origin=UserSaved). Symmetric for filter + preset.
            updated = entry switch
            {
                LibraryEntrySavedFilter f => f with
                {
                    IsFavorite = true,
                    LastUsedUtc = null,
                    Origin = LibraryEntryOrigin.UserSaved,
                },
                LibraryEntryPreset p => p with
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
            // Unfavoriting: filters drop to Recents (matches legacy FilterCache UX); presets stay out of Recents.
            updated = entry switch
            {
                LibraryEntrySavedFilter f => f with
                {
                    IsFavorite = false,
                    LastUsedUtc = DateTimeOffset.UtcNow,
                },
                LibraryEntryPreset p => p with
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

    private static ImmutableList<SavedFilter> ExtractFilters(LibraryEntry entry) =>
        entry switch
        {
            LibraryEntrySavedFilter f => [f.Filter],
            LibraryEntryPreset p => p.Filters,
            _ => throw new InvalidOperationException($"Unhandled LibraryEntry type '{entry.GetType().FullName}'."),
        };

    private static string TruncateForDisplay(string text) => text.Length <= 80 ? text : text[..77] + "...";

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
            LibraryEntryPreset p => p with { Origin = LibraryEntryOrigin.UserSaved },
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
