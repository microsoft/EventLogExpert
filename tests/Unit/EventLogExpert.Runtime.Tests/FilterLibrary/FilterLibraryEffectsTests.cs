// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using NSubstitute;
using Effects = EventLogExpert.Runtime.FilterLibrary.Effects;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class FilterLibraryEffectsTests
{
    [Fact]
    public async Task HandleAddFilterToExistingPreset_AppendsToPreset()
    {
        var existingFilter = SavedFilter.TryCreate("Level == 2");
        var newFilter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(existingFilter);
        Assert.NotNull(newFilter);
        var preset = new LibraryEntryPreset
        {
            Name = "Preset",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [existingFilter],
        };
        var (effects, store, _, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [preset] });

        await effects.HandleAddFilterToExistingPreset(
            new AddFilterToExistingPresetAction(preset.Id, newFilter, SourceEntryId: null),
            Substitute.For<IDispatcher>());

        store.Received(1).Update(Arg.Is<LibraryEntry>(e => e is LibraryEntryPreset
            && ((LibraryEntryPreset)e).Id == preset.Id && ((LibraryEntryPreset)e).Filters.Count == 2
            && ((LibraryEntryPreset)e).Filters.Any(f => f.ComparisonText == "Level == 4")));
    }

    [Fact]
    public async Task HandleAddFilterToExistingPreset_DuplicateTuple_DoesNotAppendButStillPromotesSource()
    {
        var existingFilter = SavedFilter.TryCreate("Level == 4");
        var duplicate = SavedFilter.TryCreate("LEVEL == 4");
        Assert.NotNull(existingFilter);
        Assert.NotNull(duplicate);
        var preset = new LibraryEntryPreset
        {
            Name = "Preset",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [existingFilter],
        };
        var source = BuildFilterEntry("Source") with { Origin = LibraryEntryOrigin.AutoTracked };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [preset, source] });

        await effects.HandleAddFilterToExistingPreset(
            new AddFilterToExistingPresetAction(preset.Id, duplicate, source.Id),
            dispatcher);

        // Did NOT update the preset (duplicate).
        store.DidNotReceive().Update(Arg.Is<LibraryEntry>(e => e.Id == preset.Id));
        // But DID promote the source.
        store.Received(1).Update(Arg.Is<LibraryEntry>(e => e.Id == source.Id && e.Origin == LibraryEntryOrigin.UserSaved));
    }

    [Fact]
    public async Task HandleAddFilterToExistingPreset_SameTextDifferentMode_AppendsAsDistinctFilter()
    {
        // Mode-drift policy: distinct Mode + same ComparisonText must coexist in a preset.
        var advanced = SavedFilter.TryCreate("Level == 4", mode: FilterMode.Advanced);
        var basic = SavedFilter.TryCreate("Level == 4", mode: FilterMode.Basic);
        Assert.NotNull(advanced);
        Assert.NotNull(basic);
        var preset = new LibraryEntryPreset
        {
            Name = "Preset",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [advanced],
        };
        var (effects, store, _, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [preset] });

        await effects.HandleAddFilterToExistingPreset(
            new AddFilterToExistingPresetAction(preset.Id, basic, SourceEntryId: null),
            Substitute.For<IDispatcher>());

        store.Received(1).Update(Arg.Is<LibraryEntry>(e => e.GetType() == typeof(LibraryEntryPreset)
            && ((LibraryEntryPreset)e).Id == preset.Id
            && ((LibraryEntryPreset)e).Filters.Count == 2
            && ((LibraryEntryPreset)e).Filters.Any(f => f.Mode == FilterMode.Advanced)
            && ((LibraryEntryPreset)e).Filters.Any(f => f.Mode == FilterMode.Basic)));
    }

    [Fact]
    public async Task HandleAddFilterToExistingPreset_UnknownPreset_IsNoOp()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var (effects, store, _, _, _) = CreateEffects();

        await effects.HandleAddFilterToExistingPreset(
            new AddFilterToExistingPresetAction(LibraryEntryId.Create(), filter, SourceEntryId: null),
            Substitute.For<IDispatcher>());

        store.DidNotReceive().Update(Arg.Any<LibraryEntry>());
    }

    [Fact]
    public async Task HandleAddFilterToNewPreset_CreatesPresetWithSingleFilter()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleAddFilterToNewPreset(new AddFilterToNewPresetAction("New", filter, SourceEntryId: null), dispatcher);

        store.Received(1).Add(Arg.Is<LibraryEntry>(e => e.GetType() == typeof(LibraryEntryPreset)
            && ((LibraryEntryPreset)e).Name == "New"
            && ((LibraryEntryPreset)e).Filters.Count == 1
            && ((LibraryEntryPreset)e).Filters[0].Id != filter.Id));
    }

    [Fact]
    public async Task HandleAddFilterToNewPreset_PromotesAutoTrackedSource()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var source = BuildFilterEntry("Source") with { Origin = LibraryEntryOrigin.AutoTracked };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [source] });

        await effects.HandleAddFilterToNewPreset(new AddFilterToNewPresetAction("New", filter, source.Id), dispatcher);

        store.Received(1).Update(Arg.Is<LibraryEntry>(e =>
            e.Id == source.Id && e.Origin == LibraryEntryOrigin.UserSaved));
    }

    [Fact]
    public async Task HandleAddFilterToNewPreset_WhitespaceName_IsNoOp()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var (effects, store, _, _, _) = CreateEffects();

        await effects.HandleAddFilterToNewPreset(new AddFilterToNewPresetAction("   ", filter, SourceEntryId: null), Substitute.For<IDispatcher>());

        store.DidNotReceive().Add(Arg.Any<LibraryEntry>());
    }

    [Fact]
    public async Task HandleAddLibraryEntry_PersistsAndDispatchesSuccess()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleAddLibraryEntry(new AddLibraryEntryAction(entry), dispatcher);

        store.Received(1).Add(entry);
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => ReferenceEquals(a.Entry, entry)));
    }

    [Fact]
    public async Task HandleAddLibraryEntry_WhenStoreThrows_DoesNotDispatchSuccess()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.When(s => s.Add(Arg.Any<LibraryEntry>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleAddLibraryEntry(new AddLibraryEntryAction(entry), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddLibraryEntrySuccessAction>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleApplyLibraryEntry_PresetEntry_DispatchesMergeFiltersWithAllFiltersAndRecordEntryApplied()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);

        var preset = new LibraryEntryPreset
        {
            Name = "Preset",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [f1, f2],
        };
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [preset] });

        await effects.HandleApplyLibraryEntry(new ApplyLibraryEntryAction(preset.Id), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<MergeFiltersAction>(a => a.Filters.Count == 2));
        dispatcher.Received(1).Dispatch(Arg.Is<RecordEntryAppliedAction>(a => a.EntryId == preset.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReplaceFiltersAction>());
    }

    [Fact]
    public async Task HandleApplyLibraryEntry_SavedFilterEntry_DispatchesMergeFiltersWithSingleFilterAndRecordEntryApplied()
    {
        var entry = BuildFilterEntry("First");
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleApplyLibraryEntry(new ApplyLibraryEntryAction(entry.Id), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<MergeFiltersAction>(a => a.Filters.Count == 1));
        dispatcher.Received(1).Dispatch(Arg.Is<RecordEntryAppliedAction>(a => a.EntryId == entry.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReplaceFiltersAction>());
    }

    [Fact]
    public async Task HandleApplyLibraryEntry_UnknownConcreteType_ThrowsInvalidOperationException()
    {
        var unknown = new UnknownLibraryEntry
        {
            Name = "Unknown",
            CreatedUtc = DateTimeOffset.UtcNow,
        };
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [unknown] });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            effects.HandleApplyLibraryEntry(new ApplyLibraryEntryAction(unknown.Id), dispatcher));
    }

    [Fact]
    public async Task HandleApplyLibraryEntry_UnknownId_IsNoOp()
    {
        var (effects, _, dispatcher, _, _) = CreateEffects();

        await effects.HandleApplyLibraryEntry(new ApplyLibraryEntryAction(LibraryEntryId.Create()), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<MergeFiltersAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<RecordEntryAppliedAction>());
    }

    [Fact]
    public async Task HandleDeleteLibraryEntry_PersistsAndDispatchesSuccess()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();
        var id = LibraryEntryId.Create();

        await effects.HandleDeleteLibraryEntry(new DeleteLibraryEntryAction(id), dispatcher);

        store.Received(1).Delete(id);
        dispatcher.Received(1).Dispatch(Arg.Is<DeleteLibraryEntrySuccessAction>(a => a.EntryId == id));
    }

    [Fact]
    public async Task HandleDeleteLibraryEntry_WhenStoreThrows_DoesNotDispatchSuccess()
    {
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.When(s => s.Delete(Arg.Any<LibraryEntryId>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleDeleteLibraryEntry(new DeleteLibraryEntryAction(LibraryEntryId.Create()), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<DeleteLibraryEntrySuccessAction>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleLoadLibrary_DispatchesStartedAndSuccessWithStoreEntries()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, _) = CreateEffects();
        store.LoadAll().Returns([entry]);

        await effects.HandleLoadLibrary(dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryStartedAction>());
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 1 && a.Entries[0].Id == entry.Id));
    }

    [Fact]
    public async Task HandleLoadLibrary_WhenStoreThrows_DispatchesStartedThenFailureAndLogs()
    {
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.LoadAll().Returns(_ => throw new InvalidOperationException("boom"));

        await effects.HandleLoadLibrary(dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryStartedAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryFailureAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibrarySuccessAction>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleRecordEntryApplied_BumpReturnsFalse_SkipsDispatchAndPrune()
    {
        // SQL bump returns false (concurrent SetIsFavorite committed) → no dispatch, no prune.
        var entry = BuildFilterEntry("First") with { Origin = LibraryEntryOrigin.AutoTracked };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });
        store.TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>()).Returns(false);

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(entry.Id), dispatcher);

        store.Received(1).TryBumpLastUsedIfNotFavorite(entry.Id, Arg.Any<DateTimeOffset>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        store.DidNotReceive().TryDeleteAutoTrackedIfNotFavorite(Arg.Any<LibraryEntryId>());
    }

    [Fact]
    public async Task HandleRecordEntryApplied_FavoritedEntry_IsNoOp()
    {
        var entry = BuildFilterEntry("First") with { IsFavorite = true };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(entry.Id), dispatcher);

        store.DidNotReceive().TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleRecordEntryApplied_NotFavoriteBumpSucceeds_DispatchesUpdate()
    {
        var entry = BuildFilterEntry("First") with { Origin = LibraryEntryOrigin.AutoTracked };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });
        store.TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>()).Returns(true);

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(entry.Id), dispatcher);

        store.Received(1).TryBumpLastUsedIfNotFavorite(entry.Id, Arg.Any<DateTimeOffset>());
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a =>
            a.Entry.Id == entry.Id && a.Entry.LastUsedUtc != null));
    }

    [Fact]
    public async Task HandleRecordEntryApplied_PruneDeleteReturnsFalse_DoesNotDispatchDeleteSuccess()
    {
        // SQL DELETE returns false (entry was promoted/favorited since snapshot) → don't orphan state.
        var seedFilter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(seedFilter);
        var entries = new List<LibraryEntry>();
        for (int i = 0; i < 50; i++)
        {
            entries.Add(new LibraryEntrySavedFilter
            {
                Name = $"recent-{i}",
                CreatedUtc = DateTimeOffset.UtcNow,
                Origin = LibraryEntryOrigin.AutoTracked,
                LastUsedUtc = new DateTimeOffset(2026, 5, 1, 0, 0, i, TimeSpan.Zero),
                Filter = seedFilter,
            });
        }

        var bumpTarget = new LibraryEntrySavedFilter
        {
            Name = "bumped",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            LastUsedUtc = new DateTimeOffset(2026, 5, 1, 0, 1, 0, TimeSpan.Zero),
            Filter = seedFilter,
        };
        entries.Add(bumpTarget);

        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [.. entries] });
        store.TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>()).Returns(true);
        store.TryDeleteAutoTrackedIfNotFavorite(Arg.Any<LibraryEntryId>()).Returns(false);

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(bumpTarget.Id), dispatcher);

        store.Received(1).TryDeleteAutoTrackedIfNotFavorite(Arg.Any<LibraryEntryId>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<DeleteLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleRecordEntryApplied_PrunesOldestAutoTrackedEntries_WhenCapExceeded()
    {
        // Seed 50 AutoTracked recents (at-cap) + the entry we're about to bump.
        var seedFilter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(seedFilter);
        var entries = new List<LibraryEntry>();
        for (int i = 0; i < 50; i++)
        {
            entries.Add(new LibraryEntrySavedFilter
            {
                Name = $"recent-{i}",
                CreatedUtc = DateTimeOffset.UtcNow,
                Origin = LibraryEntryOrigin.AutoTracked,
                LastUsedUtc = new DateTimeOffset(2026, 5, 1, 0, 0, i, TimeSpan.Zero),
                Filter = seedFilter,
            });
        }

        // The 51st entry — about to be bumped (will project into the prune snapshot).
        // Pre-bump LastUsedUtc doesn't affect the prune order — the effect overwrites it with UtcNow
        // and that's the value that lands in the snapshot before PruneFromSnapshot runs.
        var bumpTarget = new LibraryEntrySavedFilter
        {
            Name = "bumped",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            LastUsedUtc = new DateTimeOffset(2026, 5, 1, 0, 1, 0, TimeSpan.Zero),
            Filter = seedFilter,
        };
        entries.Add(bumpTarget);

        var oldestId = ((LibraryEntrySavedFilter)entries[0]).Id;
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [.. entries] });
        store.TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>()).Returns(true);
        store.TryDeleteAutoTrackedIfNotFavorite(Arg.Any<LibraryEntryId>()).Returns(true);

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(bumpTarget.Id), dispatcher);

        // SQL-guarded delete no-ops if the row was concurrently promoted/favorited.
        store.Received(1).TryDeleteAutoTrackedIfNotFavorite(oldestId);
        dispatcher.Received(1).Dispatch(Arg.Is<DeleteLibraryEntrySuccessAction>(a => a.EntryId == oldestId));
    }

    [Fact]
    public async Task HandleRecordEntryApplied_UnknownId_IsNoOp()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(LibraryEntryId.Create()), dispatcher);

        store.DidNotReceive().TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task HandleRecordFilterApplied_CollisionBranch_BumpsExistingAndDispatchesAddSuccess()
    {
        // In-memory state has no match; SQL INSERT OR IGNORE collides with an existing AutoTracked row.
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var existing = new LibraryEntrySavedFilter
        {
            Name = "Pre-existing in SQL",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            LastUsedUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            Filter = filter,
        };

        var (effects, store, dispatcher, _, _) = CreateEffects();
        store.AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>()).Returns((existing, false));
        store.TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        store.Received(1).AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>());
        store.Received(1).TryBumpLastUsedIfNotFavorite(existing.Id, Arg.Any<DateTimeOffset>());
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a =>
            a.Entry.Id == existing.Id && a.Entry.LastUsedUtc != existing.LastUsedUtc));
    }

    [Fact]
    public async Task HandleRecordFilterApplied_CollisionBranch_FavoritedExisting_SkipsBumpAndDispatch()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var existing = new LibraryEntrySavedFilter
        {
            Name = "Favorited in SQL",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            IsFavorite = true,
            Filter = filter,
        };

        var (effects, store, dispatcher, _, _) = CreateEffects();
        store.AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>()).Returns((existing, false));

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        store.DidNotReceive().TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleRecordFilterApplied_CollisionBranch_StateAlreadyHasEntry_DoesNotInflateSnapshotForPrune()
    {
        var seedFilter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(seedFilter);
        var entries = new List<LibraryEntry>();
        for (int i = 0; i < 49; i++)
        {
            entries.Add(new LibraryEntrySavedFilter
            {
                Name = $"recent-{i}",
                CreatedUtc = DateTimeOffset.UtcNow,
                Origin = LibraryEntryOrigin.AutoTracked,
                LastUsedUtc = new DateTimeOffset(2026, 5, 1, 0, 0, i, TimeSpan.Zero),
                Filter = SavedFilter.TryCreate($"Level == {i + 100}")!,
            });
        }

        // The "already in state" collision target — distinct ComparisonText so in-memory match misses.
        var alreadyInState = new LibraryEntrySavedFilter
        {
            Name = "already-in-state",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            LastUsedUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 49, TimeSpan.Zero),
            Filter = SavedFilter.TryCreate("Level == 1999")!,
        };
        entries.Add(alreadyInState);  // state count now 50 (at cap)

        var newFilter = SavedFilter.TryCreate("Level == 2777");
        Assert.NotNull(newFilter);

        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [.. entries] });
        store.AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>())
            .Returns((alreadyInState, false));
        store.TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>()).Returns(true);
        store.TryDeleteAutoTrackedIfNotFavorite(Arg.Any<LibraryEntryId>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(newFilter), dispatcher);

        // Snapshot should be 50 (SetItem on the existing row, not Add); prune sees count == cap; no delete.
        store.DidNotReceive().TryDeleteAutoTrackedIfNotFavorite(Arg.Any<LibraryEntryId>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<DeleteLibraryEntrySuccessAction>());
        // Bump + AddSuccess dispatch still happen as part of the collision-bump path.
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => a.Entry.Id == alreadyInState.Id));
    }

    [Fact]
    public async Task HandleRecordFilterApplied_DifferentMode_TreatedAsDistinctEntry()
    {
        var advanced = SavedFilter.TryCreate("Level == 4", mode: FilterMode.Advanced);
        var basic = SavedFilter.TryCreate("Level == 4", mode: FilterMode.Basic);
        Assert.NotNull(advanced);
        Assert.NotNull(basic);

        var existing = new LibraryEntrySavedFilter
        {
            Name = "Level == 4",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            Filter = advanced,
        };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [existing] });
        store.AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>())
            .Returns(call => (call.Arg<LibraryEntrySavedFilter>(), true));

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(basic), dispatcher);

        store.Received(1).AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>());
        store.DidNotReceive().TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task HandleRecordFilterApplied_EmptyComparisonText_IsNoOp()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(SavedFilter.Empty), dispatcher);

        store.DidNotReceive().AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>());
        store.DidNotReceive().TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task HandleRecordFilterApplied_FavoritedExisting_DoesNotBump()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var existing = new LibraryEntrySavedFilter
        {
            Name = "Level == 4",
            CreatedUtc = DateTimeOffset.UtcNow,
            IsFavorite = true,
            Filter = filter,
        };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [existing] });

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        store.DidNotReceive().TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>());
        store.DidNotReceive().AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>());
    }

    [Fact]
    public async Task HandleRecordFilterApplied_NoExisting_InsertsAutoTrackedEntry()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();
        store.AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>())
            .Returns(call =>
            {
                var candidate = call.Arg<LibraryEntrySavedFilter>();
                return (candidate, true);
            });
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        store.Received(1).AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>());
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a =>
            a.Entry.GetType() == typeof(LibraryEntrySavedFilter)
                && ((LibraryEntrySavedFilter)a.Entry).Origin == LibraryEntryOrigin.AutoTracked
                && ((LibraryEntrySavedFilter)a.Entry).LastUsedUtc != null
                && ((LibraryEntrySavedFilter)a.Entry).Filter.ComparisonText == filter.ComparisonText));
    }

    [Fact]
    public async Task HandleRecordFilterApplied_PrunesOldestAutoTrackedEntries_WhenInsertExceedsCap()
    {
        // Mirrors the HandleRecordEntryApplied prune test for the auto-create insert path.
        var seedFilter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(seedFilter);
        var entries = new List<LibraryEntry>();
        for (int i = 0; i < 50; i++)
        {
            entries.Add(new LibraryEntrySavedFilter
            {
                Name = $"recent-{i}",
                CreatedUtc = DateTimeOffset.UtcNow,
                Origin = LibraryEntryOrigin.AutoTracked,
                LastUsedUtc = new DateTimeOffset(2026, 5, 1, 0, 0, i, TimeSpan.Zero),
                // Distinct ComparisonText per entry forces the effect through AddOrReturnExistingFilter.
                Filter = SavedFilter.TryCreate($"Level == {i + 100}")!,
            });
        }

        var oldestId = ((LibraryEntrySavedFilter)entries[0]).Id;
        var newFilter = SavedFilter.TryCreate("Level == 999");
        Assert.NotNull(newFilter);

        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [.. entries] });
        store.AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>())
            .Returns(call => (call.Arg<LibraryEntrySavedFilter>(), true));
        store.TryDeleteAutoTrackedIfNotFavorite(Arg.Any<LibraryEntryId>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(newFilter), dispatcher);

        store.Received(1).AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>());
        store.Received(1).TryDeleteAutoTrackedIfNotFavorite(oldestId);
        dispatcher.Received(1).Dispatch(Arg.Is<DeleteLibraryEntrySuccessAction>(a => a.EntryId == oldestId));
    }

    [Fact]
    public async Task HandleRecordFilterApplied_TupleMatch_BumpReturnsFalse_SkipsDispatch()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var existing = new LibraryEntrySavedFilter
        {
            Name = "Level == 4",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            LastUsedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
        };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [existing] });
        store.TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>()).Returns(false);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleRecordFilterApplied_TupleMatch_BumpsLastUsedAndDispatchesUpdate()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var existing = new LibraryEntrySavedFilter
        {
            Name = "Level == 4",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            LastUsedUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            Filter = filter,
        };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [existing] });
        store.TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        store.Received(1).TryBumpLastUsedIfNotFavorite(existing.Id, Arg.Any<DateTimeOffset>());
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a =>
            a.Entry.Id == existing.Id && a.Entry.LastUsedUtc != existing.LastUsedUtc));
        store.DidNotReceive().AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>());
    }

    [Fact]
    public async Task HandleRecordFilterApplied_UserSavedExisting_BumpsAndSkipsAutoTrack()
    {
        // Re-applying a user-saved filter refreshes its LastUsedUtc; no AutoTracked duplicate.
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var existing = new LibraryEntrySavedFilter
        {
            Name = "User-Saved Level 4",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.UserSaved,
            LastUsedUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            Filter = filter,
        };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [existing] });
        store.TryBumpLastUsedIfNotFavorite(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        store.Received(1).TryBumpLastUsedIfNotFavorite(existing.Id, Arg.Any<DateTimeOffset>());
        store.DidNotReceive().AddOrReturnExistingFilter(Arg.Any<LibraryEntrySavedFilter>());
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a => a.Entry.Id == existing.Id));
    }

    [Fact]
    public async Task HandleReplaceWithLibraryEntry_PresetEntry_DispatchesReplaceFiltersWithAllFiltersAndRecordEntryApplied()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);

        var preset = new LibraryEntryPreset
        {
            Name = "Preset",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [f1, f2],
        };
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [preset] });

        await effects.HandleReplaceWithLibraryEntry(new ReplaceWithLibraryEntryAction(preset.Id), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<ReplaceFiltersAction>(a => a.Filters.Count == 2));
        dispatcher.Received(1).Dispatch(Arg.Is<RecordEntryAppliedAction>(a => a.EntryId == preset.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<MergeFiltersAction>());
    }

    [Fact]
    public async Task HandleReplaceWithLibraryEntry_SavedFilterEntry_DispatchesReplaceFiltersWithSingleFilterAndRecordEntryApplied()
    {
        var entry = BuildFilterEntry("First");
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleReplaceWithLibraryEntry(new ReplaceWithLibraryEntryAction(entry.Id), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<ReplaceFiltersAction>(a => a.Filters.Count == 1));
        dispatcher.Received(1).Dispatch(Arg.Is<RecordEntryAppliedAction>(a => a.EntryId == entry.Id));
    }

    [Fact]
    public async Task HandleReplaceWithLibraryEntry_UnknownConcreteType_ThrowsInvalidOperationException()
    {
        var unknown = new UnknownLibraryEntry
        {
            Name = "Unknown",
            CreatedUtc = DateTimeOffset.UtcNow,
        };
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [unknown] });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            effects.HandleReplaceWithLibraryEntry(new ReplaceWithLibraryEntryAction(unknown.Id), dispatcher));
    }

    [Fact]
    public async Task HandleReplaceWithLibraryEntry_UnknownId_IsNoOp()
    {
        var (effects, _, dispatcher, _, _) = CreateEffects();

        await effects.HandleReplaceWithLibraryEntry(new ReplaceWithLibraryEntryAction(LibraryEntryId.Create()), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReplaceFiltersAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<RecordEntryAppliedAction>());
    }

    [Fact]
    public async Task HandleSaveEntry_AlreadyUserSaved_IsNoOp()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleSaveEntry(new SaveEntryAction(entry.Id), dispatcher);

        store.DidNotReceive().Update(Arg.Any<LibraryEntry>());
    }

    [Fact]
    public async Task HandleSaveEntry_AutoTrackedEntry_PromotesToUserSaved()
    {
        var entry = BuildFilterEntry("First") with { Origin = LibraryEntryOrigin.AutoTracked };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleSaveEntry(new SaveEntryAction(entry.Id), dispatcher);

        store.Received(1).Update(Arg.Is<LibraryEntry>(e =>
            e.Id == entry.Id && e.Origin == LibraryEntryOrigin.UserSaved));
    }

    [Fact]
    public async Task HandleSaveEntry_UnknownId_IsNoOp()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleSaveEntry(new SaveEntryAction(LibraryEntryId.Create()), dispatcher);

        store.DidNotReceive().Update(Arg.Any<LibraryEntry>());
    }

    [Fact]
    public async Task HandleSavePaneAsPreset_EmptyPane_IsNoOp()
    {
        var (effects, _, dispatcher, _, _) = CreateEffects(paneState: new FilterPaneState());

        await effects.HandleSavePaneAsPreset(new SavePaneAsPresetAction("Pane Preset"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<SavePresetAction>());
    }

    [Fact]
    public async Task HandleSavePaneAsPreset_PaneHasFilters_DispatchesSavePresetWithPaneFilters()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        Assert.NotNull(f1);
        var paneState = new FilterPaneState { Filters = [f1] };
        var (effects, _, dispatcher, _, _) = CreateEffects(paneState: paneState);

        await effects.HandleSavePaneAsPreset(new SavePaneAsPresetAction("Pane Preset"), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<SavePresetAction>(a =>
            a.Name == "Pane Preset" && a.Filters.Count == 1 && a.Filters[0] == f1));
    }

    [Fact]
    public async Task HandleSavePaneAsPreset_WhitespaceName_IsNoOp()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        Assert.NotNull(f1);
        var (effects, _, dispatcher, _, _) = CreateEffects(paneState: new FilterPaneState { Filters = [f1] });

        await effects.HandleSavePaneAsPreset(new SavePaneAsPresetAction("   "), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<SavePresetAction>());
    }

    [Fact]
    public async Task HandleSavePreset_CreatesNewPresetWithRegeneratedFilterIds()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);
        var originalIds = new[] { f1.Id, f2.Id };
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleSavePreset(new SavePresetAction("My Preset", [f1, f2]), dispatcher);

        store.Received(1).Add(Arg.Is<LibraryEntry>(e => e.GetType() == typeof(LibraryEntryPreset)
            && ((LibraryEntryPreset)e).Name == "My Preset"
            && ((LibraryEntryPreset)e).Origin == LibraryEntryOrigin.UserSaved
            && ((LibraryEntryPreset)e).Filters.Count == 2
            && ((LibraryEntryPreset)e).Filters.All(f => !originalIds.Contains(f.Id))
            && ((LibraryEntryPreset)e).Filters.All(f => !f.IsEnabled)));
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => a.Entry.GetType() == typeof(LibraryEntryPreset)));
    }

    [Fact]
    public async Task HandleSavePreset_EmptyFilters_IsNoOp()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleSavePreset(new SavePresetAction("Empty", []), dispatcher);

        store.DidNotReceive().Add(Arg.Any<LibraryEntry>());
    }

    [Fact]
    public async Task HandleSavePreset_WhitespaceName_IsNoOp()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleSavePreset(new SavePresetAction("   ", [filter]), dispatcher);

        store.DidNotReceive().Add(Arg.Any<LibraryEntry>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleSetIsFavorite_AlreadyAtTargetState_IsNoOp()
    {
        var entry = BuildFilterEntry("First") with { IsFavorite = true };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleSetIsFavorite(new SetIsFavoriteAction(entry.Id, IsFavorite: true), dispatcher);

        store.DidNotReceive().Update(Arg.Any<LibraryEntry>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleSetIsFavorite_False_OnFilter_BumpsLastUsedToNow()
    {
        var filter = BuildFilterEntry("First") with { IsFavorite = true };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [filter] });

        await effects.HandleSetIsFavorite(new SetIsFavoriteAction(filter.Id, IsFavorite: false), dispatcher);

        store.Received(1).Update(Arg.Is<LibraryEntry>(e =>
            e.Id == filter.Id && !e.IsFavorite && e.LastUsedUtc != null));
    }

    [Fact]
    public async Task HandleSetIsFavorite_False_OnPreset_LeavesLastUsedNull()
    {
        var f = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f);
        var preset = new LibraryEntryPreset
        {
            Name = "Preset",
            CreatedUtc = DateTimeOffset.UtcNow,
            IsFavorite = true,
            Filters = [f],
        };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [preset] });

        await effects.HandleSetIsFavorite(new SetIsFavoriteAction(preset.Id, IsFavorite: false), dispatcher);

        store.Received(1).Update(Arg.Is<LibraryEntry>(e => e.Id == preset.Id && !e.IsFavorite && e.LastUsedUtc == null));
    }

    [Fact]
    public async Task HandleSetIsFavorite_True_SetsFavoriteClearsLastUsedAndPromotesOrigin()
    {
        var entry = BuildFilterEntry("First") with
        {
            Origin = LibraryEntryOrigin.AutoTracked,
            LastUsedUtc = new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero),
        };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleSetIsFavorite(new SetIsFavoriteAction(entry.Id, IsFavorite: true), dispatcher);

        store.Received(1).Update(Arg.Is<LibraryEntry>(e =>
            e.Id == entry.Id && e.IsFavorite && e.LastUsedUtc == null && e.Origin == LibraryEntryOrigin.UserSaved));
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a =>
            a.Entry.IsFavorite && a.Entry.LastUsedUtc == null && a.Entry.Origin == LibraryEntryOrigin.UserSaved));
    }

    [Fact]
    public async Task HandleSetIsFavorite_UnknownId_IsNoOp()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleSetIsFavorite(new SetIsFavoriteAction(LibraryEntryId.Create(), IsFavorite: true), dispatcher);

        store.DidNotReceive().Update(Arg.Any<LibraryEntry>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleUpdateLibraryEntry_PersistsAndDispatchesSuccess()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleUpdateLibraryEntry(new UpdateLibraryEntryAction(entry), dispatcher);

        store.Received(1).Update(entry);
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a => ReferenceEquals(a.Entry, entry)));
    }

    [Fact]
    public async Task HandleUpdateLibraryEntry_WhenStoreThrows_DoesNotDispatchSuccess()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.When(s => s.Update(Arg.Any<LibraryEntry>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleUpdateLibraryEntry(new UpdateLibraryEntryAction(entry), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    private static LibraryEntrySavedFilter BuildFilterEntry(string name)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
        };
    }

    private static (Effects effects, IFilterLibraryStore store, IDispatcher dispatcher, IState<FilterLibraryState> stateMock, ITraceLogger logger) CreateEffects(
        FilterLibraryState? state = null,
        FilterPaneState? paneState = null)
    {
        var store = Substitute.For<IFilterLibraryStore>();
        store.LoadAll().Returns([]);

        var stateMock = Substitute.For<IState<FilterLibraryState>>();
        stateMock.Value.Returns(state ?? new FilterLibraryState());

        var paneStateMock = Substitute.For<IState<FilterPaneState>>();
        paneStateMock.Value.Returns(paneState ?? new FilterPaneState());

        var logger = Substitute.For<ITraceLogger>();
        var dispatcher = Substitute.For<IDispatcher>();
        var effects = new Effects(store, stateMock, paneStateMock, logger);

        return (effects, store, dispatcher, stateMock, logger);
    }

    // Used for the unknown-concrete-type wildcard test (covers the throw arm in HandleApplyLibraryEntry).
    private sealed record UnknownLibraryEntry : LibraryEntry;
}
