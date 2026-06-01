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
        var existing = BuildFilterEntry("First");
        var state = new FilterLibraryState { Entries = [existing] };
        var added = BuildFilterEntry("Second");

        var result = Reducers.ReduceAddLibraryEntrySuccess(state, new AddLibraryEntrySuccessAction(added));

        Assert.Equal(2, result.Entries.Count);
        Assert.Same(existing, result.Entries[0]);
        Assert.Same(added, result.Entries[1]);
    }

    [Fact]
    public void ReduceDeleteLibraryEntrySuccess_RemovesById()
    {
        var first = BuildFilterEntry("First");
        var second = BuildFilterEntry("Second");
        var state = new FilterLibraryState { Entries = [first, second] };

        var result = Reducers.ReduceDeleteLibraryEntrySuccess(state, new DeleteLibraryEntrySuccessAction(first.Id));

        Assert.Single(result.Entries);
        Assert.Same(second, result.Entries[0]);
    }

    [Fact]
    public void ReduceDeleteLibraryEntrySuccess_UnknownId_IsNoOp()
    {
        var existing = BuildFilterEntry("First");
        var state = new FilterLibraryState { Entries = [existing] };

        var result = Reducers.ReduceDeleteLibraryEntrySuccess(state, new DeleteLibraryEntrySuccessAction(LibraryEntryId.Create()));

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceLoadLibraryFailure_SetsLoadErrorAndIsLoaded_ClearsEntries()
    {
        var initialState = new FilterLibraryState
        {
            Entries = [BuildFilterEntry("Stale")],
            IsLoaded = true,
        };

        var result = Reducers.ReduceLoadLibraryFailure(initialState);

        Assert.Empty(result.Entries);
        Assert.True(result.IsLoaded);
        Assert.True(result.LoadError);
    }

    [Fact]
    public void ReduceLoadLibrarySuccess_SetsEntriesAndIsLoaded_ClearsLoadError()
    {
        var initialState = new FilterLibraryState { LoadError = true };
        var entry = BuildFilterEntry("First");
        var action = new LoadLibrarySuccessAction([entry]);

        var result = Reducers.ReduceLoadLibrarySuccess(initialState, action);

        Assert.Single(result.Entries);
        Assert.True(result.IsLoaded);
        Assert.False(result.LoadError);
    }

    [Fact]
    public void ReduceUpdateLibraryEntrySuccess_ReplacesByIdPreservingPosition()
    {
        var first = BuildFilterEntry("First");
        var second = BuildFilterEntry("Second");
        var state = new FilterLibraryState { Entries = [first, second] };
        var replaced = new LibraryEntrySavedFilter
        {
            Id = first.Id,
            Name = "First (updated)",
            CreatedUtc = first.CreatedUtc,
            Filter = first.Filter,
        };

        var result = Reducers.ReduceUpdateLibraryEntrySuccess(state, new UpdateLibraryEntrySuccessAction(replaced));

        Assert.Equal(2, result.Entries.Count);
        Assert.Same(replaced, result.Entries[0]);
        Assert.Same(second, result.Entries[1]);
    }

    [Fact]
    public void ReduceUpdateLibraryEntrySuccess_UnknownId_IsNoOp()
    {
        var existing = BuildFilterEntry("First");
        var state = new FilterLibraryState { Entries = [existing] };
        var unknown = BuildFilterEntry("Unknown");

        var result = Reducers.ReduceUpdateLibraryEntrySuccess(state, new UpdateLibraryEntrySuccessAction(unknown));

        Assert.Same(state, result);
    }

    private static LibraryEntrySavedFilter BuildFilterEntry(string name)
    {
        var filter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(filter);

        return new LibraryEntrySavedFilter
        {
            Name = name,
            CreatedUtc = new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero),
            Filter = filter,
        };
    }
}
