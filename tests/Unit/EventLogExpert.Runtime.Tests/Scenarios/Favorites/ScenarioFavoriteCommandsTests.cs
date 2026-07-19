// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Scenarios.Favorites;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Scenarios.Favorites;

public sealed class ScenarioFavoriteCommandsTests
{
    [Fact]
    public void Load_DispatchesLoadAction()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new ScenarioFavoriteCommands(dispatcher);

        sut.Load();

        dispatcher.Received(1).Dispatch(Arg.Any<LoadScenarioFavoritesAction>());
    }

    [Fact]
    public void SetFavorite_Favorite_DispatchesActionWithArguments()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new ScenarioFavoriteCommands(dispatcher);

        sut.SetFavorite("application-crashes", "Application crashes", isFavorite: true);

        dispatcher.Received(1).Dispatch(Arg.Is<SetScenarioFavoriteAction>(action =>
            action != null &&
            action.ScenarioId == "application-crashes" &&
            action.ScenarioName == "Application crashes" &&
            action.IsFavorite));
    }

    [Fact]
    public void SetFavorite_Unfavorite_DispatchesActionWithFalse()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var sut = new ScenarioFavoriteCommands(dispatcher);

        sut.SetFavorite("application-crashes", "Application crashes", isFavorite: false);

        dispatcher.Received(1).Dispatch(Arg.Is<SetScenarioFavoriteAction>(action =>
            action != null && action.ScenarioId == "application-crashes" && !action.IsFavorite));
    }
}
