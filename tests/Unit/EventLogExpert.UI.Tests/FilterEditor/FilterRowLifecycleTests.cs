// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.FilterEditor;

namespace EventLogExpert.UI.Tests.FilterEditor;

public sealed class FilterRowLifecycleTests
{
    [Fact]
    public void Dispose_InvokesOnDisposedWithSelf()
    {
        FilterRow? disposed = null;
        var row = new FilterRow();
        typeof(FilterRow).GetProperty(nameof(FilterRow.OnDisposed))!
            .SetValue(row, (Action<FilterRow>)(r => disposed = r));

        row.Dispose();

        Assert.Same(row, disposed);
    }
}
