// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class FilterLibraryCommandsTests
{
    [Fact]
    public void AddEntry_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);
        var entry = BuildEntry();

        sut.AddEntry(entry);

        dispatcher.Received(1).Dispatch(Arg.Is<AddLibraryEntryAction>(a => ReferenceEquals(a.Entry, entry)));
    }

    [Fact]
    public void AddFilterToExistingFilterSet_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);
        var filterSetId = LibraryEntryId.Create();
        var source = LibraryEntryId.Create();
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        sut.AddFilterToExistingFilterSet(filterSetId, filter, source);

        dispatcher.Received(1).Dispatch(Arg.Is<AddFilterToExistingFilterSetAction>(a =>
            a.FilterSetId == filterSetId && a.SourceEntryId == source && ReferenceEquals(a.Filter, filter)));
    }

    [Fact]
    public void AddFilterToNewFilterSet_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        sut.AddFilterToNewFilterSet("New", filter, sourceEntryId: null);

        dispatcher.Received(1).Dispatch(Arg.Is<AddFilterToNewFilterSetAction>(a =>
            a.NewFilterSetName == "New" && a.SourceEntryId == null && ReferenceEquals(a.Filter, filter)));
    }

    [Fact]
    public void ApplyEntry_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);
        var id = LibraryEntryId.Create();

        sut.ApplyEntry(id);

        dispatcher.Received(1).Dispatch(Arg.Is<ApplyLibraryEntryAction>(a => a.EntryId == id));
    }

    [Fact]
    public void DeleteEntry_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);
        var id = LibraryEntryId.Create();

        sut.DeleteEntry(id);

        dispatcher.Received(1).Dispatch(Arg.Is<DeleteLibraryEntryAction>(a => a.EntryId == id));
    }

    [Fact]
    public void LoadLibrary_Dispatches()
    {
        // Arrange
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);

        // Act
        sut.LoadLibrary();

        // Assert
        dispatcher.Received(1).Dispatch(Arg.Any<LoadLibraryAction>());
    }

    [Fact]
    public void RecordFilterApplied_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        sut.RecordFilterApplied(filter);

        dispatcher.Received(1).Dispatch(Arg.Is<RecordFilterAppliedAction>(a => ReferenceEquals(a.Filter, filter)));
    }

    [Fact]
    public void ReplaceWithEntry_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);
        var id = LibraryEntryId.Create();

        sut.ReplaceWithEntry(id);

        dispatcher.Received(1).Dispatch(Arg.Is<ReplaceWithLibraryEntryAction>(a => a.EntryId == id));
    }

    [Fact]
    public void SaveEntry_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);
        var id = LibraryEntryId.Create();

        sut.SaveEntry(id);

        dispatcher.Received(1).Dispatch(Arg.Is<SaveEntryAction>(a => a.EntryId == id));
    }

    [Fact]
    public void SaveFilterSet_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);
        var filters = ImmutableList.Create(filter);

        sut.SaveFilterSet("My Preset", filters);

        dispatcher.Received(1).Dispatch(Arg.Is<SaveFilterSetAction>(a => a.Name == "My Preset" && a.Filters == filters));
    }

    [Fact]
    public void SavePaneAsFilterSet_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);

        sut.SavePaneAsFilterSet("My Preset");

        dispatcher.Received(1).Dispatch(Arg.Is<SavePaneAsFilterSetAction>(a => a.Name == "My Preset"));
    }

    [Fact]
    public void SetIsFavorite_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);
        var id = LibraryEntryId.Create();

        sut.SetIsFavorite(id, isFavorite: true);

        dispatcher.Received(1).Dispatch(Arg.Is<SetIsFavoriteAction>(a => a.EntryId == id && a.IsFavorite));
    }

    [Fact]
    public void UpdateEntry_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);
        var entry = BuildEntry();

        sut.UpdateEntry(entry);

        dispatcher.Received(1).Dispatch(Arg.Is<UpdateLibraryEntryAction>(a => ReferenceEquals(a.Entry, entry)));
    }

    private static LibraryEntry BuildEntry()
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = "Test",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
        };
    }
}
