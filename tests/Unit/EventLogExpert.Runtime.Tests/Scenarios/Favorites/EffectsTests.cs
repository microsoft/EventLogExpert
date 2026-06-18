// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Scenarios.Favorites;
using Fluxor;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.Scenarios.Favorites;

public sealed class EffectsTests
{
    private readonly IAnnouncementService _announcer = Substitute.For<IAnnouncementService>();
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();
    private readonly ITraceLogger _logger = Substitute.For<ITraceLogger>();
    private readonly IState<ScenarioFavoritesState> _state = Substitute.For<IState<ScenarioFavoritesState>>();
    private readonly IScenarioFavoriteStore _store = Substitute.For<IScenarioFavoriteStore>();

    [Fact]
    public async Task HandleLoadScenarioFavorites_LoadFailure_DoesNotDispatchSuccess()
    {
        _state.Value.Returns(new ScenarioFavoritesState());
        _store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<string>>(new IOException("boom")));
        var sut = new Effects(_store, _state, _announcer, _logger);

        await sut.HandleLoadScenarioFavorites(_dispatcher);

        _dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadScenarioFavoritesSuccessAction>());
    }

    [Fact]
    public async Task HandleLoadScenarioFavorites_Success_DispatchesLoadedIds()
    {
        _state.Value.Returns(new ScenarioFavoritesState());
        _store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["application-crashes", "failed-services-at-boot"]));
        var sut = new Effects(_store, _state, _announcer, _logger);

        await sut.HandleLoadScenarioFavorites(_dispatcher);

        _dispatcher.Received(1).Dispatch(Arg.Is<LoadScenarioFavoritesSuccessAction>(action =>
            action.FavoriteScenarioIds.Contains("application-crashes") &&
            action.FavoriteScenarioIds.Contains("failed-services-at-boot")));
    }

    [Fact]
    public async Task HandleLoadScenarioFavorites_WhenAlreadyLoaded_DoesNotReadStore()
    {
        _state.Value.Returns(new ScenarioFavoritesState { IsLoaded = true });
        var sut = new Effects(_store, _state, _announcer, _logger);

        await sut.HandleLoadScenarioFavorites(_dispatcher);

        await _store.DidNotReceive().LoadAllAsync(Arg.Any<CancellationToken>());
        _dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadScenarioFavoritesSuccessAction>());
    }

    [Fact]
    public async Task HandleSetScenarioFavorite_Favorite_PersistsAnnouncesAndDispatches()
    {
        _state.Value.Returns(new ScenarioFavoritesState());
        var sut = new Effects(_store, _state, _announcer, _logger);

        await sut.HandleSetScenarioFavorite(
            new SetScenarioFavoriteAction("application-crashes", "Application crashes", IsFavorite: true), _dispatcher);

        await _store.Received(1).AddAsync("application-crashes", Arg.Any<CancellationToken>());
        _announcer.Received(1).Announce("Added Application crashes to favorites");
        _dispatcher.Received(1).Dispatch(Arg.Is<SetScenarioFavoriteSuccessAction>(action =>
            action.ScenarioId == "application-crashes" && action.IsFavorite));
    }

    [Fact]
    public async Task HandleSetScenarioFavorite_NoOpWhenAlreadyInDesiredState()
    {
        _state.Value.Returns(new ScenarioFavoritesState { FavoriteScenarioIds = ["application-crashes"] });
        var sut = new Effects(_store, _state, _announcer, _logger);

        await sut.HandleSetScenarioFavorite(
            new SetScenarioFavoriteAction("application-crashes", "Application crashes", IsFavorite: true), _dispatcher);

        await _store.DidNotReceive().AddAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _announcer.DidNotReceive().Announce(Arg.Any<string>());
        _dispatcher.DidNotReceive().Dispatch(Arg.Any<SetScenarioFavoriteSuccessAction>());
    }

    [Fact]
    public async Task HandleSetScenarioFavorite_PersistFailure_NoAnnounceNoSuccess()
    {
        _state.Value.Returns(new ScenarioFavoritesState());
        _store.AddAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("boom")));
        var sut = new Effects(_store, _state, _announcer, _logger);

        await sut.HandleSetScenarioFavorite(
            new SetScenarioFavoriteAction("application-crashes", "Application crashes", IsFavorite: true), _dispatcher);

        _announcer.DidNotReceive().Announce(Arg.Any<string>());
        _dispatcher.DidNotReceive().Dispatch(Arg.Any<SetScenarioFavoriteSuccessAction>());
    }

    [Fact]
    public async Task HandleSetScenarioFavorite_Unfavorite_DeletesAndAnnounces()
    {
        _state.Value.Returns(new ScenarioFavoritesState { FavoriteScenarioIds = ["application-crashes"] });
        var sut = new Effects(_store, _state, _announcer, _logger);

        await sut.HandleSetScenarioFavorite(
            new SetScenarioFavoriteAction("application-crashes", "Application crashes", IsFavorite: false), _dispatcher);

        await _store.Received(1).DeleteAsync("application-crashes", Arg.Any<CancellationToken>());
        _announcer.Received(1).Announce("Removed Application crashes from favorites");
        _dispatcher.Received(1).Dispatch(Arg.Is<SetScenarioFavoriteSuccessAction>(action => !action.IsFavorite));
    }
}
