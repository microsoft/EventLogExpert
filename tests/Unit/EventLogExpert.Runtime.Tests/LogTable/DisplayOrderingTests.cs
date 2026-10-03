// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class DisplayOrderingTests
{
    [Fact]
    public void Equality_IncludesRequestedFields()
    {
        var committed = new DisplayOrdering(ColumnName.Source, IsDescending: true, GroupBy: null, IsGroupDescending: false);
        var requestedFlip = committed with { RequestedIsDescending = false };

        Assert.NotEqual(committed, requestedFlip);
    }

    [Fact]
    public void RequestedFields_DefaultToCommitted_WhenNotSpecified()
    {
        var ordering = new DisplayOrdering(ColumnName.Source, IsDescending: true, ColumnName.Level, IsGroupDescending: false);

        Assert.Equal(ColumnName.Source, ordering.RequestedOrderBy);
        Assert.True(ordering.RequestedIsDescending);
        Assert.Equal(ColumnName.Level, ordering.RequestedGroupBy);
        Assert.False(ordering.RequestedIsGroupDescending);
    }

    [Fact]
    public void RequestedFields_OverrideCommitted_WithoutMutatingCommitted()
    {
        var ordering = new DisplayOrdering(ColumnName.Source, IsDescending: true, GroupBy: null, IsGroupDescending: false)
        {
            RequestedOrderBy = ColumnName.Level,
            RequestedIsDescending = false
        };

        Assert.Equal(ColumnName.Source, ordering.OrderBy);
        Assert.True(ordering.IsDescending);
        Assert.Equal(ColumnName.Level, ordering.RequestedOrderBy);
        Assert.False(ordering.RequestedIsDescending);
    }
}
