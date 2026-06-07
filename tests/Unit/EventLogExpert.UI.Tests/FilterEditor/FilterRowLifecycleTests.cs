// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.UI.FilterEditor;

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

        row.Dispose();

        Assert.Equal(filter.Id, disposed);
    }
}
