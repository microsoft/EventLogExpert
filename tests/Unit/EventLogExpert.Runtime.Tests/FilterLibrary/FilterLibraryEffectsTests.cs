// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;
using Effects = EventLogExpert.Runtime.FilterLibrary.Effects;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class FilterLibraryEffectsTests
{
    [Fact]
    public async Task HandleAddFilterToExistingFilterSet_AppendsToFilterSet()
    {
        var existingFilter = SavedFilter.TryCreate("Level == 2");
        var newFilter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(existingFilter);
        Assert.NotNull(newFilter);
        var filterSet = new LibraryEntryFilterSet
        {
            Name = "Filter Set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [existingFilter],
        };
        var (effects, store, _, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [filterSet] });

        await effects.HandleAddFilterToExistingFilterSet(
            new AddFilterToExistingFilterSetAction(filterSet.Id, newFilter, SourceEntryId: null),
            Substitute.For<IDispatcher>());

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e is LibraryEntryFilterSet
            && ((LibraryEntryFilterSet)e).Id == filterSet.Id && ((LibraryEntryFilterSet)e).Filters.Count == 2
            && ((LibraryEntryFilterSet)e).Filters.Any(f => f.ComparisonText == "Level == 4")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAddFilterToExistingFilterSet_DuplicateTuple_DoesNotAppendButStillPromotesSource()
    {
        var existingFilter = SavedFilter.TryCreate("Level == 4");
        var duplicate = SavedFilter.TryCreate("LEVEL == 4");
        Assert.NotNull(existingFilter);
        Assert.NotNull(duplicate);
        var filterSet = new LibraryEntryFilterSet
        {
            Name = "Filter Set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [existingFilter],
        };
        var source = BuildFilterEntry("Source") with { Origin = LibraryEntryOrigin.AutoTracked };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [filterSet, source] });

        await effects.HandleAddFilterToExistingFilterSet(
            new AddFilterToExistingFilterSetAction(filterSet.Id, duplicate, source.Id),
            dispatcher);

        // Did NOT update the filter set (duplicate).
        await store.DidNotReceive().UpdateAsync(Arg.Is<LibraryEntry>(e => e.Id == filterSet.Id), Arg.Any<CancellationToken>());
        // But DID promote the source.
        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e.Id == source.Id && e.Origin == LibraryEntryOrigin.UserSaved), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAddFilterToExistingFilterSet_SameTextDifferentMode_AppendsAsDistinctFilter()
    {
        // Mode-drift policy: distinct Mode + same ComparisonText must coexist in a filter set.
        var advanced = SavedFilter.TryCreate("Level == 4", mode: FilterMode.Advanced);
        var basic = SavedFilter.TryCreate("Level == 4", mode: FilterMode.Basic);
        Assert.NotNull(advanced);
        Assert.NotNull(basic);
        var filterSet = new LibraryEntryFilterSet
        {
            Name = "Filter Set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [advanced],
        };
        var (effects, store, _, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [filterSet] });

        await effects.HandleAddFilterToExistingFilterSet(
            new AddFilterToExistingFilterSetAction(filterSet.Id, basic, SourceEntryId: null),
            Substitute.For<IDispatcher>());

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e.GetType() == typeof(LibraryEntryFilterSet)
            && ((LibraryEntryFilterSet)e).Id == filterSet.Id
            && ((LibraryEntryFilterSet)e).Filters.Count == 2
            && ((LibraryEntryFilterSet)e).Filters.Any(f => f.Mode == FilterMode.Advanced)
            && ((LibraryEntryFilterSet)e).Filters.Any(f => f.Mode == FilterMode.Basic)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAddFilterToExistingFilterSet_UnknownFilterSet_IsNoOp()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var (effects, store, _, _, _) = CreateEffects();

        await effects.HandleAddFilterToExistingFilterSet(
            new AddFilterToExistingFilterSetAction(LibraryEntryId.Create(), filter, SourceEntryId: null),
            Substitute.For<IDispatcher>());

        await store.DidNotReceive().UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAddFilterToNewFilterSet_CreatesFilterSetWithSingleFilter()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleAddFilterToNewFilterSet(new AddFilterToNewFilterSetAction("New", filter, SourceEntryId: null), dispatcher);

        await store.Received(1).AddAsync(Arg.Is<LibraryEntry>(e => e.GetType() == typeof(LibraryEntryFilterSet)
            && ((LibraryEntryFilterSet)e).Name == "New"
            && ((LibraryEntryFilterSet)e).Filters.Count == 1
            && ((LibraryEntryFilterSet)e).Filters[0].Id != filter.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAddFilterToNewFilterSet_PromotesAutoTrackedSource()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var source = BuildFilterEntry("Source") with { Origin = LibraryEntryOrigin.AutoTracked };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [source] });

        await effects.HandleAddFilterToNewFilterSet(new AddFilterToNewFilterSetAction("New", filter, source.Id), dispatcher);

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e =>
            e.Id == source.Id && e.Origin == LibraryEntryOrigin.UserSaved), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAddFilterToNewFilterSet_WhitespaceName_IsNoOp()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var (effects, store, _, _, _) = CreateEffects();

        await effects.HandleAddFilterToNewFilterSet(new AddFilterToNewFilterSetAction("   ", filter, SourceEntryId: null), Substitute.For<IDispatcher>());

        await store.DidNotReceive().AddAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAddLibraryEntry_PersistsAndDispatchesSuccess()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleAddLibraryEntry(new AddLibraryEntryAction(entry), dispatcher);

        await store.Received(1).AddAsync(Arg.Is(entry), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => ReferenceEquals(a.Entry, entry)));
    }

    [Fact]
    public async Task HandleAddLibraryEntry_WhenStoreThrows_DoesNotDispatchSuccess()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.When(s => s.AddAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleAddLibraryEntry(new AddLibraryEntryAction(entry), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddLibraryEntrySuccessAction>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleApplyLibraryEntry_FilterSetEntry_DispatchesMergeFiltersWithAllFiltersAndRecordEntryApplied()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);

        var filterSet = new LibraryEntryFilterSet
        {
            Name = "Filter Set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [f1, f2],
        };
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [filterSet] });

        await effects.HandleApplyLibraryEntry(new ApplyLibraryEntryAction(filterSet.Id), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<MergeFiltersAction>(a => a.Filters.Count == 2));
        dispatcher.Received(1).Dispatch(Arg.Is<RecordEntryAppliedAction>(a => a.EntryId == filterSet.Id));
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

        await store.Received(1).DeleteAsync(Arg.Is(id), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<DeleteLibraryEntrySuccessAction>(a => a.EntryId == id));
    }

    [Fact]
    public async Task HandleDeleteLibraryEntry_WhenStoreThrows_DoesNotDispatchSuccess()
    {
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.When(s => s.DeleteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleDeleteLibraryEntry(new DeleteLibraryEntryAction(LibraryEntryId.Create()), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<DeleteLibraryEntrySuccessAction>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleDeleteTag_EmptyName_NoOp()
    {
        var entry = BuildFilterEntry("E") with { Tags = ["bug"] };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleDeleteTag(new DeleteTagAction("  "), dispatcher);

        await store.DidNotReceive().UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleDeleteTag_MatchesMixedCaseStoredTag_RemovesIt()
    {
        var entry = BuildFilterEntry("E") with { Tags = ["BUG", "perf"] };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleDeleteTag(new DeleteTagAction("bug"), dispatcher);

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list =>
            list.Count == 1 && list[0].Tags.SequenceEqual(new[] { "perf" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleDeleteTag_NoMatchingEntries_NoStoreCall_NoAnnounce()
    {
        var entry = BuildFilterEntry("E") with { Tags = ["perf"] };
        var announcer = Substitute.For<IAnnouncementService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [entry] }, announcementService: announcer);

        await effects.HandleDeleteTag(new DeleteTagAction("bug"), dispatcher);

        await store.DidNotReceiveWithAnyArgs().UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>());
        announcer.DidNotReceiveWithAnyArgs().Announce(default!);
    }

    [Fact]
    public async Task HandleDeleteTag_RemovesFromAllMatchingEntries()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug", "perf"] };
        var b = BuildFilterEntry("B") with { Tags = ["bug"] };
        var c = BuildFilterEntry("C") with { Tags = ["perf"] };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [a, b, c] });

        await effects.HandleDeleteTag(new DeleteTagAction("BUG"), dispatcher);

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list =>
            list.Count == 2 &&
            list.Any(e => e.Id == a.Id && e.Tags.SequenceEqual(new[] { "perf" })) &&
            list.Any(e => e.Id == b.Id && e.Tags.IsEmpty) &&
            list.All(e => e.Id != c.Id)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleDeleteTag_StorePersistsNoRows_ReloadsLibrary_AnnouncesFailure_DispatchesFailedAction()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug"] };
        var announcer = Substitute.For<IAnnouncementService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [a] }, announcementService: announcer);
        store.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns([]);

        await effects.HandleDeleteTag(new DeleteTagAction("bug"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<TagBulkUpdateFailedAction>());
        announcer.Received(1).Announce("Couldn't update tags. The library was reloaded.");
    }

    [Fact]
    public async Task HandleDeleteTag_StoreSkipsAlreadyGoneRow_DispatchesUpdatedOnly_ReloadsAndAnnouncesSingular()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug"] };
        var b = BuildFilterEntry("B") with { Tags = ["bug"] };
        var announcer = Substitute.For<IAnnouncementService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [a, b] }, announcementService: announcer);
        store.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns([a.Id]);

        await effects.HandleDeleteTag(new DeleteTagAction("bug"), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(s => s.Entry.Id == a.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(s => s.Entry.Id == b.Id));
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        announcer.Received(1).Announce("Removed tag 'bug' from 1 entry");
    }

    [Fact]
    public async Task HandleDeleteTag_StoreThrows_NoSuccessDispatched_ReloadsLibrary_AnnouncesFailure()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug"] };
        var b = BuildFilterEntry("B") with { Tags = ["bug"] };
        var announcer = Substitute.For<IAnnouncementService>();
        var (effects, store, dispatcher, _, logger) = CreateEffects(
            state: new FilterLibraryState { Entries = [a, b] }, announcementService: announcer);
        store.When(s => s.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleDeleteTag(new DeleteTagAction("bug"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<TagBulkUpdateFailedAction>());
        announcer.Received(1).Announce("Couldn't update tags. The library was reloaded.");
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleDeleteTag_Success_AnnouncesAffectedCount_PluralAndDispatchesPerEntry()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug"] };
        var b = BuildFilterEntry("B") with { Tags = ["bug", "perf"] };
        var announcer = Substitute.For<IAnnouncementService>();
        var (effects, _, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [a, b] }, announcementService: announcer);

        await effects.HandleDeleteTag(new DeleteTagAction("bug"), dispatcher);

        announcer.Received(1).Announce("Removed tag 'bug' from 2 entries");
        dispatcher.Received(2).Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleLoadLibrary_BackslashMigratorRunsAfterLegacyMigrator()
    {
        var store = Substitute.For<IFilterLibraryStore>();
        var entryWithBackslash = BuildFilterSetEntry(@"Network\DNS");
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([entryWithBackslash]);

        var legacyMigrator = Substitute.For<ILegacyFilterMigrator>();
        legacyMigrator.ShouldRunMigration().Returns(false);

        var backslashMigrator = Substitute.For<IBackslashNameMigrator>();
        backslashMigrator.ShouldRunMigration().Returns(true);
        var migratedEntry = BuildFilterSetEntry("DNS") with { Tags = ["network"] };
        backslashMigrator.BuildMigrationPlan(Arg.Any<IReadOnlyList<LibraryEntry>>())
            .Returns(new BackslashMigrationResult([migratedEntry], 0));

        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(
            migrator: legacyMigrator,
            store: store,
            backslashMigrator: backslashMigrator);

        await effects.HandleLoadLibrary(dispatcher);

        backslashMigrator.Received(1).BuildMigrationPlan(Arg.Any<IReadOnlyList<LibraryEntry>>());
        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e.Name == "DNS"), Arg.Any<CancellationToken>());
        backslashMigrator.Received(1).MarkMigrationCompleted();
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibrarySuccessAction>());
    }

    [Fact]
    public async Task HandleLoadLibrary_BackslashMigratorShouldNotRun_SkipsMigration()
    {
        var backslashMigrator = Substitute.For<IBackslashNameMigrator>();
        backslashMigrator.ShouldRunMigration().Returns(false);

        var legacyMigrator = Substitute.For<ILegacyFilterMigrator>();
        legacyMigrator.ShouldRunMigration().Returns(false);

        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(
            migrator: legacyMigrator,
            backslashMigrator: backslashMigrator);

        await effects.HandleLoadLibrary(dispatcher);

        backslashMigrator.DidNotReceive().BuildMigrationPlan(Arg.Any<IReadOnlyList<LibraryEntry>>());
        backslashMigrator.DidNotReceive().MarkMigrationCompleted();
    }

    [Fact]
    public async Task HandleLoadLibrary_BuildEntriesFromLegacyThrows_OuterCatchFires_GateReleased_SecondLoadSucceeds()
    {
        var shouldThrow = true;
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy().Returns(_ => shouldThrow
            ? throw new InvalidOperationException("migrator broke")
            : new LegacyMigrationResult(ImmutableList<LibraryEntry>.Empty, LegacyMigrationSections.Recents));
        var (effects, store, dispatcher, _, _, logger) = CreateEffectsWithMigrator(migrator);
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await effects.HandleLoadLibrary(dispatcher);
        shouldThrow = false;
        await effects.HandleLoadLibrary(dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryFailureAction>());
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.IsEmpty));
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleLoadLibrary_Concurrent_TwoSimultaneousDispatches_MigratorBuildCalledExactlyOnce()
    {
        var migratedEntry = BuildFilterEntry("migrated");
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        var migrationComplete = 0;
        // Realistic mock: ShouldRunMigration returns true until the migrator marks completion (matches the
        // real LegacyFilterMigrator's per-section flag semantics). The previous test relied on the now-removed
        // entries.IsEmpty guard to suppress the second BuildEntriesFromLegacy call.
        migrator.ShouldRunMigration().Returns(_ => Volatile.Read(ref migrationComplete) == 0);
        migrator.When(m => m.MarkMigrationCompleted(Arg.Any<LegacyMigrationSections>()))
            .Do(_ => Volatile.Write(ref migrationComplete, 1));
        migrator.BuildEntriesFromLegacy()
            .Returns(new LegacyMigrationResult(
                ImmutableList.Create<LibraryEntry>(migratedEntry),
                LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents));
        var store = Substitute.For<IFilterLibraryStore>();
        var loadAllCount = 0;
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            loadAllCount++;
            return loadAllCount == 1 ? [] : new[] { migratedEntry };
        });
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator, store: store);

        var ct = TestContext.Current.CancellationToken;
        var task1 = Task.Run(() => effects.HandleLoadLibrary(dispatcher), ct);
        var task2 = Task.Run(() => effects.HandleLoadLibrary(dispatcher), ct);
        await Task.WhenAll(task1, task2);

        migrator.Received(1).BuildEntriesFromLegacy();
        await store.Received(1).AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>());
        dispatcher.Received(2).Dispatch(Arg.Any<LoadLibrarySuccessAction>());
    }

    [Fact]
    public async Task HandleLoadLibrary_DispatchesStartedActionBeforeAcquiringGate()
    {
        using var migratorReleaser = new SemaphoreSlim(initialCount: 0, maxCount: 1);
        var migratorReachedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        var migratorCallCount = 0;
        migrator.BuildEntriesFromLegacy().Returns(_ =>
        {
            if (Interlocked.Increment(ref migratorCallCount) == 1)
            {
                migratorReachedSignal.TrySetResult();
                migratorReleaser.Wait();
            }

            return new LegacyMigrationResult(
                ImmutableList.Create<LibraryEntry>(BuildFilterEntry("migrated")),
                LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents);
        });
        var store = Substitute.For<IFilterLibraryStore>();
        var loadAllCount = 0;
        var migratedEntry = BuildFilterEntry("migrated");
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(_ => Interlocked.Increment(ref loadAllCount) == 1 ? [] : new[] { migratedEntry });
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator, store: store);

        var ct = TestContext.Current.CancellationToken;
        var load1 = Task.Run(() => effects.HandleLoadLibrary(dispatcher), ct);
        var load2 = default(Task);
        try
        {
            await migratorReachedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            load2 = Task.Run(() => effects.HandleLoadLibrary(dispatcher), ct);
            await PollUntilAsync(
                () => dispatcher.ReceivedCalls().Count(c => c.GetArguments().FirstOrDefault() is LoadLibraryStartedAction) >= 2,
                TimeSpan.FromSeconds(5),
                ct);

            dispatcher.Received(2).Dispatch(Arg.Any<LoadLibraryStartedAction>());
            dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibrarySuccessAction>());
        }
        finally
        {
            // Always release the migrator so the load tasks unblock even if an assertion or polling timeout
            // throws - otherwise xUnit's per-test cleanup hangs waiting for the still-running load tasks.
            migratorReleaser.Release();
            if (load2 is not null) { await Task.WhenAll(load1, load2); }
            else { await load1; }
        }

        dispatcher.Received(2).Dispatch(Arg.Any<LoadLibrarySuccessAction>());
    }

    [Fact]
    public async Task HandleLoadLibrary_DispatchesStartedAndSuccessWithStoreEntries()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, _) = CreateEffects();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([entry]);

        await effects.HandleLoadLibrary(dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryStartedAction>());
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 1 && a.Entries[0].Id == entry.Id));
    }

    [Fact]
    public async Task HandleLoadLibrary_EmptyStore_AddRangeSucceeds_PostMigrationLoadAllThrows_FallsBackToInMemoryResult_StillMarksCompleted()
    {
        var migratedEntry = BuildFilterEntry("migrated");
        var sections = LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents;
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy()
            .Returns(new LegacyMigrationResult(ImmutableList.Create<LibraryEntry>(migratedEntry), sections));
        var store = Substitute.For<IFilterLibraryStore>();
        var loadAllCallCount = 0;
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            loadAllCallCount++;
            if (loadAllCallCount == 1) { return []; }
            throw new InvalidOperationException("reload blew up");
        });
        var (effects, _, dispatcher, _, _, logger) = CreateEffectsWithMigrator(migrator, store: store);

        await effects.HandleLoadLibrary(dispatcher);

        await store.Received(1).AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 1 && a.Entries[0].Id == migratedEntry.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibraryFailureAction>());
        migrator.Received(1).MarkMigrationCompleted(sections);
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleLoadLibrary_EmptyStore_AddRangeThrows_DispatchesSuccessEmpty_DoesNotMarkMigrationCompleted()
    {
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy()
            .Returns(new LegacyMigrationResult(
                ImmutableList.Create<LibraryEntry>(BuildFilterEntry("migrated")),
                LegacyMigrationSections.Favorites | LegacyMigrationSections.Recents));
        var store = Substitute.For<IFilterLibraryStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        store.When(s => s.AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("sqlite locked"));
        var (effects, _, dispatcher, _, _, logger) = CreateEffectsWithMigrator(migrator, store: store);

        await effects.HandleLoadLibrary(dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.IsEmpty));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibraryFailureAction>());
        migrator.DidNotReceive().MarkMigrationCompleted(Arg.Any<LegacyMigrationSections>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleLoadLibrary_EmptyStore_MigratorReturnsEmptyEntries_DoesNotCallAddRange_MarksMigrationCompletedWithBuildResultSections()
    {
        var sections = LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents;
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy().Returns(new LegacyMigrationResult(ImmutableList<LibraryEntry>.Empty, sections));
        var (effects, store, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator);

        await effects.HandleLoadLibrary(dispatcher);

        await store.DidNotReceive().AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.IsEmpty));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibraryFailureAction>());
        migrator.Received(1).MarkMigrationCompleted(sections);
    }

    [Fact]
    public async Task HandleLoadLibrary_EmptyStore_MigratorReturnsEntries_AddRangeSucceeds_DispatchesReloadedEntries_MarksMigrationCompleted()
    {
        var migratedEntry = BuildFilterEntry("migrated");
        var sections = LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents;
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy()
            .Returns(new LegacyMigrationResult(ImmutableList.Create<LibraryEntry>(migratedEntry), sections));
        var store = Substitute.For<IFilterLibraryStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([], [migratedEntry]);
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator, store: store);

        await effects.HandleLoadLibrary(dispatcher);

        await store.Received(1).AddRangeAsync(Arg.Is<IEnumerable<LibraryEntry>>(e => e.Count() == 1), Arg.Any<CancellationToken>());
        await store.Received(2).LoadAllAsync(Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 1 && a.Entries[0].Id == migratedEntry.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibraryFailureAction>());
        migrator.Received(1).MarkMigrationCompleted(sections);
    }

    [Fact]
    public async Task HandleLoadLibrary_EmptyStore_NoMigratableData_DispatchesSuccessWithEmptyEntries()
    {
        var (effects, store, dispatcher, _, migrator, _) = CreateEffectsWithMigrator();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await effects.HandleLoadLibrary(dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.IsEmpty));
        await store.DidNotReceive().AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>());
        migrator.Received(1).MarkMigrationCompleted(Arg.Any<LegacyMigrationSections>());
    }

    [Fact]
    public async Task HandleLoadLibrary_MigratorShouldRunReturnsFalse_SkipsBuildEntriesFromLegacy_DispatchesSuccessEmpty()
    {
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(false);
        var (effects, store, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator);
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await effects.HandleLoadLibrary(dispatcher);

        migrator.Received(1).ShouldRunMigration();
        migrator.DidNotReceive().BuildEntriesFromLegacy();
        migrator.DidNotReceive().MarkMigrationCompleted(Arg.Any<LegacyMigrationSections>());
        await store.DidNotReceive().AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.IsEmpty));
    }

    [Fact]
    public async Task HandleLoadLibrary_NonEmptyStore_FilterSetsSameNameDifferentFilters_BothSurvive()
    {
        // Presets are user-created with no name-uniqueness invariant; same-name presets with distinct
        // filter content must both persist. Dedup must key on name + filters fingerprint, not name alone.
        var existingFilter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(existingFilter);
        var existingFilterSet = new LibraryEntryFilterSet
        {
            Name = "Errors",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = ImmutableList.Create(existingFilter),
        };
        var migratedFilter = SavedFilter.TryCreate("Level == 5");
        Assert.NotNull(migratedFilter);
        var migratedPreset = new LibraryEntryFilterSet
        {
            Name = "Errors",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = ImmutableList.Create(migratedFilter), // different filters
        };
        var sections = LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents;
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy()
            .Returns(new LegacyMigrationResult(ImmutableList.Create<LibraryEntry>(migratedPreset), sections));
        var store = Substitute.For<IFilterLibraryStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([existingFilterSet], [existingFilterSet, migratedPreset]);
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator, store: store);

        await effects.HandleLoadLibrary(dispatcher);

        // Distinct filter content → migrated filter set is NOT deduped → AddRange called with it.
        await store.Received(1).AddRangeAsync(Arg.Is<IEnumerable<LibraryEntry>>(e => e.Count() == 1 && e.First() is LibraryEntryFilterSet), Arg.Any<CancellationToken>());
        migrator.Received(1).MarkMigrationCompleted(sections);
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 2));
    }

    [Fact]
    public async Task HandleLoadLibrary_NonEmptyStore_MigrationEntriesOverlap_DedupsAgainstExisting_NoDuplicateInsertion()
    {
        // Critical defense against the SetString-throws-then-retry duplication scenario: if a prior launch
        // successfully ran AddRange but failed MarkMigrationCompleted, the next launch reads the populated
        // store, ShouldRunMigration still returns true, BuildEntriesFromLegacy re-emits the same entries
        // (fresh GUIDs but same content), and the migration must NOT re-insert them as content-duplicates.
        var existingFavorite = BuildFilterEntryWithText("Favorite", "Level == 4");
        var existingFilterSet = BuildFilterSetEntry("Errors");
        var duplicateFavorite = BuildFilterEntryWithText("Favorite", "Level == 4");
        var duplicateFilterSet = BuildFilterSetEntry("Errors");
        var sections = LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents;
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy()
            .Returns(new LegacyMigrationResult(ImmutableList.Create<LibraryEntry>(duplicateFavorite, duplicateFilterSet), sections));
        var store = Substitute.For<IFilterLibraryStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([existingFavorite, existingFilterSet]);
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator, store: store);

        await effects.HandleLoadLibrary(dispatcher);

        // Both migration entries collide with existing store content → AddRange must NOT be called with them.
        await store.DidNotReceive().AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>());
        // Bitmask still advances - the section is "complete" from the persistence perspective.
        migrator.Received(1).MarkMigrationCompleted(sections);
        // Loaded entries reflect the existing store (no duplicates).
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 2));
    }

    [Fact]
    public async Task HandleLoadLibrary_NonEmptyStore_MigrationEntriesPartiallyOverlap_OnlyNonOverlappingPersisted()
    {
        var existing = BuildFilterEntryWithText("Existing", "Level == 4");
        var dupeOverlap = BuildFilterEntryWithText("Existing", "Level == 4");
        var newEntry = BuildFilterEntryWithText("New", "Level == 5"); // does not collide
        var sections = LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents;
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy()
            .Returns(new LegacyMigrationResult(ImmutableList.Create<LibraryEntry>(dupeOverlap, newEntry), sections));
        var store = Substitute.For<IFilterLibraryStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([existing], [existing, newEntry]);
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator, store: store);

        await effects.HandleLoadLibrary(dispatcher);

        // Only the non-overlapping entry is inserted.
        await store.Received(1).AddRangeAsync(Arg.Is<IEnumerable<LibraryEntry>>(e => e.Count() == 1 && e.First().Id == newEntry.Id), Arg.Any<CancellationToken>());
        migrator.Received(1).MarkMigrationCompleted(sections);
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 2));
    }

    [Fact]
    public async Task HandleLoadLibrary_NonEmptyStore_PartialMigration_AddRangeThrows_PreservesExistingEntriesWithoutMarkingComplete()
    {
        var existingEntry = BuildFilterEntryWithText("existing", "Level == 4");
        var migrationEntry = BuildFilterEntryWithText("would-be-migrated", "Level == 5");
        var partial = LegacyMigrationSections.Favorites | LegacyMigrationSections.Recents;
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy()
            .Returns(new LegacyMigrationResult(ImmutableList.Create<LibraryEntry>(migrationEntry), partial));
        var store = Substitute.For<IFilterLibraryStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([existingEntry]);
        store.When(s => s.AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator, store: store);

        await effects.HandleLoadLibrary(dispatcher);

        migrator.DidNotReceive().MarkMigrationCompleted(Arg.Any<LegacyMigrationSections>());
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 1 && a.Entries[0].Id == existingEntry.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibraryFailureAction>());
    }

    [Fact]
    public async Task HandleLoadLibrary_NonEmptyStore_ShouldRunReturnsFalse_DoesNotInvokeBuildOrAddRange()
    {
        var existingEntry = BuildFilterEntry("existing");
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(false);
        var (effects, store, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator);
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([existingEntry]);

        await effects.HandleLoadLibrary(dispatcher);

        migrator.Received(1).ShouldRunMigration();
        migrator.DidNotReceive().BuildEntriesFromLegacy();
        await store.DidNotReceive().AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>());
        migrator.DidNotReceive().MarkMigrationCompleted(Arg.Any<LegacyMigrationSections>());
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 1));
    }

    [Fact]
    public async Task HandleLoadLibrary_NonEmptyStore_ShouldRunReturnsTrue_StillInvokesMigrator()
    {
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy()
            .Returns(new LegacyMigrationResult(ImmutableList<LibraryEntry>.Empty, LegacyMigrationSections.Recents));
        var (effects, store, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator);
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([BuildFilterEntry("existing")]);

        await effects.HandleLoadLibrary(dispatcher);

        migrator.Received(1).ShouldRunMigration();
        migrator.Received(1).BuildEntriesFromLegacy();
        await store.DidNotReceive().AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>());
        migrator.Received(1).MarkMigrationCompleted(Arg.Any<LegacyMigrationSections>());
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 1));
    }

    [Fact]
    public async Task HandleLoadLibrary_NonEmptyStore_StillInvokesMigratorWhenShouldRun_MergesNonOverlappingEntries()
    {
        var existingEntry = BuildFilterEntryWithText("existing", "Level == 4");
        var migratedEntry = BuildFilterEntryWithText("migrated-groups", "Level == 5");
        var sections = LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents;
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy()
            .Returns(new LegacyMigrationResult(ImmutableList.Create<LibraryEntry>(migratedEntry), sections));
        var store = Substitute.For<IFilterLibraryStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([existingEntry], [existingEntry, migratedEntry]);
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator, store: store);

        await effects.HandleLoadLibrary(dispatcher);

        migrator.Received(1).ShouldRunMigration();
        migrator.Received(1).BuildEntriesFromLegacy();
        await store.Received(1).AddRangeAsync(Arg.Is<IEnumerable<LibraryEntry>>(e => e.Count() == 1), Arg.Any<CancellationToken>());
        migrator.Received(1).MarkMigrationCompleted(sections);
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 2));
    }

    [Fact]
    public async Task HandleLoadLibrary_WhenStoreThrows_DispatchesStartedThenFailureAndLogs()
    {
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.When(s => s.LoadAllAsync(Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("boom"));

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
        store.TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(entry.Id), dispatcher);

        await store.Received(1).TryBumpLastUsedIfNotFavoriteAsync(entry.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        await store.DidNotReceive().TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRecordEntryApplied_FavoritedEntry_IsNoOp()
    {
        var entry = BuildFilterEntry("First") with { IsFavorite = true };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(entry.Id), dispatcher);

        await store.DidNotReceive().TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleRecordEntryApplied_FilterSetEntry_IsNoOp_NoLastUsedBump()
    {
        var filterSet = BuildFilterSetEntry("FilterSetName");
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [filterSet] });

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(filterSet.Id), dispatcher);

        await store.DidNotReceive().TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleRecordEntryApplied_NotFavoriteBumpSucceeds_DispatchesUpdate()
    {
        var entry = BuildFilterEntry("First") with { Origin = LibraryEntryOrigin.AutoTracked };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });
        store.TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(entry.Id), dispatcher);

        await store.Received(1).TryBumpLastUsedIfNotFavoriteAsync(entry.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
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
        store.TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        store.TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>()).Returns(false);

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(bumpTarget.Id), dispatcher);

        await store.Received(1).TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>());
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

        // The 51st entry - about to be bumped (will project into the prune snapshot).
        // Pre-bump LastUsedUtc doesn't affect the prune order - the effect overwrites it with UtcNow
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
        store.TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        store.TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>()).Returns(true);

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(bumpTarget.Id), dispatcher);

        // SQL-guarded delete no-ops if the row was concurrently promoted/favorited.
        await store.Received(1).TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Is(oldestId), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<DeleteLibraryEntrySuccessAction>(a => a.EntryId == oldestId));
    }

    [Fact]
    public async Task HandleRecordEntryApplied_UnknownId_IsNoOp()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(LibraryEntryId.Create()), dispatcher);

        await store.DidNotReceive().TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
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
        store.AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>()).Returns((existing, false));
        store.TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        await store.Received(1).AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>());
        await store.Received(1).TryBumpLastUsedIfNotFavoriteAsync(existing.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
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
        store.AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>()).Returns((existing, false));

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        await store.DidNotReceive().TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
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

        // The "already in state" collision target - distinct ComparisonText so in-memory match misses.
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
        store.AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>())
            .Returns((alreadyInState, false));
        store.TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        store.TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(newFilter), dispatcher);

        // Snapshot should be 50 (SetItem on the existing row, not Add); prune sees count == cap; no delete.
        await store.DidNotReceive().TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>());
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
        store.AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<LibraryEntrySavedFilter>(), true));

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(basic), dispatcher);

        await store.Received(1).AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRecordFilterApplied_EmptyComparisonText_IsNoOp()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(SavedFilter.Empty), dispatcher);

        await store.DidNotReceive().AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
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

        await store.DidNotReceive().TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRecordFilterApplied_NoExisting_InsertsAutoTrackedEntry()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();
        store.AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var candidate = call.Arg<LibraryEntrySavedFilter>();
                return (candidate, true);
            });
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        await store.Received(1).AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>());
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
        store.AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.Arg<LibraryEntrySavedFilter>(), true));
        store.TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(newFilter), dispatcher);

        await store.Received(1).AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>());
        await store.Received(1).TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Is(oldestId), Arg.Any<CancellationToken>());
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
        store.TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(false);

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
        store.TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        await store.Received(1).TryBumpLastUsedIfNotFavoriteAsync(existing.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a =>
            a.Entry.Id == existing.Id && a.Entry.LastUsedUtc != existing.LastUsedUtc));
        await store.DidNotReceive().AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>());
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
        store.TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        await store.Received(1).TryBumpLastUsedIfNotFavoriteAsync(existing.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a => a.Entry.Id == existing.Id));
    }

    [Fact]
    public async Task HandleRenameTag_CollidingTags_DedupesPreservingNewCanonical()
    {
        var entry = BuildFilterEntry("E") with { Tags = ["bug", "defect"] };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleRenameTag(new RenameTagAction("bug", "defect"), dispatcher);

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list =>
            list.Count == 1 && list[0].Tags.SequenceEqual(new[] { "defect" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRenameTag_EmptyNewName_NoOp()
    {
        var entry = BuildFilterEntry("E") with { Tags = ["bug"] };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleRenameTag(new RenameTagAction("bug", "  "), dispatcher);

        await store.DidNotReceiveWithAnyArgs().UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRenameTag_FilterSetEntry_RewritesTagsViaReplaceTagsHelper()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var fs = new LibraryEntryFilterSet
        {
            Name = "FS",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [filter],
            Tags = ["bug"],
        };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [fs] });

        await effects.HandleRenameTag(new RenameTagAction("bug", "defect"), dispatcher);

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list =>
            list.Count == 1 && list[0] is LibraryEntryFilterSet && list[0].Tags.SequenceEqual(new[] { "defect" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRenameTag_MatchesMixedCaseStoredTag_HealsToCanonical()
    {
        var entry = BuildFilterEntry("E") with { Tags = ["BUG"] };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleRenameTag(new RenameTagAction("bug", "defect"), dispatcher);

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list =>
            list.Count == 1 && list[0].Tags.SequenceEqual(new[] { "defect" })), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleRenameTag_NoOpWhenNormalizedOldAndNewAreEqual()
    {
        var entry = BuildFilterEntry("E") with { Tags = ["bug"] };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleRenameTag(new RenameTagAction("bug", "BUG"), dispatcher);

        await store.DidNotReceiveWithAnyArgs().UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleRenameTag_NormalizesBothNames_StoresLowercase()
    {
        var entry = BuildFilterEntry("E") with { Tags = ["bug"] };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleRenameTag(new RenameTagAction("  BUG  ", "Defect "), dispatcher);

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list =>
            list.Count == 1 && list[0].Tags.SequenceEqual(new[] { "defect" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRenameTag_RewritesAllMatchingEntries_DispatchesSuccessPerEntry()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug", "perf"] };
        var b = BuildFilterEntry("B") with { Tags = ["bug"] };
        var c = BuildFilterEntry("C") with { Tags = ["perf"] };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [a, b, c] });

        await effects.HandleRenameTag(new RenameTagAction("bug", "defect"), dispatcher);

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list =>
            list.Count == 2 &&
            list.Any(e => e.Id == a.Id && e.Tags.SequenceEqual(new[] { "defect", "perf" })) &&
            list.Any(e => e.Id == b.Id && e.Tags.SequenceEqual(new[] { "defect" })) &&
            list.All(e => e.Id != c.Id)), Arg.Any<CancellationToken>());
        dispatcher.Received(2).Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleRenameTag_SkipsEntriesWithoutTag()
    {
        var withTag = BuildFilterEntry("A") with { Tags = ["bug"] };
        var withoutTag = BuildFilterEntry("B");
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [withTag, withoutTag] });

        await effects.HandleRenameTag(new RenameTagAction("bug", "defect"), dispatcher);

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list =>
            list.Count == 1 && list[0].Id == withTag.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRenameTag_StoreSkipsAlreadyGoneRow_DispatchesUpdatedOnly_ReloadsAndAnnouncesSingular()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug"] };
        var b = BuildFilterEntry("B") with { Tags = ["bug"] };
        var announcer = Substitute.For<IAnnouncementService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [a, b] }, announcementService: announcer);
        store.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns([a.Id]);

        await effects.HandleRenameTag(new RenameTagAction("bug", "defect"), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(s => s.Entry.Id == a.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(s => s.Entry.Id == b.Id));
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        announcer.Received(1).Announce("Renamed tag 'bug' to 'defect' in 1 entry");
    }

    [Fact]
    public async Task HandleRenameTag_StoreThrows_NoSuccessDispatched_ReloadsLibrary_AnnouncesFailure()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug"] };
        var b = BuildFilterEntry("B") with { Tags = ["bug"] };
        var announcer = Substitute.For<IAnnouncementService>();
        var (effects, store, dispatcher, _, logger) = CreateEffects(
            state: new FilterLibraryState { Entries = [a, b] }, announcementService: announcer);
        store.When(s => s.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleRenameTag(new RenameTagAction("bug", "defect"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<TagBulkUpdateFailedAction>());
        announcer.Received(1).Announce("Couldn't update tags. The library was reloaded.");
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleRenameTag_Success_AnnouncesRenamedWithCount()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug"] };
        var b = BuildFilterEntry("B") with { Tags = ["bug"] };
        var announcer = Substitute.For<IAnnouncementService>();
        var (effects, _, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [a, b] }, announcementService: announcer);

        await effects.HandleRenameTag(new RenameTagAction("bug", "defect"), dispatcher);

        announcer.Received(1).Announce("Renamed tag 'bug' to 'defect' in 2 entries");
    }

    [Fact]
    public async Task HandleReplaceWithLibraryEntry_FilterSetEntry_DispatchesReplaceFiltersWithAllFiltersAndRecordEntryApplied()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);

        var filterSet = new LibraryEntryFilterSet
        {
            Name = "Filter Set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [f1, f2],
        };
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [filterSet] });

        await effects.HandleReplaceWithLibraryEntry(new ReplaceWithLibraryEntryAction(filterSet.Id), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<ReplaceFiltersAction>(a => a.Filters.Count == 2));
        dispatcher.Received(1).Dispatch(Arg.Is<RecordEntryAppliedAction>(a => a.EntryId == filterSet.Id));
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

        await store.DidNotReceive().UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleSaveEntry_AutoTrackedEntry_PromotesToUserSaved()
    {
        var entry = BuildFilterEntry("First") with { Origin = LibraryEntryOrigin.AutoTracked };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleSaveEntry(new SaveEntryAction(entry.Id), dispatcher);

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e =>
            e.Id == entry.Id && e.Origin == LibraryEntryOrigin.UserSaved), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleSaveEntry_ReprojectsOriginOntoLatestSnapshot_NotPreAwaitSnapshot()
    {
        var staleEntry = BuildFilterEntry("OldName") with { Origin = LibraryEntryOrigin.AutoTracked };
        var renamedEntry = (LibraryEntrySavedFilter)staleEntry with { Name = "NewName" };
        var staleState = new FilterLibraryState { Entries = [staleEntry] };
        var renamedState = new FilterLibraryState { Entries = [renamedEntry] };
        var (effects, _, dispatcher, stateMock, _) = CreateEffects(state: staleState);

        stateMock.Value.Returns(staleState, staleState, renamedState);

        await effects.HandleSaveEntry(new SaveEntryAction(staleEntry.Id), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a =>
            a.Entry.Id == staleEntry.Id &&
            a.Entry.Name == "NewName" &&
            a.Entry.Origin == LibraryEntryOrigin.UserSaved));
    }

    [Fact]
    public async Task HandleSaveEntry_UnknownId_IsNoOp()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleSaveEntry(new SaveEntryAction(LibraryEntryId.Create()), dispatcher);

        await store.DidNotReceive().UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleSaveEntry_WhenEntryDeletedDuringStoreAwait_DoesNotDispatch()
    {
        var entry = BuildFilterEntry("Auto") with { Origin = LibraryEntryOrigin.AutoTracked };
        var initialState = new FilterLibraryState { Entries = [entry] };
        var emptyState = new FilterLibraryState { Entries = [] };
        var (effects, store, dispatcher, stateMock, _) = CreateEffects(state: initialState);

        stateMock.Value.Returns(initialState, initialState, emptyState);

        await effects.HandleSaveEntry(new SaveEntryAction(entry.Id), dispatcher);

        await store.Received(1).UpdateAsync(
            Arg.Is<LibraryEntry>(e => e.Id == entry.Id && e.Origin == LibraryEntryOrigin.UserSaved),
            Arg.Any<CancellationToken>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleSaveFilterSet_CreatesNewFilterSetWithRegeneratedFilterIds()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);
        var originalIds = new[] { f1.Id, f2.Id };
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleSaveFilterSet(new SaveFilterSetAction("My Preset", [f1, f2]), dispatcher);

        await store.Received(1).AddAsync(Arg.Is<LibraryEntry>(e => e.GetType() == typeof(LibraryEntryFilterSet)
            && ((LibraryEntryFilterSet)e).Name == "My Preset"
            && ((LibraryEntryFilterSet)e).Origin == LibraryEntryOrigin.UserSaved
            && ((LibraryEntryFilterSet)e).Filters.Count == 2
            && ((LibraryEntryFilterSet)e).Filters.All(f => !originalIds.Contains(f.Id))
            && ((LibraryEntryFilterSet)e).Filters.All(f => !f.IsEnabled)), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => a.Entry.GetType() == typeof(LibraryEntryFilterSet)));
    }

    [Fact]
    public async Task HandleSaveFilterSet_EmptyFilters_IsNoOp()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleSaveFilterSet(new SaveFilterSetAction("Empty", []), dispatcher);

        await store.DidNotReceive().AddAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleSaveFilterSet_WhitespaceName_IsNoOp()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleSaveFilterSet(new SaveFilterSetAction("   ", [filter]), dispatcher);

        await store.DidNotReceive().AddAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleSavePaneAsFilterSet_EmptyPane_IsNoOp()
    {
        var (effects, _, dispatcher, _, _) = CreateEffects(paneState: new FilterPaneState());

        await effects.HandleSavePaneAsFilterSet(new SavePaneAsFilterSetAction("Pane Preset"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<SaveFilterSetAction>());
    }

    [Fact]
    public async Task HandleSavePaneAsFilterSet_PaneHasFilters_DispatchesSaveFilterSetWithPaneFilters()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        Assert.NotNull(f1);
        var paneState = new FilterPaneState { Filters = [f1] };
        var (effects, _, dispatcher, _, _) = CreateEffects(paneState: paneState);

        await effects.HandleSavePaneAsFilterSet(new SavePaneAsFilterSetAction("Pane Preset"), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<SaveFilterSetAction>(a =>
            a.Name == "Pane Preset" && a.Filters.Count == 1 && a.Filters[0] == f1));
    }

    [Fact]
    public async Task HandleSavePaneAsFilterSet_WhitespaceName_IsNoOp()
    {
        var f1 = SavedFilter.TryCreate("Level == 2");
        Assert.NotNull(f1);
        var (effects, _, dispatcher, _, _) = CreateEffects(paneState: new FilterPaneState { Filters = [f1] });

        await effects.HandleSavePaneAsFilterSet(new SavePaneAsFilterSetAction("   "), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<SaveFilterSetAction>());
    }

    [Fact]
    public async Task HandleSetIsFavorite_AlreadyAtTargetState_IsNoOp()
    {
        var entry = BuildFilterEntry("First") with { IsFavorite = true };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleSetIsFavorite(new SetIsFavoriteAction(entry.Id, IsFavorite: true), dispatcher);

        await store.DidNotReceive().UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleSetIsFavorite_False_OnFilter_BumpsLastUsedToNow()
    {
        var filter = BuildFilterEntry("First") with { IsFavorite = true };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [filter] });

        await effects.HandleSetIsFavorite(new SetIsFavoriteAction(filter.Id, IsFavorite: false), dispatcher);

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e =>
            e.Id == filter.Id && !e.IsFavorite && e.LastUsedUtc != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleSetIsFavorite_False_OnFilterSet_LeavesLastUsedNull()
    {
        var f = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f);
        var filterSet = new LibraryEntryFilterSet
        {
            Name = "Filter Set",
            CreatedUtc = DateTimeOffset.UtcNow,
            IsFavorite = true,
            Filters = [f],
        };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [filterSet] });

        await effects.HandleSetIsFavorite(new SetIsFavoriteAction(filterSet.Id, IsFavorite: false), dispatcher);

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e.Id == filterSet.Id && !e.IsFavorite && e.LastUsedUtc == null), Arg.Any<CancellationToken>());
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

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e =>
            e.Id == entry.Id && e.IsFavorite && e.LastUsedUtc == null && e.Origin == LibraryEntryOrigin.UserSaved), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a =>
            a.Entry.IsFavorite && a.Entry.LastUsedUtc == null && a.Entry.Origin == LibraryEntryOrigin.UserSaved));
    }

    [Fact]
    public async Task HandleSetIsFavorite_UnknownId_IsNoOp()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleSetIsFavorite(new SetIsFavoriteAction(LibraryEntryId.Create(), IsFavorite: true), dispatcher);

        await store.DidNotReceive().UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleUpdateLibraryEntry_PersistsAndDispatchesSuccess()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleUpdateLibraryEntry(new UpdateLibraryEntryAction(entry), dispatcher);

        await store.Received(1).UpdateAsync(Arg.Is(entry), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a => ReferenceEquals(a.Entry, entry)));
    }

    [Fact]
    public async Task HandleUpdateLibraryEntry_WhenStoreThrows_DoesNotDispatchSuccess()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.When(s => s.UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("boom"));

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

    private static LibraryEntrySavedFilter BuildFilterEntryWithText(string name, string comparisonText)
    {
        var filter = SavedFilter.TryCreate(comparisonText);
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
        };
    }

    private static LibraryEntryFilterSet BuildFilterSetEntry(string name) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = ImmutableList<SavedFilter>.Empty,
        };

    private static (Effects effects, IFilterLibraryStore store, IDispatcher dispatcher, IState<FilterLibraryState> stateMock, ITraceLogger logger) CreateEffects(
        FilterLibraryState? state = null,
        FilterPaneState? paneState = null,
        IAnnouncementService? announcementService = null)
    {
        var (effects, store, dispatcher, stateMock, _, logger) = CreateEffectsWithMigrator(
            migrator: null,
            state: state,
            paneState: paneState,
            announcementService: announcementService);

        return (effects, store, dispatcher, stateMock, logger);
    }

    private static (Effects effects, IFilterLibraryStore store, IDispatcher dispatcher, IState<FilterLibraryState> stateMock, ILegacyFilterMigrator migrator, ITraceLogger logger) CreateEffectsWithMigrator(
        ILegacyFilterMigrator? migrator = null,
        FilterLibraryState? state = null,
        FilterPaneState? paneState = null,
        IFilterLibraryStore? store = null,
        IBackslashNameMigrator? backslashMigrator = null,
        IAnnouncementService? announcementService = null)
    {
        var storeWasSupplied = store is not null;
        store ??= Substitute.For<IFilterLibraryStore>();
        if (!storeWasSupplied)
        {
            store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([]);
            store.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ci => ((IReadOnlyList<LibraryEntry>)ci[0]).Select(e => e.Id).ToList());
        }

        var stateMock = Substitute.For<IState<FilterLibraryState>>();
        stateMock.Value.Returns(state ?? new FilterLibraryState());

        var paneStateMock = Substitute.For<IState<FilterPaneState>>();
        paneStateMock.Value.Returns(paneState ?? new FilterPaneState());

        if (migrator is null)
        {
            migrator = Substitute.For<ILegacyFilterMigrator>();
            migrator.ShouldRunMigration().Returns(true);
            migrator.BuildEntriesFromLegacy()
                .Returns(new LegacyMigrationResult(
                    ImmutableList<LibraryEntry>.Empty,
                    LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents));
        }

        backslashMigrator ??= Substitute.For<IBackslashNameMigrator>();
        announcementService ??= Substitute.For<IAnnouncementService>();

        var logger = Substitute.For<ITraceLogger>();
        var dispatcher = Substitute.For<IDispatcher>();
        var effects = new Effects(store, stateMock, paneStateMock, migrator, backslashMigrator, announcementService, logger);

        return (effects, store, dispatcher, stateMock, migrator, logger);
    }

    private static async Task PollUntilAsync(Func<bool> predicate, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) { return; }
            await Task.Delay(10, cancellationToken);
        }
        if (!predicate()) { throw new TimeoutException($"Predicate did not become true within {timeout}."); }
    }

    // Used for the unknown-concrete-type wildcard test (covers the throw arm in HandleApplyLibraryEntry).
    private sealed record UnknownLibraryEntry : LibraryEntry;
}
