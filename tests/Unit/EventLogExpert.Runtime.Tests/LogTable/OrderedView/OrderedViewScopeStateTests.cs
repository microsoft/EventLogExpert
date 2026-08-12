// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable.OrderedView;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

public sealed class OrderedViewScopeStateTests
{
    [Fact]
    public void TrySetScope_AfterReset_StillRejectsAStaleLowerVersion()
    {
        var scope = new OrderedViewScopeState();
        Assert.True(scope.TrySetScope([], scopeVersion: 5));

        scope.Reset();

        Assert.False(scope.TrySetScope([], scopeVersion: 3));
        Assert.Equal(5L, scope.ScopeVersion);
    }

    [Fact]
    public void TrySetScope_RejectsAStrictlyLowerVersion()
    {
        var scope = new OrderedViewScopeState();

        Assert.True(scope.TrySetScope([], scopeVersion: 5));
        Assert.False(scope.TrySetScope([], scopeVersion: 4));
        Assert.Equal(5L, scope.ScopeVersion);
    }
}
