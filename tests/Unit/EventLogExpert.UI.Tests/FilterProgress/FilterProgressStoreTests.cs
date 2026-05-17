// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.FilterProgress;

namespace EventLogExpert.UI.Tests.FilterProgress;

public sealed class FilterProgressStateTests
{
    [Fact]
    public void FilterProgressState_DefaultState_ShouldNotBeLoading()
    {
        var state = new FilterProgressState();

        Assert.False(state.IsLoading);
    }
}

public sealed class FilterLoadingActionTests
{
    [Fact]
    public void SetFilterProgressAction_ShouldCreateAction()
    {
        var action = new SetFilterProgressAction(true);

        Assert.True(action.IsLoading);
    }
}

public sealed class FilterLoadingReducerTests
{
    [Fact]
    public void ReduceCloseAll_WhenFilterLoading_ShouldClearLoadingFlag()
    {
        var state = new FilterProgressState { IsLoading = true };

        var result = Reducers.ReduceCloseAll(state);

        Assert.False(result.IsLoading);
    }

    [Fact]
    public void ReduceCloseAll_WhenNotFilterLoading_ShouldReturnSameState()
    {
        var state = new FilterProgressState { IsLoading = false };

        var result = Reducers.ReduceCloseAll(state);

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceSetFilterProgress_WithDifferentValue_ShouldFlagFilterLoading()
    {
        var state = new FilterProgressState { IsLoading = false };

        var result = Reducers.ReduceSetFilterProgress(state, new SetFilterProgressAction(true));

        Assert.True(result.IsLoading);
    }

    [Fact]
    public void ReduceSetFilterProgress_WithUnchangedValue_ShouldReturnSameState()
    {
        var state = new FilterProgressState { IsLoading = true };

        var result = Reducers.ReduceSetFilterProgress(state, new SetFilterProgressAction(true));

        Assert.Same(state, result);
    }
}
