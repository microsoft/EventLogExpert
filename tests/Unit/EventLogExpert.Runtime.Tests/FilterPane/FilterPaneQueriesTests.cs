// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class FilterPaneQueriesTests
{
    [Fact]
    public void IsEnabled_ReadsIsEnabled_NotIsFilteringEnabled()
    {
        var state = new FilterPaneState { IsEnabled = true, Filters = [] };

        Assert.True(new FilterPaneQueries(StateReturning(state)).IsEnabled());
        Assert.False(state.IsFilteringEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsEnabled_ReflectsTheMasterToggle(bool enabled)
    {
        var queries = new FilterPaneQueries(StateReturning(new FilterPaneState { IsEnabled = enabled }));

        Assert.Equal(enabled, queries.IsEnabled());
    }

    private static IState<FilterPaneState> StateReturning(FilterPaneState state)
    {
        var stateMock = Substitute.For<IState<FilterPaneState>>();
        stateMock.Value.Returns(state);

        return stateMock;
    }
}
