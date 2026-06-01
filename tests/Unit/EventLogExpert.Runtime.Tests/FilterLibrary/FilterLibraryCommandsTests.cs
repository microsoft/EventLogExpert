// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using Fluxor;
using NSubstitute;

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
    public void ApplyEntry_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);

        sut.ApplyEntry("id-1");

        dispatcher.Received(1).Dispatch(Arg.Is<ApplyLibraryEntryAction>(a => a.EntryId == "id-1"));
    }

    [Fact]
    public void DeleteEntry_Dispatches()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new FilterLibraryCommands(dispatcher);

        sut.DeleteEntry("id-1");

        dispatcher.Received(1).Dispatch(Arg.Is<DeleteLibraryEntryAction>(a => a.EntryId == "id-1"));
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
            Id = "id-1",
            Name = "Test",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = filter,
        };
    }
}
