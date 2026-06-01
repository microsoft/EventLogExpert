// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class FilterLibraryReducersTests
{
    [Fact]
    public void ReduceAddLibraryEntrySuccess_AppendsEntry()
    {
        // Arrange
        var existing = BuildFilterEntry("id-1", "First");
        var state = new FilterLibraryState { Entries = [existing] };
        var added = BuildFilterEntry("id-2", "Second");

        // Act
        var result = Reducers.ReduceAddLibraryEntrySuccess(state, new AddLibraryEntrySuccessAction(added));

        // Assert
        Assert.Equal(2, result.Entries.Count);
        Assert.Same(existing, result.Entries[0]);
        Assert.Same(added, result.Entries[1]);
    }

    [Fact]
    public void ReduceDeleteLibraryEntrySuccess_RemovesById()
    {
        // Arrange
        var first = BuildFilterEntry("id-1", "First");
        var second = BuildFilterEntry("id-2", "Second");
        var state = new FilterLibraryState { Entries = [first, second] };

        // Act
        var result = Reducers.ReduceDeleteLibraryEntrySuccess(state, new DeleteLibraryEntrySuccessAction("id-1"));

        // Assert
        Assert.Single(result.Entries);
        Assert.Same(second, result.Entries[0]);
    }

    [Fact]
    public void ReduceDeleteLibraryEntrySuccess_UnknownId_IsNoOp()
    {
        // Arrange
        var existing = BuildFilterEntry("id-1", "First");
        var state = new FilterLibraryState { Entries = [existing] };

        // Act
        var result = Reducers.ReduceDeleteLibraryEntrySuccess(state, new DeleteLibraryEntrySuccessAction("id-unknown"));

        // Assert
        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceLoadLibraryFailure_SetsLoadErrorAndIsLoaded_ClearsEntries()
    {
        // Arrange
        var initialState = new FilterLibraryState
        {
            Entries = [BuildFilterEntry("stale", "Stale")],
            IsLoaded = true,
        };

        // Act
        var result = Reducers.ReduceLoadLibraryFailure(initialState);

        // Assert
        Assert.Empty(result.Entries);
        Assert.True(result.IsLoaded);
        Assert.True(result.LoadError);
    }

    [Fact]
    public void ReduceLoadLibrarySuccess_SetsEntriesAndIsLoaded_ClearsLoadError()
    {
        // Arrange
        var initialState = new FilterLibraryState { LoadError = true };
        var entry = BuildFilterEntry("id-1", "First");
        var action = new LoadLibrarySuccessAction([entry]);

        // Act
        var result = Reducers.ReduceLoadLibrarySuccess(initialState, action);

        // Assert
        Assert.Single(result.Entries);
        Assert.True(result.IsLoaded);
        Assert.False(result.LoadError);
    }

    [Fact]
    public void ReduceUpdateLibraryEntrySuccess_ReplacesByIdPreservingPosition()
    {
        // Arrange
        var first = BuildFilterEntry("id-1", "First");
        var second = BuildFilterEntry("id-2", "Second");
        var state = new FilterLibraryState { Entries = [first, second] };
        var replaced = BuildFilterEntry("id-1", "First (updated)");

        // Act
        var result = Reducers.ReduceUpdateLibraryEntrySuccess(state, new UpdateLibraryEntrySuccessAction(replaced));

        // Assert
        Assert.Equal(2, result.Entries.Count);
        Assert.Same(replaced, result.Entries[0]);
        Assert.Same(second, result.Entries[1]);
    }

    [Fact]
    public void ReduceUpdateLibraryEntrySuccess_UnknownId_IsNoOp()
    {
        // Arrange
        var existing = BuildFilterEntry("id-1", "First");
        var state = new FilterLibraryState { Entries = [existing] };
        var unknown = BuildFilterEntry("id-unknown", "Unknown");

        // Act
        var result = Reducers.ReduceUpdateLibraryEntrySuccess(state, new UpdateLibraryEntrySuccessAction(unknown));

        // Assert
        Assert.Same(state, result);
    }

    private static LibraryEntrySavedFilter BuildFilterEntry(string id, string name)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Id = id,
            Name = name,
            CreatedUtc = new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero),
            Filter = filter,
        };
    }
}
