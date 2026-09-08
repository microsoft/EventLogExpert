// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Collections.Immutable;
using Effects = EventLogExpert.Runtime.FilterLibrary.Effects;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class FilterLibraryEffectsTests
{
    private const int PollDelayMilliseconds = 10;
    private const int SettleDelayMilliseconds = 150;

    private static readonly TimeSpan s_shortTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_testTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ApplyBulkTagUpdate_ReissueAgainstLatestSnapshotFails_ReloadsLibrary_ReportsErrorBanner_DispatchesNoProjection()
    {
        var staleEntry = BuildFilterEntry("A") with { Tags = ["bug"], Name = "Stale" };
        var renamedEntry = staleEntry with { Name = "Renamed" };
        var staleState = new FilterLibraryState { Entries = [staleEntry] };
        var renamedState = new FilterLibraryState { Entries = [renamedEntry] };
        var announcer = Substitute.For<IAnnouncementService>();
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, stateMock, _) = CreateEffects(
            state: staleState,
            announcementService: announcer,
            errorBannerService: errorBanner);

        stateMock.Value.Returns(staleState, renamedState);

        int bulkCalls = 0;
        store.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                bulkCalls++;
                if (bulkCalls == 1)
                {
                    var list = call.ArgAt<IReadOnlyList<LibraryEntry>>(0);
                    return Task.FromResult<IReadOnlyList<LibraryEntryId>>([.. list.Select(e => e.Id)]);
                }

                return Task.FromException<IReadOnlyList<LibraryEntryId>>(new InvalidOperationException("reissue-fail"));
            });

        await effects.HandleDeleteTag(new DeleteTagAction("bug"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<TagBulkUpdateFailedAction>());
        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsLibraryTagsBulkUpdateFailed(message)));
        announcer.DidNotReceiveWithAnyArgs().Announce(default!);
    }

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

        await store.DidNotReceive().UpdateAsync(Arg.Is<LibraryEntry>(e => e != null && e.Id == filterSet.Id), Arg.Any<CancellationToken>());
        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e != null && e.Id == source.Id && e.Origin == LibraryEntryOrigin.UserSaved), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAddFilterToExistingFilterSet_FilterPresentInGateSnapshot_DoesNotDoubleAddOrPersist()
    {
        var existing = SavedFilter.TryCreate("Level == 4");
        var duplicate = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(existing);
        Assert.NotNull(duplicate);
        var filterSetEmpty = new LibraryEntryFilterSet
        {
            Name = "Set",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [],
        };
        var filterSetWithExisting = filterSetEmpty with { Filters = [existing] };
        var stateWithoutFilter = new FilterLibraryState { Entries = [filterSetEmpty] };
        var stateWithFilter = new FilterLibraryState { Entries = [filterSetWithExisting] };
        var (effects, store, dispatcher, stateMock, _) = CreateEffects(state: stateWithoutFilter);

        stateMock.Value.Returns(stateWithoutFilter, stateWithFilter, stateWithFilter);

        await effects.HandleAddFilterToExistingFilterSet(
            new AddFilterToExistingFilterSetAction(filterSetEmpty.Id, duplicate, SourceEntryId: null),
            dispatcher);

        await store.DidNotReceive().UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleAddFilterToExistingFilterSet_SameTextDifferentMode_AppendsAsDistinctFilter()
    {
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

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e != null && e.GetType() == typeof(LibraryEntryFilterSet)
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
    public async Task HandleAddFilterToExistingFilterSet_WhenStoreThrows_ReportsErrorBanner()
    {
        var filterSet = BuildFilterSetEntry("Errors");
        var newFilter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(newFilter);
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [filterSet] },
            errorBannerService: errorBanner);
        store.UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk full"));

        await effects.HandleAddFilterToExistingFilterSet(
            new AddFilterToExistingFilterSetAction(filterSet.Id, newFilter, null), dispatcher);

        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsFilterAddToSetFailed(message)));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleAddFilterToNewFilterSet_CreatesFilterSetWithSingleFilter()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleAddFilterToNewFilterSet(new AddFilterToNewFilterSetAction("New", filter, SourceEntryId: null), dispatcher);

        await store.Received(1).AddAsync(Arg.Is<LibraryEntry>(e => e != null && e.GetType() == typeof(LibraryEntryFilterSet)
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

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e != null &&
            e.Id == source.Id && e.Origin == LibraryEntryOrigin.UserSaved), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAddFilterToNewFilterSet_WhenSourcePromotionFails_DoesNotReportErrorBanner()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var source = BuildFilterEntry("Source") with { Origin = LibraryEntryOrigin.AutoTracked };
        var store = Substitute.For<IFilterLibraryStore>();
        store.AddAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        store.UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk full"));
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(
            state: new FilterLibraryState { Entries = [source] },
            store: store,
            errorBannerService: errorBanner);

        await effects.HandleAddFilterToNewFilterSet(new AddFilterToNewFilterSetAction("New", filter, source.Id), dispatcher);

        errorBanner.DidNotReceiveWithAnyArgs().ReportError(default!);
    }

    [Fact]
    public async Task HandleAddFilterToNewFilterSet_WhenStoreThrows_ReportsErrorBanner()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var store = Substitute.For<IFilterLibraryStore>();
        store.AddAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk full"));
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(store: store, errorBannerService: errorBanner);

        await effects.HandleAddFilterToNewFilterSet(new AddFilterToNewFilterSetAction("New", filter, SourceEntryId: null), dispatcher);

        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsFilterSetCreateFailed(message, "New")));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddLibraryEntrySuccessAction>());
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
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => a != null && ReferenceEquals(a.Entry, entry)));
    }

    [Fact]
    public async Task HandleAddLibraryEntry_WhenStoreThrows_ReportsErrorBannerAndDoesNotDispatchSuccess()
    {
        var entry = BuildFilterEntry("First");
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, logger) = CreateEffects(errorBannerService: errorBanner);
        store.When(s => s.AddAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleAddLibraryEntry(new AddLibraryEntryAction(entry), dispatcher);

        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsLibraryEntrySaveFailed(message, "First")));
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

        dispatcher.Received(1).Dispatch(Arg.Is<MergeFiltersAction>(a => a != null && a.Filters.Count == 2));
        dispatcher.Received(1).Dispatch(Arg.Is<RecordEntryAppliedAction>(a => a != null && a.EntryId == filterSet.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReplaceFiltersAction>());
    }

    [Fact]
    public async Task HandleApplyLibraryEntry_SavedFilterEntry_DispatchesMergeFiltersWithSingleFilterAndRecordEntryApplied()
    {
        var entry = BuildFilterEntry("First");
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleApplyLibraryEntry(new ApplyLibraryEntryAction(entry.Id), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<MergeFiltersAction>(a => a != null && a.Filters.Count == 1));
        dispatcher.Received(1).Dispatch(Arg.Is<RecordEntryAppliedAction>(a => a != null && a.EntryId == entry.Id));
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
        dispatcher.Received(1).Dispatch(Arg.Is<DeleteLibraryEntrySuccessAction>(a => a != null && a.EntryId == id));
    }

    [Fact]
    public async Task HandleDeleteLibraryEntry_WhenStoreThrows_ReportsErrorBannerAndDoesNotDispatchSuccess()
    {
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, logger) = CreateEffects(errorBannerService: errorBanner);
        store.When(s => s.DeleteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleDeleteLibraryEntry(new DeleteLibraryEntryAction(LibraryEntryId.Create()), dispatcher);

        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsLibraryEntryDeleteFailed(message)));
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

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list => list != null &&
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

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list => list != null &&
            list.Count == 2 &&
            list.Any(e => e.Id == a.Id && e.Tags.SequenceEqual(new[] { "perf" })) &&
            list.Any(e => e.Id == b.Id && e.Tags.IsEmpty) &&
            list.All(e => e.Id != c.Id)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleDeleteTag_StorePersistsNoRows_ReloadsLibrary_ReportsErrorBanner_DispatchesFailedAction()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug"] };
        var announcer = Substitute.For<IAnnouncementService>();
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [a] },
            announcementService: announcer,
            errorBannerService: errorBanner);
        store.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns([]);

        await effects.HandleDeleteTag(new DeleteTagAction("bug"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<TagBulkUpdateFailedAction>());
        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsLibraryTagsBulkUpdateFailed(message)));
        announcer.DidNotReceiveWithAnyArgs().Announce(default!);
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

        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(s => s != null && s.Entry.Id == a.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(s => s != null && s.Entry.Id == b.Id));
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        announcer.Received(1).Announce("Removed tag 'bug' from 1 entry");
    }

    [Fact]
    public async Task HandleDeleteTag_StoreThrows_NoSuccessDispatched_ReloadsLibrary_ReportsErrorBanner()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug"] };
        var b = BuildFilterEntry("B") with { Tags = ["bug"] };
        var announcer = Substitute.For<IAnnouncementService>();
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, logger) = CreateEffects(
            state: new FilterLibraryState { Entries = [a, b] },
            announcementService: announcer,
            errorBannerService: errorBanner);
        store.When(s => s.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleDeleteTag(new DeleteTagAction("bug"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<TagBulkUpdateFailedAction>());
        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsLibraryTagsBulkUpdateFailed(message)));
        announcer.DidNotReceiveWithAnyArgs().Announce(default!);
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
    public async Task HandleImportLibraryEntries_AddSucceedsThenUpdateThrows_ReportsBannerAndReloadsWithoutSuccessOrAnnounce()
    {
        var add = BuildFilterEntry("Added");
        var update = BuildFilterEntry("Update");
        var announcer = Substitute.For<IAnnouncementService>();
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, logger) = CreateEffects(
            announcementService: announcer,
            errorBannerService: errorBanner);
        store.AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        store.When(s => s.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleImportLibraryEntries(
            new ImportLibraryEntriesAction([add], [update], new ImportSummary(1, 1, 0, 0, 0)), dispatcher);

        // Adds committed but the update phase threw: suppress the add/update success actions and resync via reload;
        // exactly one banner, no announce.
        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsFilterLibraryImportFailed(message)));
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddLibraryEntrySuccessAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        announcer.DidNotReceiveWithAnyArgs().Announce(default!);
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleImportLibraryEntries_AddsOnlySuccess_AnnouncesWithoutReportingOrReloading()
    {
        var add = BuildFilterEntry("Added");
        var announcer = Substitute.For<IAnnouncementService>();
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            announcementService: announcer,
            errorBannerService: errorBanner);
        store.AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        store.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns([]);

        await effects.HandleImportLibraryEntries(new ImportLibraryEntriesAction([add], [], new ImportSummary(1, 0, 0, 0, 0)), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(action => action != null && ReferenceEquals(action.Entry, add)));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibraryAction>());
        announcer.Received(1).Announce("Imported 1 new, replaced 0, updated 0 tags, skipped 0");
        errorBanner.DidNotReceiveWithAnyArgs().ReportError(default!);
    }

    [Fact]
    public async Task HandleImportLibraryEntries_EmptyImport_AnnouncesWithoutStoreOrWriteDispatch()
    {
        var announcer = Substitute.For<IAnnouncementService>();
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            announcementService: announcer,
            errorBannerService: errorBanner);

        await effects.HandleImportLibraryEntries(new ImportLibraryEntriesAction([], [], new ImportSummary(0, 0, 0, 3, 0)), dispatcher);

        await store.DidNotReceiveWithAnyArgs().AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>());
        await store.DidNotReceiveWithAnyArgs().UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddLibraryEntrySuccessAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibraryAction>());
        announcer.Received(1).Announce("Imported 0 new, replaced 0, updated 0 tags, skipped 3");
        errorBanner.DidNotReceiveWithAnyArgs().ReportError(default!);
    }

    [Fact]
    public async Task HandleImportLibraryEntries_PartialVanish_ReloadsAndAnnouncesWithoutErrorBanner()
    {
        var updateA = BuildFilterEntry("UpdateA");
        var updateB = BuildFilterEntry("UpdateB");
        var announcer = Substitute.For<IAnnouncementService>();
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            announcementService: announcer,
            errorBannerService: errorBanner);
        store.AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        store.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns([updateA.Id]);

        await effects.HandleImportLibraryEntries(new ImportLibraryEntriesAction([], [updateA, updateB], new ImportSummary(0, 2, 0, 0, 1)), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(action => action != null && ReferenceEquals(action.Entry, updateA)));
        dispatcher.DidNotReceive().Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(action => action != null && ReferenceEquals(action.Entry, updateB)));
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());

        // Only updateA persisted (updateB vanished), so the announcement reflects the actual replaced count (1), not
        // the preflight total (2).
        announcer.Received(1).Announce("Imported 0 new, replaced 1, updated 0 tags, skipped 0, imported 1 ambiguous as new");
        errorBanner.DidNotReceiveWithAnyArgs().ReportError(default!);
    }

    [Fact]
    public async Task HandleImportLibraryEntries_SuccessWithAddsAndUpdates_DispatchesReturnedRowsAndAnnounces()
    {
        var add = BuildFilterEntry("Added");
        var updateA = BuildFilterEntry("UpdateA");
        var updateB = BuildFilterEntry("UpdateB");
        var announcer = Substitute.For<IAnnouncementService>();
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            announcementService: announcer,
            errorBannerService: errorBanner);
        store.AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        store.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns([updateA.Id, updateB.Id]);

        await effects.HandleImportLibraryEntries(new ImportLibraryEntriesAction([add], [updateA, updateB], new ImportSummary(1, 1, 1, 2, 0)), dispatcher);

        await store.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<LibraryEntry>>(entries => entries != null && entries.SequenceEqual(new[] { add })),
            Arg.Any<CancellationToken>());
        await store.Received(1).UpdateRangeAsync(
            Arg.Is<IReadOnlyList<LibraryEntry>>(entries => entries != null && entries.SequenceEqual(new[] { updateA, updateB })),
            Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(action => action != null && ReferenceEquals(action.Entry, add)));
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(action => action != null && ReferenceEquals(action.Entry, updateA)));
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(action => action != null && ReferenceEquals(action.Entry, updateB)));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibraryAction>());
        announcer.Received(1).Announce("Imported 1 new, replaced 1, updated 1 tag, skipped 2");
        errorBanner.DidNotReceiveWithAnyArgs().ReportError(default!);
    }

    [Fact]
    public async Task HandleImportLibraryEntries_TagUpdateVanishes_AnnouncesActualTagCount()
    {
        // ToUpdate is [replacement, tag-update] with Summary.Replaced = 1, so the boundary index attributes the second
        // entry to the tag-update count. The tag-update row vanishes before commit, so the announcement must drop the
        // tag-update ("updated 0 tags") while keeping the replacement.
        var replaced = BuildFilterEntry("Replaced");
        var tagUpdate = BuildFilterEntry("TagUpdate");
        var announcer = Substitute.For<IAnnouncementService>();
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            announcementService: announcer,
            errorBannerService: errorBanner);
        store.AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        store.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>()).Returns([replaced.Id]);

        await effects.HandleImportLibraryEntries(new ImportLibraryEntriesAction([], [replaced, tagUpdate], new ImportSummary(0, 1, 1, 0, 0)), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(action => action != null && ReferenceEquals(action.Entry, replaced)));
        dispatcher.DidNotReceive().Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(action => action != null && ReferenceEquals(action.Entry, tagUpdate)));
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        announcer.Received(1).Announce("Imported 0 new, replaced 1, updated 0 tags, skipped 0");
        errorBanner.DidNotReceiveWithAnyArgs().ReportError(default!);
    }

    [Fact]
    public async Task HandleImportLibraryEntries_WhenAddRangeThrows_ReportsOneErrorBannerAndReloadsWithoutSuccessOrAnnounce()
    {
        var entry = BuildFilterEntry("First");
        var announcer = Substitute.For<IAnnouncementService>();
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, logger) = CreateEffects(
            announcementService: announcer,
            errorBannerService: errorBanner);
        store.When(s => s.AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleImportLibraryEntries(new ImportLibraryEntriesAction([entry], [], new ImportSummary(1, 0, 0, 0, 0)), dispatcher);

        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsFilterLibraryImportFailed(message)));
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddLibraryEntrySuccessAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        announcer.DidNotReceiveWithAnyArgs().Announce(default!);
        logger.ReceivedWithAnyArgs(1).Warning(default);
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
        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e != null && e.Name == "DNS"), Arg.Any<CancellationToken>());
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
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.IsEmpty));
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleLoadLibrary_Concurrent_TwoSimultaneousDispatches_MigratorBuildCalledExactlyOnce()
    {
        var migratedEntry = BuildFilterEntry("migrated");
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        var migrationComplete = 0;
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
            await migratorReachedSignal.Task.WaitAsync(s_testTimeout, ct);
            load2 = Task.Run(() => effects.HandleLoadLibrary(dispatcher), ct);
            await PollUntilAsync(
                () => dispatcher.ReceivedCalls().Count(c => c.GetArguments().FirstOrDefault() is LoadLibraryStartedAction) >= 2,
                s_testTimeout,
                ct);

            dispatcher.Received(2).Dispatch(Arg.Any<LoadLibraryStartedAction>());
            dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibrarySuccessAction>());
        }
        finally
        {
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
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.Count == 1 && a.Entries[0].Id == entry.Id));
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
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.Count == 1 && a.Entries[0].Id == migratedEntry.Id));
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

        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.IsEmpty));
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
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.IsEmpty));
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

        await store.Received(1).AddRangeAsync(Arg.Is<IEnumerable<LibraryEntry>>(e => e != null && e.Count() == 1), Arg.Any<CancellationToken>());
        await store.Received(2).LoadAllAsync(Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.Count == 1 && a.Entries[0].Id == migratedEntry.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibraryFailureAction>());
        migrator.Received(1).MarkMigrationCompleted(sections);
    }

    [Fact]
    public async Task HandleLoadLibrary_EmptyStore_NoMigratableData_DispatchesSuccessWithEmptyEntries()
    {
        var (effects, store, dispatcher, _, migrator, _) = CreateEffectsWithMigrator();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await effects.HandleLoadLibrary(dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.IsEmpty));
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
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.IsEmpty));
    }

    [Fact]
    public async Task HandleLoadLibrary_NonEmptyStore_FilterSetsSameNameDifferentFilters_BothSurvive()
    {
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
            Filters = ImmutableList.Create(migratedFilter),
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

        await store.Received(1).AddRangeAsync(Arg.Is<IEnumerable<LibraryEntry>>(e => e != null && e.Count() == 1 && e.First() is LibraryEntryFilterSet), Arg.Any<CancellationToken>());
        migrator.Received(1).MarkMigrationCompleted(sections);
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.Count == 2));
    }

    [Fact]
    public async Task HandleLoadLibrary_NonEmptyStore_MigrationEntriesOverlap_DedupsAgainstExisting_NoDuplicateInsertion()
    {
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

        await store.DidNotReceive().AddRangeAsync(Arg.Any<IEnumerable<LibraryEntry>>(), Arg.Any<CancellationToken>());
        migrator.Received(1).MarkMigrationCompleted(sections);
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.Count == 2));
    }

    [Fact]
    public async Task HandleLoadLibrary_NonEmptyStore_MigrationEntriesPartiallyOverlap_OnlyNonOverlappingPersisted()
    {
        var existing = BuildFilterEntryWithText("Existing", "Level == 4");
        var dupeOverlap = BuildFilterEntryWithText("Existing", "Level == 4");
        var newEntry = BuildFilterEntryWithText("New", "Level == 5");
        var sections = LegacyMigrationSections.Favorites | LegacyMigrationSections.Groups | LegacyMigrationSections.Recents;
        var migrator = Substitute.For<ILegacyFilterMigrator>();
        migrator.ShouldRunMigration().Returns(true);
        migrator.BuildEntriesFromLegacy()
            .Returns(new LegacyMigrationResult(ImmutableList.Create<LibraryEntry>(dupeOverlap, newEntry), sections));
        var store = Substitute.For<IFilterLibraryStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([existing], [existing, newEntry]);
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(migrator, store: store);

        await effects.HandleLoadLibrary(dispatcher);

        await store.Received(1).AddRangeAsync(Arg.Is<IEnumerable<LibraryEntry>>(e => e != null && e.Count() == 1 && e.First().Id == newEntry.Id), Arg.Any<CancellationToken>());
        migrator.Received(1).MarkMigrationCompleted(sections);
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.Count == 2));
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
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.Count == 1 && a.Entries[0].Id == existingEntry.Id));
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
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.Count == 1));
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
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.Count == 1));
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
        await store.Received(1).AddRangeAsync(Arg.Is<IEnumerable<LibraryEntry>>(e => e != null && e.Count() == 1), Arg.Any<CancellationToken>());
        migrator.Received(1).MarkMigrationCompleted(sections);
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a != null && a.Entries.Count == 2));
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
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a => a != null &&
            a.Entry.Id == entry.Id && a.Entry.LastUsedUtc != null));
    }

    [Fact]
    public async Task HandleRecordEntryApplied_PruneDeleteReturnsFalse_DoesNotDispatchDeleteSuccess()
    {
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

        var oldestId = ((LibraryEntrySavedFilter)entries[0]).Id;
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [.. entries] });
        store.TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        store.TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>()).Returns(true);

        await effects.HandleRecordEntryApplied(new RecordEntryAppliedAction(bumpTarget.Id), dispatcher);

        await store.Received(1).TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Is(oldestId), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<DeleteLibraryEntrySuccessAction>(a => a != null && a.EntryId == oldestId));
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
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => a != null &&
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

        var alreadyInState = new LibraryEntrySavedFilter
        {
            Name = "already-in-state",
            CreatedUtc = DateTimeOffset.UtcNow,
            Origin = LibraryEntryOrigin.AutoTracked,
            LastUsedUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 49, TimeSpan.Zero),
            Filter = SavedFilter.TryCreate("Level == 1999")!,
        };
        entries.Add(alreadyInState);

        var newFilter = SavedFilter.TryCreate("Level == 2777");
        Assert.NotNull(newFilter);

        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [.. entries] });
        store.AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>())
            .Returns((alreadyInState, false));
        store.TryBumpLastUsedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        store.TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(newFilter), dispatcher);

        await store.DidNotReceive().TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<DeleteLibraryEntrySuccessAction>());
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => a != null && a.Entry.Id == alreadyInState.Id));
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
            .Returns(call => (call.ArgAt<LibraryEntrySavedFilter>(0), true));

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
                var candidate = call.ArgAt<LibraryEntrySavedFilter>(0);
                return (candidate, true);
            });
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(filter), dispatcher);

        await store.Received(1).AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => a != null &&
            a.Entry.GetType() == typeof(LibraryEntrySavedFilter)
                && ((LibraryEntrySavedFilter)a.Entry).Origin == LibraryEntryOrigin.AutoTracked
                && ((LibraryEntrySavedFilter)a.Entry).LastUsedUtc != null
                && ((LibraryEntrySavedFilter)a.Entry).Filter.ComparisonText == filter.ComparisonText));
    }

    [Fact]
    public async Task HandleRecordFilterApplied_PrunesOldestAutoTrackedEntries_WhenInsertExceedsCap()
    {
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
                Filter = SavedFilter.TryCreate($"Level == {i + 100}")!,
            });
        }

        var oldestId = ((LibraryEntrySavedFilter)entries[0]).Id;
        var newFilter = SavedFilter.TryCreate("Level == 999");
        Assert.NotNull(newFilter);

        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [.. entries] });
        store.AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>())
            .Returns(call => (call.ArgAt<LibraryEntrySavedFilter>(0), true));
        store.TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Any<LibraryEntryId>(), Arg.Any<CancellationToken>()).Returns(true);

        await effects.HandleRecordFilterApplied(new RecordFilterAppliedAction(newFilter), dispatcher);

        await store.Received(1).AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>());
        await store.Received(1).TryDeleteAutoTrackedIfNotFavoriteAsync(Arg.Is(oldestId), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<DeleteLibraryEntrySuccessAction>(a => a != null && a.EntryId == oldestId));
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
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a => a != null &&
            a.Entry.Id == existing.Id && a.Entry.LastUsedUtc != existing.LastUsedUtc));
        await store.DidNotReceive().AddOrReturnExistingFilterAsync(Arg.Any<LibraryEntrySavedFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRecordFilterApplied_UserSavedExisting_BumpsAndSkipsAutoTrack()
    {
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
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a => a != null && a.Entry.Id == existing.Id));
    }

    [Fact]
    public async Task HandleRenameTag_CollidingTags_DedupesPreservingNewCanonical()
    {
        var entry = BuildFilterEntry("E") with { Tags = ["bug", "defect"] };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleRenameTag(new RenameTagAction("bug", "defect"), dispatcher);

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list => list != null &&
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

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list => list != null &&
            list.Count == 1 && list[0] is LibraryEntryFilterSet && list[0].Tags.SequenceEqual(new[] { "defect" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRenameTag_MatchesMixedCaseStoredTag_HealsToCanonical()
    {
        var entry = BuildFilterEntry("E") with { Tags = ["BUG"] };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleRenameTag(new RenameTagAction("bug", "defect"), dispatcher);

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list => list != null &&
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

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list => list != null &&
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

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list => list != null &&
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

        await store.Received(1).UpdateRangeAsync(Arg.Is<IReadOnlyList<LibraryEntry>>(list => list != null &&
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

        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(s => s != null && s.Entry.Id == a.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(s => s != null && s.Entry.Id == b.Id));
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        announcer.Received(1).Announce("Renamed tag 'bug' to 'defect' in 1 entry");
    }

    [Fact]
    public async Task HandleRenameTag_StoreThrows_NoSuccessDispatched_ReloadsLibrary_ReportsErrorBanner()
    {
        var a = BuildFilterEntry("A") with { Tags = ["bug"] };
        var b = BuildFilterEntry("B") with { Tags = ["bug"] };
        var announcer = Substitute.For<IAnnouncementService>();
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, logger) = CreateEffects(
            state: new FilterLibraryState { Entries = [a, b] },
            announcementService: announcer,
            errorBannerService: errorBanner);
        store.When(s => s.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleRenameTag(new RenameTagAction("bug", "defect"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        dispatcher.Received(1).Dispatch(Arg.Any<TagBulkUpdateFailedAction>());
        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsLibraryTagsBulkUpdateFailed(message)));
        announcer.DidNotReceiveWithAnyArgs().Announce(default!);
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

        dispatcher.Received(1).Dispatch(Arg.Is<ReplaceFiltersAction>(a => a != null && a.Filters.Count == 2));
        dispatcher.Received(1).Dispatch(Arg.Is<RecordEntryAppliedAction>(a => a != null && a.EntryId == filterSet.Id));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<MergeFiltersAction>());
    }

    [Fact]
    public async Task HandleReplaceWithLibraryEntry_SavedFilterEntry_DispatchesReplaceFiltersWithSingleFilterAndRecordEntryApplied()
    {
        var entry = BuildFilterEntry("First");
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        await effects.HandleReplaceWithLibraryEntry(new ReplaceWithLibraryEntryAction(entry.Id), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<ReplaceFiltersAction>(a => a != null && a.Filters.Count == 1));
        dispatcher.Received(1).Dispatch(Arg.Is<RecordEntryAppliedAction>(a => a != null && a.EntryId == entry.Id));
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

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e != null &&
            e.Id == entry.Id && e.Origin == LibraryEntryOrigin.UserSaved), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleSaveEntry_ReprojectsOriginOntoLatestSnapshot_NotPreAwaitSnapshot()
    {
        var staleEntry = BuildFilterEntry("OldName") with { Origin = LibraryEntryOrigin.AutoTracked };
        var renamedEntry = staleEntry with { Name = "NewName" };
        var staleState = new FilterLibraryState { Entries = [staleEntry] };
        var renamedState = new FilterLibraryState { Entries = [renamedEntry] };
        var (effects, _, dispatcher, stateMock, _) = CreateEffects(state: staleState);

        stateMock.Value.Returns(staleState, staleState, renamedState);

        await effects.HandleSaveEntry(new SaveEntryAction(staleEntry.Id), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a => a != null &&
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
            Arg.Is<LibraryEntry>(e => e != null && e.Id == entry.Id && e.Origin == LibraryEntryOrigin.UserSaved),
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

        await store.Received(1).AddAsync(Arg.Is<LibraryEntry>(e => e != null && e.GetType() == typeof(LibraryEntryFilterSet)
            && ((LibraryEntryFilterSet)e).Name == "My Preset"
            && ((LibraryEntryFilterSet)e).Origin == LibraryEntryOrigin.UserSaved
            && ((LibraryEntryFilterSet)e).Filters.Count == 2
            && ((LibraryEntryFilterSet)e).Filters.All(f => !originalIds.Contains(f.Id))
            && ((LibraryEntryFilterSet)e).Filters.All(f => !f.IsEnabled)), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => a != null && a.Entry.GetType() == typeof(LibraryEntryFilterSet)));
    }

    [Fact]
    public async Task HandleSaveFilterSet_EmptyFilters_IsNoOp()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleSaveFilterSet(new SaveFilterSetAction("Empty", []), dispatcher);

        await store.DidNotReceive().AddAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleSaveFilterSet_LensOrigin_Succeeds_DispatchesSucceededWithOrigin()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var (effects, _, dispatcher, _, _) = CreateEffects();

        await effects.HandleSaveFilterSet(
            new SaveFilterSetAction("My Group", [filter], SaveFilterSetOrigin.Lens), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => a != null));
        dispatcher.Received(1).Dispatch(Arg.Is<SaveFilterSetSucceededAction>(a =>
            a != null && a.Name == "My Group" && a.Origin == SaveFilterSetOrigin.Lens));
    }

    [Fact]
    public async Task HandleSaveFilterSet_StoreThrows_ReportsErrorBanner_AndDoesNotDispatchSucceeded()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var store = Substitute.For<IFilterLibraryStore>();
        store.AddAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk full"));
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, _, dispatcher, _, _, _) = CreateEffectsWithMigrator(store: store, errorBannerService: errorBanner);

        await effects.HandleSaveFilterSet(
            new SaveFilterSetAction("My Group", [filter], SaveFilterSetOrigin.Lens), dispatcher);

        errorBanner.Received(1).ReportError(
            Arg.Is<BannerMessage>(message => IsFilterSetSaveFailed(message, "My Group")));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<SaveFilterSetSucceededAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddLibraryEntrySuccessAction>());
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
    public async Task HandleSaveFilterSet_WithLensesToClearOnSuccess_ForwardsThemToSucceeded()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var lensA = FilterLensId.Create();
        var lensB = FilterLensId.Create();
        var (effects, _, dispatcher, _, _) = CreateEffects();

        await effects.HandleSaveFilterSet(
            new SaveFilterSetAction("My Group", [filter], SaveFilterSetOrigin.Lens, [lensA, lensB]), dispatcher);

        // The saved-lens ids ride opaquely through the FilterLibrary save so the FilterLenses success handler can
        // remove exactly the saved lenses once the write is confirmed persisted.
        dispatcher.Received(1).Dispatch(Arg.Is<SaveFilterSetSucceededAction>(a =>
            a != null && a.LensesToClearOnSuccess != null && a.LensesToClearOnSuccess.Count == 2 &&
            a.LensesToClearOnSuccess.Contains(lensA) && a.LensesToClearOnSuccess.Contains(lensB)));
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

        dispatcher.Received(1).Dispatch(Arg.Is<SaveFilterSetAction>(a => a != null &&
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
    public async Task HandleSetEntryName_WhenStoreThrows_ReportsErrorBanner()
    {
        var entry = BuildFilterEntry("First");
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [entry] },
            errorBannerService: errorBanner);
        store.UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk full"));

        await effects.HandleSetEntryName(new SetEntryNameAction(entry.Id, "Renamed"), dispatcher);

        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsLibraryEntryRenameFailed(message)));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleSetEntryTags_WhenStoreThrows_ReportsErrorBanner()
    {
        var entry = BuildFilterEntry("Tagged");
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [entry] },
            errorBannerService: errorBanner);
        store.UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk full"));

        await effects.HandleSetEntryTags(new SetEntryTagsAction(entry.Id, ["updated"]), dispatcher);

        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsLibraryEntryTagsSaveFailed(message)));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleSetFilterSetFilters_WhenStoreThrows_ReportsErrorBanner()
    {
        var filterSet = BuildFilterSetEntry("Errors");
        var newFilter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(newFilter);
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [filterSet] },
            errorBannerService: errorBanner);
        store.UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk full"));

        await effects.HandleSetFilterSetFilters(new SetFilterSetFiltersAction(filterSet.Id, [newFilter]), dispatcher);

        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsFilterSetUpdateFailed(message)));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
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

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e != null && e.Id == filterSet.Id && !e.IsFavorite && e.LastUsedUtc == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleSetIsFavorite_False_OnFilter_BumpsLastUsedToNow()
    {
        var filter = BuildFilterEntry("First") with { IsFavorite = true };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [filter] });

        await effects.HandleSetIsFavorite(new SetIsFavoriteAction(filter.Id, IsFavorite: false), dispatcher);

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e != null &&
            e.Id == filter.Id && !e.IsFavorite && e.LastUsedUtc != null), Arg.Any<CancellationToken>());
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

        await store.Received(1).UpdateAsync(Arg.Is<LibraryEntry>(e => e != null &&
            e.Id == entry.Id && e.IsFavorite && e.LastUsedUtc == null && e.Origin == LibraryEntryOrigin.UserSaved), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a => a != null &&
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
    public async Task HandleSetIsFavorite_WhenStoreThrows_ReportsErrorBanner()
    {
        var entry = BuildFilterEntry("Fav");
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, _) = CreateEffects(
            state: new FilterLibraryState { Entries = [entry] },
            errorBannerService: errorBanner);
        store.UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disk full"));

        await effects.HandleSetIsFavorite(new SetIsFavoriteAction(entry.Id, true), dispatcher);

        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsLibraryEntryFavoriteFailed(message)));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
    }

    [Fact]
    public async Task HandleUpdateLibraryEntry_PersistsAndDispatchesSuccess()
    {
        var entry = BuildFilterEntry("First");
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleUpdateLibraryEntry(new UpdateLibraryEntryAction(entry), dispatcher);

        await store.Received(1).UpdateAsync(Arg.Is(entry), Arg.Any<CancellationToken>());
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a => a != null && ReferenceEquals(a.Entry, entry)));
    }

    [Fact]
    public async Task HandleUpdateLibraryEntry_WhenStoreThrows_ReportsErrorBannerAndDoesNotDispatchSuccess()
    {
        var entry = BuildFilterEntry("First");
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, _, logger) = CreateEffects(errorBannerService: errorBanner);
        store.When(s => s.UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleUpdateLibraryEntry(new UpdateLibraryEntryAction(entry), dispatcher);

        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsLibraryEntryUpdateFailed(message, "First")));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task PersistAndDispatchAsync_ReissueAgainstLatestSnapshotFails_DispatchesLoadLibraryToResync()
    {
        var staleEntry = BuildFilterEntry("OldName") with { Origin = LibraryEntryOrigin.AutoTracked };
        var renamedEntry = staleEntry with { Name = "NewName" };
        var staleState = new FilterLibraryState { Entries = [staleEntry] };
        var renamedState = new FilterLibraryState { Entries = [renamedEntry] };
        var errorBanner = Substitute.For<IErrorBannerService>();
        var (effects, store, dispatcher, stateMock, logger) = CreateEffects(state: staleState, errorBannerService: errorBanner);

        stateMock.Value.Returns(staleState, staleState, renamedState);

        int updateCalls = 0;
        store.UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                updateCalls++;
                return updateCalls == 1
                    ? Task.CompletedTask
                    : Task.FromException(new InvalidOperationException("reissue-fail"));
            });

        await effects.HandleSaveEntry(new SaveEntryAction(staleEntry.Id), dispatcher);

        Assert.Equal(2, updateCalls);
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        errorBanner.Received(1).ReportError(Arg.Is<BannerMessage>(message => IsLibraryEntryPromoteFailed(message)));
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task WriteGate_SerializesConcurrentPersistAndDispatchAsyncCalls()
    {
        var entry1 = BuildFilterEntry("E1") with { Origin = LibraryEntryOrigin.AutoTracked };
        var entry2 = BuildFilterEntry("E2") with { Origin = LibraryEntryOrigin.AutoTracked };
        var (effects, store, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry1, entry2] });

        var firstBlocker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int totalCalls = 0;
        Lock lockObj = new();

        store.UpdateAsync(Arg.Any<LibraryEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                int seq;

                lock (lockObj) { seq = ++totalCalls; }

                return seq == 1 ? firstBlocker.Task : Task.CompletedTask;
            });

        var task1 = effects.HandleSaveEntry(new SaveEntryAction(entry1.Id), dispatcher);
        var task2 = effects.HandleSaveEntry(new SaveEntryAction(entry2.Id), dispatcher);

        await PollUntilAsync(() => Volatile.Read(ref totalCalls) == 1, s_shortTimeout, TestContext.Current.CancellationToken);
        await Task.Delay(SettleDelayMilliseconds, TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref totalCalls));

        firstBlocker.SetResult(true);
        await Task.WhenAll(task1, task2);

        Assert.Equal(2, Volatile.Read(ref totalCalls));
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
        IAnnouncementService? announcementService = null,
        IErrorBannerService? errorBannerService = null)
    {
        var (effects, store, dispatcher, stateMock, _, logger) = CreateEffectsWithMigrator(
            migrator: null,
            state: state,
            paneState: paneState,
            announcementService: announcementService,
            errorBannerService: errorBannerService);

        return (effects, store, dispatcher, stateMock, logger);
    }

    private static (Effects effects, IFilterLibraryStore store, IDispatcher dispatcher, IState<FilterLibraryState> stateMock, ILegacyFilterMigrator migrator, ITraceLogger logger) CreateEffectsWithMigrator(
        ILegacyFilterMigrator? migrator = null,
        FilterLibraryState? state = null,
        FilterPaneState? paneState = null,
        IFilterLibraryStore? store = null,
        IBackslashNameMigrator? backslashMigrator = null,
        IAnnouncementService? announcementService = null,
        IErrorBannerService? errorBannerService = null)
    {
        var storeWasSupplied = store is not null;
        store ??= Substitute.For<IFilterLibraryStore>();
        if (!storeWasSupplied)
        {
            store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns([]);
            store.UpdateRangeAsync(Arg.Any<IReadOnlyList<LibraryEntry>>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(ci =>
            [
                .. ci.ArgAt<IReadOnlyList<LibraryEntry>>(0).Select(e => e.Id)
            ]);
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
        errorBannerService ??= Substitute.For<IErrorBannerService>();

        var logger = Substitute.For<ITraceLogger>();
        var dispatcher = Substitute.For<IDispatcher>();
        var effects = new Effects(store, stateMock, paneStateMock, migrator, backslashMigrator, announcementService, errorBannerService, logger, new TagBulkUpdateFailedNotifier(Substitute.For<ITraceLogger>()));

        return (effects, store, dispatcher, stateMock, migrator, logger);
    }

    private static bool IsFilterAddToSetFailed(BannerMessage? message) =>
        message is FilterAddToSetFailed;

    private static bool IsFilterLibraryImportFailed(BannerMessage? message) =>
        message is FilterLibraryImportFailed;

    private static bool IsFilterSetCreateFailed(BannerMessage? message, string name) =>
        message is FilterSetCreateFailed createFailed && createFailed.Name == name;

    private static bool IsFilterSetSaveFailed(BannerMessage? message, string name) =>
        message is FilterSetSaveFailed saveFailed && saveFailed.Name == name;

    private static bool IsFilterSetUpdateFailed(BannerMessage? message) =>
        message is FilterSetUpdateFailed;

    private static bool IsLibraryEntryDeleteFailed(BannerMessage? message) =>
        message is LibraryEntryDeleteFailed;

    private static bool IsLibraryEntryFavoriteFailed(BannerMessage? message) =>
        message is LibraryEntryFavoriteFailed;

    private static bool IsLibraryEntryPromoteFailed(BannerMessage? message) =>
        message is LibraryEntryPromoteFailed;

    private static bool IsLibraryEntryRenameFailed(BannerMessage? message) =>
        message is LibraryEntryRenameFailed;

    private static bool IsLibraryEntrySaveFailed(BannerMessage? message, string name) =>
        message is LibraryEntrySaveFailed saveFailed && saveFailed.Name == name;

    private static bool IsLibraryEntryTagsSaveFailed(BannerMessage? message) =>
        message is LibraryEntryTagsSaveFailed;

    private static bool IsLibraryEntryUpdateFailed(BannerMessage? message, string name) =>
        message is LibraryEntryUpdateFailed updateFailed && updateFailed.Name == name;

    private static bool IsLibraryTagsBulkUpdateFailed(BannerMessage? message) =>
        message is LibraryTagsBulkUpdateFailed;

    private static async Task PollUntilAsync(Func<bool> predicate, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) { return; }
            await Task.Delay(PollDelayMilliseconds, cancellationToken);
        }
        if (!predicate()) { throw new TimeoutException($"Predicate did not become true within {timeout}."); }
    }

    private sealed record UnknownLibraryEntry : LibraryEntry;
}
