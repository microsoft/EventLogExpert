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
    public async Task HandleAddLibraryEntry_PersistsAndDispatchesSuccess()
    {
        // Arrange
        var entry = BuildFilterEntry("id-1", "First");
        var (effects, store, dispatcher, _, _) = CreateEffects();

        // Act
        await effects.HandleAddLibraryEntry(new AddLibraryEntryAction(entry), dispatcher);

        // Assert
        store.Received(1).Add(entry);
        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntrySuccessAction>(a => ReferenceEquals(a.Entry, entry)));
    }

    [Fact]
    public async Task HandleAddLibraryEntry_WhenStoreThrows_DoesNotDispatchSuccess()
    {
        // Arrange
        var entry = BuildFilterEntry("id-1", "First");
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.When(s => s.Add(Arg.Any<LibraryEntry>())).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        await effects.HandleAddLibraryEntry(new AddLibraryEntryAction(entry), dispatcher);

        // Assert
        dispatcher.DidNotReceive().Dispatch(Arg.Any<AddLibraryEntrySuccessAction>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleApplyLibraryEntry_PresetEntry_DispatchesReplaceFiltersWithAllFilters()
    {
        // Arrange
        var f1 = SavedFilter.TryCreate("Level == 2");
        var f2 = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(f1);
        Assert.NotNull(f2);

        var preset = new LibraryEntryPreset
        {
            Id = "preset-1",
            Name = "Preset",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [f1, f2],
        };
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [preset] });

        // Act
        await effects.HandleApplyLibraryEntry(new ApplyLibraryEntryAction("preset-1"), dispatcher);

        // Assert
        dispatcher.Received(1).Dispatch(Arg.Is<ReplaceFiltersAction>(a => a.Filters.Count == 2));
    }

    [Fact]
    public async Task HandleApplyLibraryEntry_SavedFilterEntry_DispatchesReplaceFiltersWithSingleFilter()
    {
        // Arrange
        var entry = BuildFilterEntry("id-1", "First");
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [entry] });

        // Act
        await effects.HandleApplyLibraryEntry(new ApplyLibraryEntryAction("id-1"), dispatcher);

        // Assert
        dispatcher.Received(1).Dispatch(Arg.Is<ReplaceFiltersAction>(a => a.Filters.Count == 1));
    }

    [Fact]
    public async Task HandleApplyLibraryEntry_UnknownConcreteType_ThrowsInvalidOperationException()
    {
        var unknown = new UnknownLibraryEntry
        {
            Id = "id-x",
            Name = "Unknown",
            CreatedUtc = DateTimeOffset.UtcNow,
        };
        var (effects, _, dispatcher, _, _) = CreateEffects(state: new FilterLibraryState { Entries = [unknown] });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            effects.HandleApplyLibraryEntry(new ApplyLibraryEntryAction("id-x"), dispatcher));
    }

    [Fact]
    public async Task HandleApplyLibraryEntry_UnknownId_IsNoOp()
    {
        var (effects, _, dispatcher, _, _) = CreateEffects();

        await effects.HandleApplyLibraryEntry(new ApplyLibraryEntryAction("missing"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<ReplaceFiltersAction>());
    }

    [Fact]
    public async Task HandleDeleteLibraryEntry_PersistsAndDispatchesSuccess()
    {
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleDeleteLibraryEntry(new DeleteLibraryEntryAction("id-1"), dispatcher);

        store.Received(1).Delete("id-1");
        dispatcher.Received(1).Dispatch(Arg.Is<DeleteLibraryEntrySuccessAction>(a => a.EntryId == "id-1"));
    }

    [Fact]
    public async Task HandleDeleteLibraryEntry_WhenStoreThrows_DoesNotDispatchSuccess()
    {
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.When(s => s.Delete(Arg.Any<string>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleDeleteLibraryEntry(new DeleteLibraryEntryAction("id-1"), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<DeleteLibraryEntrySuccessAction>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleLoadLibrary_DispatchesSuccessWithStoreEntries()
    {
        // Arrange
        var entry = BuildFilterEntry("id-1", "First");
        var (effects, store, dispatcher, _, _) = CreateEffects();
        store.LoadAll().Returns([entry]);

        // Act
        await effects.HandleLoadLibrary(dispatcher);

        // Assert
        dispatcher.Received(1).Dispatch(Arg.Is<LoadLibrarySuccessAction>(a => a.Entries.Count == 1 && a.Entries[0].Id == "id-1"));
    }

    [Fact]
    public async Task HandleLoadLibrary_WhenStoreThrows_DispatchesFailureActionAndLogs()
    {
        // Arrange
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.LoadAll().Returns(_ => throw new InvalidOperationException("boom"));

        // Act
        await effects.HandleLoadLibrary(dispatcher);

        // Assert
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryFailureAction>());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadLibrarySuccessAction>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    [Fact]
    public async Task HandleUpdateLibraryEntry_PersistsAndDispatchesSuccess()
    {
        var entry = BuildFilterEntry("id-1", "First");
        var (effects, store, dispatcher, _, _) = CreateEffects();

        await effects.HandleUpdateLibraryEntry(new UpdateLibraryEntryAction(entry), dispatcher);

        store.Received(1).Update(entry);
        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntrySuccessAction>(a => ReferenceEquals(a.Entry, entry)));
    }

    [Fact]
    public async Task HandleUpdateLibraryEntry_WhenStoreThrows_DoesNotDispatchSuccess()
    {
        var entry = BuildFilterEntry("id-1", "First");
        var (effects, store, dispatcher, _, logger) = CreateEffects();
        store.When(s => s.Update(Arg.Any<LibraryEntry>())).Do(_ => throw new InvalidOperationException("boom"));

        await effects.HandleUpdateLibraryEntry(new UpdateLibraryEntryAction(entry), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<UpdateLibraryEntrySuccessAction>());
        logger.ReceivedWithAnyArgs(1).Warning(default);
    }

    private static LibraryEntrySavedFilter BuildFilterEntry(string id, string name)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Id = id,
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
        };
    }

    private static (Effects effects, IFilterLibraryStore store, IDispatcher dispatcher, IState<FilterLibraryState> stateMock, ITraceLogger logger) CreateEffects(
        FilterLibraryState? state = null)
    {
        var store = Substitute.For<IFilterLibraryStore>();
        store.LoadAll().Returns([]);

        var stateMock = Substitute.For<IState<FilterLibraryState>>();
        stateMock.Value.Returns(state ?? new FilterLibraryState());

        var logger = Substitute.For<ITraceLogger>();
        var dispatcher = Substitute.For<IDispatcher>();
        var effects = new Effects(store, stateMock, logger);

        return (effects, store, dispatcher, stateMock, logger);
    }

    // Used for the unknown-concrete-type wildcard test (covers the throw arm in HandleApplyLibraryEntry).
    private sealed record UnknownLibraryEntry : LibraryEntry;
}
