// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using NSubstitute;
using System.Collections.Immutable;
using Effects = EventLogExpert.Runtime.FilterPane.Effects;
using SetFilterAction = EventLogExpert.Runtime.FilterPane.SetFilterAction;

namespace EventLogExpert.Runtime.Tests.FilterPane;

public sealed class EffectsTests
{
    [Fact]
    public async Task HandleAddFilter_WhenComparisonValueExists_ShouldUpdateEventTableFilters()
    {
        var filterModel = FilterBuilder.CreateTestFilter(isEnabled: true);

        var (effects, mockDispatcher) = CreateEffects(true, ImmutableList.Create(filterModel));
        var action = new AddFilterAction(filterModel);

        await effects.HandleAddFilter(action, mockDispatcher);

        mockDispatcher.Received().Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleAddFilter_WhenComparisonValueIsNull_ShouldNotUpdateEventTableFilters()
    {
        var filterModel = new SavedFilter
        {
            ComparisonText = string.Empty,
            Compiled = null
        };

        var (effects, mockDispatcher) = CreateEffects();
        var action = new AddFilterAction(filterModel);

        await effects.HandleAddFilter(action, mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleAddFilter_WhenFilterHasBasicFilter_ShouldRecordFilterApplied()
    {
        var filterModel = FilterBuilder.CreateTestFilter(basicFilter: CreateBasicFilter());

        var (effects, mockDispatcher) = CreateEffects();
        var action = new AddFilterAction(filterModel);

        await effects.HandleAddFilter(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<RecordFilterAppliedAction>(x =>
            x != null &&
            x.Filter.ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleAddFilter_WhenFilterHasNoBasicFilter_ShouldRecordFilterApplied()
    {
        var filterModel = FilterBuilder.CreateTestFilter();

        var (effects, mockDispatcher) = CreateEffects();
        var action = new AddFilterAction(filterModel);

        await effects.HandleAddFilter(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<RecordFilterAppliedAction>(x =>
            x != null &&
            x.Filter.ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleClearAllFilters_RaisesTheNotifierOncePerDispatch()
    {
        var filterPaneState = Substitute.For<IState<FilterPaneState>>();
        filterPaneState.Value.Returns(new FilterPaneState());
        var appliedFilter = Substitute.For<IStateSelection<EventLogState, Filter>>();
        appliedFilter.Value.Returns(new Filter(null, []));
        var lensState = Substitute.For<IState<FilterLensState>>();
        lensState.Value.Returns(new FilterLensState());

        var notifier = new ClearAllFiltersNotifier(Substitute.For<ITraceLogger>());
        var raised = 0;
        notifier.Requested += () => raised++;

        var effects = new Effects(
            appliedFilter, filterPaneState, lensState,
            notifier, new SetFilterDateRangeSucceededNotifier(Substitute.For<ITraceLogger>()),
            new FilterPromotedNotifier(Substitute.For<ITraceLogger>()));

        await effects.HandleClearAllFilters(Substitute.For<IDispatcher>());
        await effects.HandleClearAllFilters(Substitute.For<IDispatcher>());

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task HandleClearAllFilters_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(
            true,
            appliedFilter: new Filter(null, CreateSingleEnabledFilters()));

        await effects.HandleClearAllFilters(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleCommitPromotedLenses_AnyWindowCommit_RaisesDateResyncOnce()
    {
        // One resync for the batch even though only one commit carries an enabled window; still one promote + one apply.
        var window = new DateFilter
        {
            After = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Before = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            IsEnabled = true
        };
        var (effects, dispatcher, promotedRaised, dateResyncRaised) = CreateCommitPromotedEffects(
            new FilterPaneState { FilteredDateRange = window },
            appliedFilter: new Filter(null, []));

        await effects.HandleCommitPromotedLenses(
            new CommitPromotedLensesAction(
                [
                    new PromotedLensCommit(FilterLensId.Create(), [], null),
                    new PromotedLensCommit(FilterLensId.Create(), [], window)
                ]),
            dispatcher);

        Assert.Equal(1, dateResyncRaised());
        Assert.Equal(1, promotedRaised());
        dispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleCommitPromotedLenses_MultipleCommits_AppliesOnceUnconditionally_RaisesPromotedOnce()
    {
        // The whole batch applies the filter exactly ONCE (not once per commit) and raises FilterPromoted once. The
        // empty candidate equals the empty applied filter, so the apply must be unconditional to land last and win.
        var (effects, dispatcher, promotedRaised, dateResyncRaised) = CreateCommitPromotedEffects(
            new FilterPaneState(),
            appliedFilter: new Filter(null, []));

        await effects.HandleCommitPromotedLenses(
            new CommitPromotedLensesAction(
                [
                    new PromotedLensCommit(FilterLensId.Create(), [], null),
                    new PromotedLensCommit(FilterLensId.Create(), [], null)
                ]),
            dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
        Assert.Equal(1, promotedRaised());
        Assert.Equal(0, dateResyncRaised());
    }

    [Fact]
    public async Task HandleCommitPromoted_AppliesEvenWhenFilteringUnchanged()
    {
        // The empty candidate equals the empty applied filter, so the guarded path would suppress. The unconditional
        // promote apply must dispatch anyway so it lands last and wins any race with a queued wide apply.
        var (effects, dispatcher, promotedRaised, _) = CreateCommitPromotedEffects(
            new FilterPaneState(),
            appliedFilter: new Filter(null, []));

        await effects.HandleCommitPromoted(new CommitPromotedLensAction(FilterLensId.Create(), [], null), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
        Assert.Equal(1, promotedRaised());
    }

    [Fact]
    public async Task HandleCommitPromoted_PropertyLens_DispatchesBakedFilter_RaisesPromoted_NotDateResync()
    {
        var promoted = FilterBuilder.CreateTestFilter(isEnabled: true, isExcluded: true);
        var (effects, dispatcher, promotedRaised, dateResyncRaised) = CreateCommitPromotedEffects(
            new FilterPaneState { Filters = [promoted] },
            appliedFilter: new Filter(null, []));

        await effects.HandleCommitPromoted(new CommitPromotedLensAction(FilterLensId.Create(), [], null), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<ApplyFilterAction>(applied =>
            applied != null && applied.Filter.Filters.Count == 1 && applied.Filter.Filters[0].IsExcluded));
        Assert.Equal(1, promotedRaised());
        Assert.Equal(0, dateResyncRaised());
    }

    [Fact]
    public async Task HandleCommitPromoted_TimeWindow_RaisesDateResyncNotifier()
    {
        // A promoted window writes FilteredDateRange, so the date editor must be told to resync its model.
        var window = new DateFilter
        {
            After = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Before = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            IsEnabled = true
        };
        var (effects, dispatcher, promotedRaised, dateResyncRaised) = CreateCommitPromotedEffects(
            new FilterPaneState { FilteredDateRange = window },
            appliedFilter: new Filter(null, []));

        await effects.HandleCommitPromoted(new CommitPromotedLensAction(FilterLensId.Create(), [], window), dispatcher);

        Assert.Equal(1, dateResyncRaised());
        Assert.Equal(1, promotedRaised());
    }

    [Fact]
    public async Task HandleMergeFilters_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        await effects.HandleMergeFilters(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleNarrowFilterDateRange_AfterOnly_KeepsExistingBeforeAndEnablesRange()
    {
        var newAfter = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var existingBefore = new DateTime(2024, 1, 1, 16, 0, 0, DateTimeKind.Utc);
        var (effects, dispatcher) = CreateEffects(
            filteredDateRange: new DateFilter { After = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc), Before = existingBefore });

        await effects.HandleNarrowFilterDateRange(new NarrowFilterDateRangeAction(After: newAfter, Before: null), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(action =>
            action != null && action.DateFilter == new DateFilter
            {
                After = newAfter,
                Before = existingBefore,
                IsEnabled = true
            }));
    }

    [Fact]
    public async Task HandleNarrowFilterDateRange_BeforeOnly_KeepsExistingAfterAndEnablesRange()
    {
        var existingAfter = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var newBefore = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var (effects, dispatcher) = CreateEffects(
            filteredDateRange: new DateFilter { After = existingAfter, Before = new DateTime(2024, 1, 1, 16, 0, 0, DateTimeKind.Utc) });

        await effects.HandleNarrowFilterDateRange(new NarrowFilterDateRangeAction(After: null, Before: newBefore), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(action =>
            action != null && action.DateFilter == new DateFilter
            {
                After = existingAfter,
                Before = newBefore,
                IsEnabled = true
            }));
    }

    [Fact]
    public async Task HandleNarrowFilterDateRange_WhenRangeIsDisabled_DoesNotCarryItsBounds()
    {
        var newAfter = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var (effects, dispatcher) = CreateEffects(
            filteredDateRange: new DateFilter { Before = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), IsEnabled = false });

        await effects.HandleNarrowFilterDateRange(new NarrowFilterDateRangeAction(After: newAfter, Before: null), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(action =>
            action != null && action.DateFilter == new DateFilter { After = newAfter, IsEnabled = true }));
    }

    [Fact]
    public async Task HandleNarrowFilterDateRange_WhenTheSuppliedBoundIsLooser_KeepsTheTighterExistingBound()
    {
        var existingAfter = new DateTime(2024, 1, 1, 15, 0, 0, DateTimeKind.Utc);
        var (effects, dispatcher) = CreateEffects(filteredDateRange: new DateFilter { After = existingAfter });

        await effects.HandleNarrowFilterDateRange(
            new NarrowFilterDateRangeAction(After: new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc), Before: null),
            dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(action =>
            action != null && action.DateFilter == new DateFilter { After = existingAfter, IsEnabled = true }));
    }

    [Fact]
    public async Task HandleNarrowFilterDateRange_WithoutExistingRange_LeavesUnspecifiedSideOpen()
    {
        var before = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var (effects, dispatcher) = CreateEffects();

        await effects.HandleNarrowFilterDateRange(new NarrowFilterDateRangeAction(After: null, Before: before), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(action =>
            action != null && action.DateFilter == new DateFilter
            {
                Before = before,
                IsEnabled = true
            }));
    }

    [Fact]
    public async Task HandleReinterpretFilterDateRange_AfterAClearAndRestore_StillRestoresTheOriginalReading()
    {
        var daylightBegins = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 2, DayOfWeek.Sunday);
        var daylightEnds = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), month: 11, week: 1, DayOfWeek.Sunday);
        var easternZone = TimeZoneInfo.CreateCustomTimeZone(
            "test-eastern", TimeSpan.FromHours(-5), "test-eastern", "test-eastern", "test-eastern",
            [TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2024, 1, 1), new DateTime(2024, 12, 31), TimeSpan.FromHours(1), daylightBegins, daylightEnds)]);
        var originalAfter = new DateTime(2024, 3, 10, 2, 30, 0, DateTimeKind.Utc);
        var paneState = Substitute.For<IState<FilterPaneState>>();
        DateFilter? currentRange = new DateFilter { After = originalAfter };
        paneState.Value.Returns(_ => new FilterPaneState { FilteredDateRange = currentRange });
        var (effects, dispatcher) = CreateEffects(filterPaneState: paneState);
        dispatcher.When(mock => mock.Dispatch(Arg.Any<SetFilterDateRangeSuccessAction>()))
            .Do(call => currentRange = call.Arg<SetFilterDateRangeSuccessAction>().DateFilter);

        await effects.HandleReinterpretFilterDateRange(
            new ReinterpretFilterDateRangeAction(TimeZoneInfo.Utc, easternZone), dispatcher);
        var rangeBeforeScenarioLaunch = currentRange;
        // A failed scenario launch clears the range, then restores the prior pane state.
        await effects.HandleSetFilterDateRange(new SetFilterDateRangeAction(null), dispatcher);
        currentRange = rangeBeforeScenarioLaunch;
        await effects.HandleReinterpretFilterDateRange(
            new ReinterpretFilterDateRangeAction(easternZone, TimeZoneInfo.Utc), dispatcher);

        Assert.Equal(originalAfter, currentRange?.After);
    }

    [Fact]
    public async Task HandleReinterpretFilterDateRange_AfterTheUserSetsTheRange_ReadsTheNewRangeNotTheRememberedOne()
    {
        var daylightBegins = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 2, DayOfWeek.Sunday);
        var daylightEnds = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), month: 11, week: 1, DayOfWeek.Sunday);
        var easternZone = TimeZoneInfo.CreateCustomTimeZone(
            "test-eastern", TimeSpan.FromHours(-5), "test-eastern", "test-eastern", "test-eastern",
            [TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2024, 1, 1), new DateTime(2024, 12, 31), TimeSpan.FromHours(1), daylightBegins, daylightEnds)]);
        var transitionInstant = new DateTime(2024, 3, 10, 7, 0, 0, DateTimeKind.Utc);
        var paneState = Substitute.For<IState<FilterPaneState>>();
        var currentRange = new DateFilter { After = new DateTime(2024, 3, 10, 2, 30, 0, DateTimeKind.Utc) };
        paneState.Value.Returns(_ => new FilterPaneState { FilteredDateRange = currentRange });
        var (effects, dispatcher) = CreateEffects(filterPaneState: paneState);
        dispatcher.When(mock => mock.Dispatch(Arg.Any<SetFilterDateRangeSuccessAction>()))
            .Do(call => currentRange = call.Arg<SetFilterDateRangeSuccessAction>().DateFilter);

        await effects.HandleReinterpretFilterDateRange(
            new ReinterpretFilterDateRangeAction(TimeZoneInfo.Utc, easternZone), dispatcher);
        // The user applies 03:00 Eastern, which is the same instant the skipped 02:30 was re-read to.
        await effects.HandleSetFilterDateRange(new SetFilterDateRangeAction(new DateFilter { After = transitionInstant }), dispatcher);
        await effects.HandleReinterpretFilterDateRange(
            new ReinterpretFilterDateRangeAction(easternZone, TimeZoneInfo.Utc), dispatcher);

        Assert.Equal(new DateTime(2024, 3, 10, 3, 0, 0, DateTimeKind.Utc), currentRange?.After);
    }

    [Fact]
    public async Task HandleReinterpretFilterDateRange_KeepsWallClockReadingsAndDisabledState()
    {
        var previousZone = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test+2", "test+2");
        var newZone = TimeZoneInfo.CreateCustomTimeZone("test-5", TimeSpan.FromHours(-5), "test-5", "test-5");
        var currentRange = new DateFilter
        {
            After = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            Before = new DateTime(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc),
            IsEnabled = false
        };
        var (effects, dispatcher) = CreateEffects(filteredDateRange: currentRange);

        await effects.HandleReinterpretFilterDateRange(
            new ReinterpretFilterDateRangeAction(previousZone, newZone), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(action =>
            action != null && action.DateFilter == new DateFilter
            {
                After = new DateTime(2024, 1, 1, 17, 0, 0, DateTimeKind.Utc),
                Before = new DateTime(2024, 1, 1, 21, 0, 0, DateTimeKind.Utc),
                IsEnabled = false
            }));
    }

    [Fact]
    public async Task HandleReinterpretFilterDateRange_RoundTripThroughADaylightSavingGap_RestoresTheOriginalReading()
    {
        var daylightBegins = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), month: 3, week: 2, DayOfWeek.Sunday);
        var daylightEnds = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0), month: 11, week: 1, DayOfWeek.Sunday);
        var easternZone = TimeZoneInfo.CreateCustomTimeZone(
            "test-eastern", TimeSpan.FromHours(-5), "test-eastern", "test-eastern", "test-eastern",
            [TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2024, 1, 1), new DateTime(2024, 12, 31), TimeSpan.FromHours(1), daylightBegins, daylightEnds)]);
        var originalAfter = new DateTime(2024, 3, 10, 2, 30, 0, DateTimeKind.Utc);
        var paneState = Substitute.For<IState<FilterPaneState>>();
        var currentRange = new DateFilter { After = originalAfter };
        paneState.Value.Returns(_ => new FilterPaneState { FilteredDateRange = currentRange });
        var (effects, dispatcher) = CreateEffects(filterPaneState: paneState);
        dispatcher.When(mock => mock.Dispatch(Arg.Any<SetFilterDateRangeSuccessAction>()))
            .Do(call => currentRange = call.Arg<SetFilterDateRangeSuccessAction>().DateFilter);

        await effects.HandleReinterpretFilterDateRange(
            new ReinterpretFilterDateRangeAction(TimeZoneInfo.Utc, easternZone), dispatcher);
        var afterInEastern = currentRange?.After;
        await effects.HandleReinterpretFilterDateRange(
            new ReinterpretFilterDateRangeAction(easternZone, TimeZoneInfo.Utc), dispatcher);

        Assert.Equal(new DateTime(2024, 3, 10, 7, 0, 0, DateTimeKind.Utc), afterInEastern);
        Assert.Equal(originalAfter, currentRange?.After);
    }

    [Fact]
    public async Task HandleReinterpretFilterDateRange_WhenNoRangeExists_DoesNotDispatch()
    {
        var (effects, dispatcher) = CreateEffects();
        var previousZone = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test+2", "test+2");
        var newZone = TimeZoneInfo.CreateCustomTimeZone("test-5", TimeSpan.FromHours(-5), "test-5", "test-5");

        await effects.HandleReinterpretFilterDateRange(
            new ReinterpretFilterDateRangeAction(previousZone, newZone), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<SetFilterDateRangeSuccessAction>());
    }

    [Fact]
    public async Task HandleReinterpretFilterDateRange_WhenZoneDoesNotChangeRange_DoesNotDispatch()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test+2", "test+2");
        var currentRange = new DateFilter
        {
            After = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            Before = new DateTime(2024, 1, 1, 14, 0, 0, DateTimeKind.Utc)
        };
        var (effects, dispatcher) = CreateEffects(filteredDateRange: currentRange);

        await effects.HandleReinterpretFilterDateRange(new ReinterpretFilterDateRangeAction(zone, zone), dispatcher);

        dispatcher.DidNotReceive().Dispatch(Arg.Any<SetFilterDateRangeSuccessAction>());
    }

    [Fact]
    public async Task HandleRemoveAdvancedFilter_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(
            true,
            appliedFilter: new Filter(null, CreateSingleEnabledFilters()));

        await effects.HandleRemoveAdvancedFilter(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleSetFilterDateRangeSuccess_RaisesTheNotifierOncePerDispatch()
    {
        var filterPaneState = Substitute.For<IState<FilterPaneState>>();
        filterPaneState.Value.Returns(new FilterPaneState());
        var appliedFilter = Substitute.For<IStateSelection<EventLogState, Filter>>();
        appliedFilter.Value.Returns(new Filter(null, []));
        var lensState = Substitute.For<IState<FilterLensState>>();
        lensState.Value.Returns(new FilterLensState());

        var notifier = new SetFilterDateRangeSucceededNotifier(Substitute.For<ITraceLogger>());
        var raised = 0;
        notifier.Succeeded += () => raised++;

        var effects = new Effects(
            appliedFilter, filterPaneState, lensState,
            new ClearAllFiltersNotifier(Substitute.For<ITraceLogger>()), notifier,
            new FilterPromotedNotifier(Substitute.For<ITraceLogger>()));

        await effects.HandleSetFilterDateRangeSuccess(Substitute.For<IDispatcher>());
        await effects.HandleSetFilterDateRangeSuccess(Substitute.For<IDispatcher>());

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task HandleSetFilterDateRangeSuccess_ShouldUpdateEventTableFilters()
    {
        var filterModel = FilterBuilder.CreateTestFilter(isEnabled: true);

        var (effects, mockDispatcher) = CreateEffects(true, ImmutableList.Create(filterModel));
        var action = new SetFilterAction(filterModel);

        await effects.HandleSetFilterDateRangeSuccess(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleSetFilterDateRange_PassesReplacementVerbatimWithNullBoundOpen()
    {
        var existingRange = new DateFilter
        {
            After = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            Before = new DateTime(2024, 1, 1, 16, 0, 0, DateTimeKind.Utc)
        };
        var replacement = new DateFilter
        {
            Before = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            IsEnabled = false
        };
        var (effects, dispatcher) = CreateEffects(filteredDateRange: existingRange);

        await effects.HandleSetFilterDateRange(new SetFilterDateRangeAction(replacement), dispatcher);

        dispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(action =>
            action != null && action.DateFilter == replacement));
    }

    [Fact]
    public async Task HandleSetFilterDateRange_WhenFilterDateModelIsNull_ShouldDispatchSuccessWithNull()
    {
        var (effects, mockDispatcher) = CreateEffects();
        var action = new SetFilterDateRangeAction(null);

        await effects.HandleSetFilterDateRange(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<SetFilterDateRangeSuccessAction>(x =>
            x != null &&
            x.DateFilter == null));
    }

    [Fact]
    public async Task HandleSetFilter_ShouldUpdateEventTableFilters()
    {
        var filterModel = FilterBuilder.CreateTestFilter(isEnabled: true);

        var (effects, mockDispatcher) = CreateEffects(true, ImmutableList.Create(filterModel));
        var action = new SetFilterAction(filterModel);

        await effects.HandleSetFilter(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleSetFilter_WhenFilterHasBasicFilter_ShouldRecordFilterApplied()
    {
        var filterModel = FilterBuilder.CreateTestFilter(basicFilter: CreateBasicFilter());

        var (effects, mockDispatcher) = CreateEffects();
        var action = new SetFilterAction(filterModel);

        await effects.HandleSetFilter(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<RecordFilterAppliedAction>(x =>
            x != null &&
            x.Filter.ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleSetFilter_WhenFilterHasNoBasicFilter_ShouldRecordFilterApplied()
    {
        var filterModel = FilterBuilder.CreateTestFilter();

        var (effects, mockDispatcher) = CreateEffects();
        var action = new SetFilterAction(filterModel);

        await effects.HandleSetFilter(action, mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<RecordFilterAppliedAction>(x =>
            x != null &&
            x.Filter.ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task HandleToggleFilterDate_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(
            true,
            filteredDateRange: new DateFilter
            {
                After = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Before = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            });

        await effects.HandleToggleFilterDate(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleToggleFilterEnabled_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        await effects.HandleToggleFilterEnabled(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleToggleFilterExcluded_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        await effects.HandleToggleFilterExcluded(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task HandleToggleIsEnabled_ShouldUpdateEventTableFilters()
    {
        var (effects, mockDispatcher) = CreateEffects(true, CreateSingleEnabledFilters());

        await effects.HandleToggleIsEnabled(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenEquivalentFiltersFromDifferentInstances_ShouldNotDispatch()
    {
        var paneFilters = ImmutableList.Create(
            FilterBuilder.CreateTestFilter(isEnabled: true,
                isExcluded: false));

        var appliedFilters = ImmutableList.Create(
            FilterBuilder.CreateTestFilter(isEnabled: true,
                isExcluded: false));

        var (effects, mockDispatcher) = CreateEffects(
            true,
            paneFilters,
            appliedFilter: new Filter(null, appliedFilters));

        await effects.HandleToggleIsEnabled(mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<ApplyFilterAction>());
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterPaneDisabled_ShouldOnlyKeepExcludedFilters()
    {
        var filters = ImmutableList.Create(
            FilterBuilder.CreateTestFilter(isEnabled: true,
                isExcluded: false),
            FilterBuilder.CreateTestFilter(
                FilterTestConstants.FilterLevelEqualsError,
                isEnabled: true,
                isExcluded: true));

        var (effects, mockDispatcher) = CreateEffects(false, filters);

        await effects.HandleToggleIsEnabled(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<ApplyFilterAction>(x =>
            x != null &&
            x.Filter.Filters.Count == 1 &&
            x.Filter.Filters[0].IsExcluded));
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterPaneEnabled_ShouldIncludeEnabledFilters()
    {
        var filters = ImmutableList.Create(
            FilterBuilder.CreateTestFilter(isEnabled: true,
                isExcluded: false),
            FilterBuilder.CreateTestFilter(
                FilterTestConstants.FilterLevelEqualsError,
                isEnabled: false,
                isExcluded: false));

        var (effects, mockDispatcher) = CreateEffects(true, filters);

        await effects.HandleToggleIsEnabled(mockDispatcher);

        mockDispatcher.Received(1).Dispatch(Arg.Is<ApplyFilterAction>(x =>
            x != null &&
            x.Filter.Filters.Count == 1 &&
            x.Filter.Filters[0].ComparisonText == FilterTestConstants.FilterIdEquals100));
    }

    [Fact]
    public async Task UpdateEventTableFilters_WhenFilterUnchanged_ShouldNotDispatch()
    {
        var filters = CreateSingleEnabledFilters();
        var (effects, mockDispatcher) = CreateEffects(
            true,
            filters,
            appliedFilter: new Filter(null, filters));

        await effects.HandleToggleIsEnabled(mockDispatcher);

        mockDispatcher.DidNotReceive().Dispatch(Arg.Any<ApplyFilterAction>());
    }

    private static BasicFilter CreateBasicFilter() =>
        new(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = FilterTestConstants.FilterValue100
            },
            []);

    private static (Effects Effects, IDispatcher Dispatcher, Func<int> PromotedRaised, Func<int> DateResyncRaised)
        CreateCommitPromotedEffects(FilterPaneState paneState, Filter appliedFilter)
    {
        var filterPaneState = Substitute.For<IState<FilterPaneState>>();
        filterPaneState.Value.Returns(paneState);

        var applied = Substitute.For<IStateSelection<EventLogState, Filter>>();
        applied.Value.Returns(appliedFilter);

        var lensState = Substitute.For<IState<FilterLensState>>();
        lensState.Value.Returns(new FilterLensState());

        var promotedNotifier = new FilterPromotedNotifier(Substitute.For<ITraceLogger>());
        var promotedRaised = 0;
        promotedNotifier.Promoted += () => promotedRaised++;

        var dateResyncNotifier = new SetFilterDateRangeSucceededNotifier(Substitute.For<ITraceLogger>());
        var dateResyncRaised = 0;
        dateResyncNotifier.Succeeded += () => dateResyncRaised++;

        var effects = new Effects(
            applied, filterPaneState, lensState,
            new ClearAllFiltersNotifier(Substitute.For<ITraceLogger>()),
            dateResyncNotifier,
            promotedNotifier);

        return (effects, Substitute.For<IDispatcher>(), () => promotedRaised, () => dateResyncRaised);
    }

    private static (Effects effects, IDispatcher mockDispatcher) CreateEffects(
        bool isEnabled = false,
        ImmutableList<SavedFilter>? filters = null,
        DateFilter? filteredDateRange = null,
        Filter? appliedFilter = null,
        IState<FilterPaneState>? filterPaneState = null)
    {
        var mockFilterPaneState = filterPaneState ?? Substitute.For<IState<FilterPaneState>>();

        if (filterPaneState is null)
        {
            mockFilterPaneState.Value.Returns(new FilterPaneState
            {
                IsEnabled = isEnabled,
                Filters = filters ?? ImmutableList<SavedFilter>.Empty,
                FilteredDateRange = filteredDateRange
            });
        }

        var mockAppliedFilter = Substitute.For<IStateSelection<EventLogState, Filter>>();
        mockAppliedFilter.Value.Returns(appliedFilter ?? new Filter(null, []));

        var mockLensState = Substitute.For<IState<FilterLensState>>();
        mockLensState.Value.Returns(new FilterLensState());

        var effects = new Effects(
            mockAppliedFilter,
            mockFilterPaneState,
            mockLensState,
            new ClearAllFiltersNotifier(Substitute.For<ITraceLogger>()),
            new SetFilterDateRangeSucceededNotifier(Substitute.For<ITraceLogger>()),
            new FilterPromotedNotifier(Substitute.For<ITraceLogger>()));
        var mockDispatcher = Substitute.For<IDispatcher>();

        return (effects, mockDispatcher);
    }

    private static ImmutableList<SavedFilter> CreateSingleEnabledFilters() =>
        ImmutableList.Create(
            FilterBuilder.CreateTestFilter(isEnabled: true));
}
