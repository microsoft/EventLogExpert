// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLenses;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.FilterLenses;

public sealed class FilterLensSourceTests
{
    [Fact]
    public void AThrowingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        var harness = new Harness(Lens("First"));
        var reachedSecond = 0;
        harness.Source.Changed += () => throw new InvalidOperationException("subscriber blew up");
        harness.Source.Changed += () => reachedSecond++;

        harness.SetLenses(harness.State.Lenses.Add(Lens("Second")));

        Assert.Equal(1, reachedSecond);
    }

    [Fact]
    public void Changed_DoesNotFire_WhenProjectionUnchanged()
    {
        var lens = Lens("First");
        var harness = new Harness(lens);
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.SetLenses([lens with { OriginLog = "not-part-of-the-projection" }]);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Changed_FiresWithLatestLenses_OnStateChange()
    {
        var harness = new Harness(Lens("First"));
        var raised = 0;
        harness.Source.Changed += () => raised++;

        var added = Lens("Second");
        harness.SetLenses(harness.State.Lenses.Add(added));

        Assert.Equal(1, raised);
        Assert.Equal(2, harness.Source.Lenses.Count);
        Assert.Equal(new FilterLensSummary(added.Id, PropertyLabel("Second")), harness.Source.Lenses[^1]);
    }

    [Fact]
    public void Construction_AdoptsAChangeThatLandsBetweenSeedAndSubscribe()
    {
        // one-lens state (the reconcile), with no StateChanged raised in between.
        var state = Substitute.For<IState<FilterLensState>>();
        state.Value.Returns(new FilterLensState(), new FilterLensState { Lenses = [Lens("First")] });

        using var source = new FilterLensSource(state, Substitute.For<ITraceLogger>());

        Assert.Single(source.Lenses);
    }

    [Fact]
    public void Dispose_StopsRaising()
    {
        var harness = new Harness(Lens("First"));
        var raised = 0;
        harness.Source.Changed += () => raised++;

        harness.Source.Dispose();
        harness.SetLenses(harness.State.Lenses.Add(Lens("Second")));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Lenses_ProjectTheLensKind_SoTimeWindowLensesAreNotTreatedAsValueLenses()
    {
        // Guards the FilterLensSummary.Kind projection: if it were dropped, a time-window lens would default to
        // Property and wrongly enable "Save as group" (which a filter set cannot store a date window for).
        var time = FilterLensFactory.ForTimeWindow(DateTime.UtcNow, TimeSpan.FromMinutes(5), TimeZoneInfo.Utc);
        var harness = new Harness(time);

        var summary = Assert.Single(harness.Source.Lenses);

        Assert.Equal(LensKind.TimeWindow, summary.Kind);
    }

    [Fact]
    public void Lenses_SummarizeTheActiveLenses()
    {
        var first = Lens("First");
        var second = Lens("Second");
        var harness = new Harness(first, second);

        var lenses = harness.Source.Lenses;

        Assert.Equal(2, lenses.Count);
        Assert.Equal(new FilterLensSummary(first.Id, PropertyLabel("First")), lenses[0]);
        Assert.Equal(new FilterLensSummary(second.Id, PropertyLabel("Second")), lenses[1]);
    }

    private static FilterLens Lens(string value) => new() { Label = PropertyLabel(value), Kind = LensKind.Property };

    private static FilterLensLabel PropertyLabel(string value) =>
        new FilterLensLabel.PropertyComparison(EventProperty.ActivityId, IsEqual: true, value);

    private sealed class Harness
    {
        private readonly IState<FilterLensState> _state = Substitute.For<IState<FilterLensState>>();

        public Harness(params FilterLens[] lenses)
        {
            State = new FilterLensState { Lenses = [.. lenses] };
            _state.Value.Returns(_ => State);
            Source = new FilterLensSource(_state, Substitute.For<ITraceLogger>());
        }

        public FilterLensSource Source { get; }

        public FilterLensState State { get; private set; }

        public void SetLenses(ImmutableList<FilterLens> lenses)
        {
            State = State with { Lenses = lenses };
            _state.StateChanged += Raise.Event<EventHandler>(_state, EventArgs.Empty);
        }
    }
}
