// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.UI.FilterEditor;
using NSubstitute;
using System.Reflection;

namespace EventLogExpert.UI.Tests.FilterEditor;

public sealed class FilterRowLifecycleTests
{
    [Fact]
    public void Dispose_InvokesOnDisposedWithFilterId()
    {
        FilterId? disposed = null;
        var filter = SavedFilter.TryCreate("Level == 4")!;
        var row = new FilterRow();
        typeof(FilterRow).GetProperty(nameof(FilterRowBase<SavedFilter?>.Value))!
            .SetValue(row, filter);
        typeof(FilterRow).GetProperty(nameof(FilterRow.OnDisposed))!
            .SetValue(row, (Action<FilterId>)(id => disposed = id));
        typeof(FilterRow).GetProperty("LibraryEntries", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(row, Substitute.For<ILibraryEntriesSource>());

        row.Dispose();

        Assert.Equal(filter.Id, disposed);
    }
}
