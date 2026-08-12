// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Scenarios.Favorites;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.Scenarios.Favorites;

public sealed class ScenarioFavoritesSourceTests
{
    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness("scenario-a");
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetState(ImmutableHashSet.Create("scenario-a", "scenario-b"));

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenAFreshSetHasEqualContent()
    {
        var harness = new Harness("scenario-a", "scenario-b");
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(ImmutableHashSet.Create("scenario-b", "scenario-a"));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_FiresWithLatestFavorites_WhenContentChanges()
    {
        var harness = new Harness("scenario-a");
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetState(ImmutableHashSet.Create("scenario-a", "scenario-b"));

        Assert.Equal(1, raised);
        Assert.Contains("scenario-b", harness.Source.FavoriteScenarioIds);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // populated state (the reconcile), with no StateChanged raised in between.
        var state = Substitute.For<IState<ScenarioFavoritesState>>();
        state.Value.Returns(new ScenarioFavoritesState(), WithFavorites("scenario-a"));

        using var source = new ScenarioFavoritesSource(state, Substitute.For<ITraceLogger>());

        Assert.Contains("scenario-a", source.FavoriteScenarioIds);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness("scenario-a");
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetState(ImmutableHashSet.Create("scenario-a", "scenario-b"));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void FavoriteScenarioIds_ReflectsTheInitialState()
    {
        var harness = new Harness("scenario-a", "scenario-b");

        Assert.Equal(2, harness.Source.FavoriteScenarioIds.Count);
        Assert.Contains("scenario-a", harness.Source.FavoriteScenarioIds);
        Assert.Contains("scenario-b", harness.Source.FavoriteScenarioIds);
    }

    private static ScenarioFavoritesState WithFavorites(params string[] ids) =>
        new() { FavoriteScenarioIds = [.. ids] };

    private sealed class Harness
    {
        private readonly IState<ScenarioFavoritesState> _state = Substitute.For<IState<ScenarioFavoritesState>>();

        public Harness(params string[] ids)
        {
            State = WithFavorites(ids);
            _state.Value.Returns(_ => State);
            Source = new ScenarioFavoritesSource(_state, Substitute.For<ITraceLogger>());
        }

        public ScenarioFavoritesSource Source { get; }

        public ScenarioFavoritesState State { get; private set; }

        public void SetState(ImmutableHashSet<string> ids)
        {
            State = new ScenarioFavoritesState { FavoriteScenarioIds = ids };
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
