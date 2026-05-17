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

public sealed class FilterProgressActionTests
{
    [Fact]
    public void SetFilterProgressAction_ShouldCreateAction()
    {
        var action = new SetFilterProgressAction(true);

        Assert.True(action.IsLoading);
    }
}

public sealed class FilterProgressReducerTests
{
    [Fact]
    public void ReduceCloseAll_WhenLoading_ShouldClearLoadingFlag()
    {
        var state = new FilterProgressState { IsLoading = true };

        var result = Reducers.ReduceCloseAll(state);

        Assert.False(result.IsLoading);
    }

    [Fact]
    public void ReduceCloseAll_WhenNotLoading_ShouldReturnSameState()
    {
        var state = new FilterProgressState { IsLoading = false };

        var result = Reducers.ReduceCloseAll(state);

        Assert.Same(state, result);
    }

    [Fact]
    public void ReduceSetFilterProgress_WithDifferentValue_ShouldFlagLoading()
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
