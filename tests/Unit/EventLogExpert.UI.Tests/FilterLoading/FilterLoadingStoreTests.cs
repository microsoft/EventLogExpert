// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.FilterLoading;

namespace EventLogExpert.UI.Tests.FilterLoading;

public sealed class FilterLoadingStateTests
{
    [Fact]
    public void FilterLoadingState_DefaultState_ShouldNotBeLoading()
    {
        var state = new FilterLoadingState();

        Assert.False(state.IsLoading);
    }
}

public sealed class FilterLoadingActionTests
{
    [Fact]
    public void SetFilterLoadingAction_ShouldCreateAction()
    {
        var action = new SetFilterLoadingAction(true);

        Assert.True(action.IsLoading);
    }
}

public sealed class FilterLoadingReducerTests
{
    [Fact]
    public void ReduceCloseAll_WhenFilterLoading_ShouldClearLoadingFlag()
    {
        var state = new FilterLoadingState { IsLoading = true };

        var result = Reducers.ReduceCloseAll(state);

        Assert.False(result.IsLoading);
    }

    [Fact]
    public void ReduceCloseAll_WhenNotFilterLoading_ShouldReturnSameState()
    {
        var state = new FilterLoadingState { IsLoading = false };

        var result = Reducers.ReduceCloseAll(state);

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceSetFilterLoading_WithDifferentValue_ShouldFlagFilterLoading()
    {
        var state = new FilterLoadingState { IsLoading = false };

        var result = Reducers.ReduceSetFilterLoading(state, new SetFilterLoadingAction(true));

        Assert.True(result.IsLoading);
    }

    [Fact]
    public void ReduceSetFilterLoading_WithUnchangedValue_ShouldReturnSameState()
    {
        var state = new FilterLoadingState { IsLoading = true };

        var result = Reducers.ReduceSetFilterLoading(state, new SetFilterLoadingAction(true));

        Assert.Same(state, result);
    }
}
