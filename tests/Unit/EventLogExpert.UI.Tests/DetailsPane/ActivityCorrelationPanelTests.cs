// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.ActivityCorrelation;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using PanelComponent = EventLogExpert.UI.DetailsPane.ActivityCorrelationPanel;

namespace EventLogExpert.UI.Tests.DetailsPane;

public sealed class ActivityCorrelationPanelTests : BunitContext
{
    private static readonly Guid s_focus = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IEventLogCommands _commands = Substitute.For<IEventLogCommands>();
    private readonly IEventDetailResolver _detailResolver = Substitute.For<IEventDetailResolver>();
    private readonly IFilterLensCommands _lensCommands = Substitute.For<IFilterLensCommands>();
    private readonly EventLogId _logId = EventLogId.Create();
    private readonly ITraceLogger _logger = Substitute.For<ITraceLogger>();
    private readonly IActivityCorrelationService _service = Substitute.For<IActivityCorrelationService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IActivityCorrelationSource _source = Substitute.For<IActivityCorrelationSource>();

    public ActivityCorrelationPanelTests()
    {
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
        _detailResolver.TryResolveLean(Arg.Any<EventLocator>(), out Arg.Any<ResolvedEvent?>())
            .Returns(call =>
            {
                call[1] = LeanEvent();

                return true;
            });
        _service.TryGetContentToken(_logId, out Arg.Any<CorrelationContentToken>())
            .Returns(call =>
            {
                call[1] = FreshToken();

                return true;
            });

        Services.AddSingleton(_service);
        Services.AddSingleton(_source);
        Services.AddSingleton(_detailResolver);
        Services.AddSingleton(_commands);
        Services.AddSingleton(_lensCommands);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_logger);
        Services.AddEventLogLocalization();
    }

    [Fact]
    public void DeactivatingTheTab_CancelsTheInFlightBuild()
    {
        var pendingBuild = new TaskCompletionSource<ActivityCorrelationView?>();
        CancellationToken buildToken = default;
        _service.BuildAsync(Arg.Any<EventLocator>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                buildToken = call.Arg<CancellationToken>();

                return pendingBuild.Task;
            });

        var cut = RenderActive();
        cut.WaitForAssertion(() => Assert.Contains(Localized("Correlation_Building"), cut.Markup));

        // Deactivating while the build is still in flight must cancel its token, not leave it running.
        cut.Render(parameters => parameters.Add(panel => panel.IsActive, false));

        Assert.True(buildToken.IsCancellationRequested);

        // A stale build that completes after cancellation must not surface a timeline.
        pendingBuild.SetResult(ViewWithEvent(new EventLocator(_logId, 0, 0)));
        Assert.Empty(cut.FindAll(".correlation-timeline"));
    }

    [Fact]
    public void EmptyDescription_ShowsTheNoMessageFallback()
    {
        _detailResolver.TryResolveLean(Arg.Any<EventLocator>(), out Arg.Any<ResolvedEvent?>())
            .Returns(call =>
            {
                call[1] = LeanEventWith(string.Empty);

                return true;
            });
        StubBuild(ViewWithEvent(new EventLocator(_logId, 0, 3)));

        var cut = RenderActive();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-row-message")));
        Assert.Contains(Localized("Correlation_NoMessage"), cut.Markup);
    }

    [Fact]
    public void EventClick_SelectsAndRequestsAOneShotReveal()
    {
        var eventLocator = new EventLocator(_logId, 0, 3);
        StubBuild(ViewWithEvent(eventLocator));

        var cut = RenderActive();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-row-button")));

        cut.Find(".correlation-row-button").Click();

        _commands.Received().SetSelectedEvents(Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>());
        _commands.Received().RequestRevealFocus(eventLocator, false);
    }

    [Fact]
    public void EventClick_WhenSnapshotChangedBeforeTheAsyncNotification_RevalidatesAndBlocksNavigation()
    {
        StubBuild(ViewWithEvent(new EventLocator(_logId, 0, 3)));

        var cut = RenderActive();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-row-button")));

        _service.TryGetContentToken(_logId, out Arg.Any<CorrelationContentToken>())
            .Returns(call =>
            {
                call[1] = new CorrelationContentToken(_logId, 0, 99, 1);

                return true;
            });

        cut.Find(".correlation-row-button").Click();

        _commands.DidNotReceive().SetSelectedEvents(Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-stale")));
    }

    [Fact]
    public void FilterToActivity_DispatchesTheActivityIdLens()
    {
        StubBuild(ViewWithEvent(new EventLocator(_logId, 0, 0)));

        var cut = RenderActive();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-filter-action")));

        cut.Find(".correlation-filter-action").Click();

        _lensCommands.Received().ShowRelatedByActivityId(s_focus, Arg.Any<string?>());
    }

    [Fact]
    public void HeaderShowsErrorAndWarningCounts()
    {
        StubBuild(ViewWithEvent(new EventLocator(_logId, 0, 0), errorCount: 2, warningCount: 1));

        var cut = RenderActive();

        cut.WaitForAssertion(() => Assert.Contains(Localized("Correlation_ErrorMany", 2), cut.Markup));
        Assert.Contains(Localized("Correlation_WarningOne", 1), cut.Markup);
    }

    [Fact]
    public void HighlightsTheSelectedEventRow()
    {
        // The rendered focus handle (0,0) matches this event, so its row is the selected one.
        StubBuild(ViewWithEvent(new EventLocator(_logId, 0, 0)));

        var cut = RenderActive();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-row.selected")));
    }

    [Fact]
    public void MessageSnippet_IsBoundedToTheFirstLineAndKeepsTheFullDescriptionOutOfTheDom()
    {
        string description = "OPERATIONSTART " + new string('x', 300) + "\nSECONDLINEMARKER should not render";
        _detailResolver.TryResolveLean(Arg.Any<EventLocator>(), out Arg.Any<ResolvedEvent?>())
            .Returns(call =>
            {
                call[1] = LeanEventWith(description);

                return true;
            });
        StubBuild(ViewWithEvent(new EventLocator(_logId, 0, 3)));

        var cut = RenderActive();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-row-message")));
        Assert.Contains("OPERATIONSTART", cut.Markup);
        Assert.DoesNotContain("SECONDLINEMARKER", cut.Markup);
        Assert.DoesNotContain(new string('x', 300), cut.Markup);
    }

    [Fact]
    public void ReactivatingAfterACompletedBuild_ReusesItWithoutRebuilding()
    {
        StubBuild(ViewWithEvent(new EventLocator(_logId, 0, 0)));

        var cut = RenderActive();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-timeline")));

        // A completed view survives tab toggles: leaving and returning reuses it rather than rebuilding.
        cut.Render(parameters => parameters.Add(panel => panel.IsActive, false));
        cut.Render(parameters => parameters.Add(panel => panel.IsActive, true));

        _service.Received(1).BuildAsync(Arg.Any<EventLocator>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ReactivatingAfterAFailedBuild_Retries()
    {
        _service.BuildAsync(Arg.Any<EventLocator>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ActivityCorrelationView?>(new InvalidOperationException("boom")));

        var cut = RenderActive();
        cut.WaitForAssertion(() => Assert.Contains(Localized("Correlation_Unavailable"), cut.Markup));

        // A faulted build must not strand the panel: returning to the tab retries.
        cut.Render(parameters => parameters.Add(panel => panel.IsActive, false));
        cut.Render(parameters => parameters.Add(panel => panel.IsActive, true));

        _service.Received(2).BuildAsync(Arg.Any<EventLocator>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ReactivatingAfterAnUnavailableBuild_Retries()
    {
        _service.BuildAsync(Arg.Any<EventLocator>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ActivityCorrelationView?>(null));

        var cut = RenderActive();
        cut.WaitForAssertion(() => Assert.Contains(Localized("Correlation_Unavailable"), cut.Markup));

        // A null (unavailable) result is not a durable build: returning to the tab retries instead of sticking.
        cut.Render(parameters => parameters.Add(panel => panel.IsActive, false));
        cut.Render(parameters => parameters.Add(panel => panel.IsActive, true));

        _service.Received(2).BuildAsync(Arg.Any<EventLocator>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RendersTheFocusTimelineWithMessageSnippetViaLeanResolve()
    {
        StubBuild(ViewWithEvent(new EventLocator(_logId, 0, 3)));

        var cut = RenderActive();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-timeline")));
        Assert.NotEmpty(cut.FindAll(".correlation-row"));
        Assert.Contains("Something went wrong", cut.Markup);
        _detailResolver.Received().TryResolveLean(Arg.Any<EventLocator>(), out Arg.Any<ResolvedEvent?>());
        _detailResolver.DidNotReceive().TryResolve(Arg.Any<EventLocator>(), out Arg.Any<ResolvedEvent?>());
    }

    [Fact]
    public void StaleView_ShowsRefreshAndBlocksEventNavigation()
    {
        StubBuild(ViewWithEvent(new EventLocator(_logId, 0, 3)));

        var cut = RenderActive();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-row-button")));

        _service.TryGetContentToken(_logId, out Arg.Any<CorrelationContentToken>())
            .Returns(call =>
            {
                call[1] = new CorrelationContentToken(_logId, 0, 99, 1);

                return true;
            });

        cut.InvokeAsync(() => _source.Changed += Raise.Event<Action>());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-stale")));
        Assert.True(cut.Find(".correlation-row-button").HasAttribute("disabled"));

        cut.Find(".correlation-row-button").Click();

        _commands.DidNotReceive().SetSelectedEvents(Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>());
    }

    [Fact]
    public void WhenSelectedEventHasNoActivityId_ShowsEmptyStateWithoutBuilding()
    {
        var cut = Render<PanelComponent>(parameters => parameters
            .Add(panel => panel.SelectedEvent, new ResolvedEvent("live", LogPathType.Channel) { Id = 1 })
            .Add(panel => panel.FocusedHandle, new EventLocator(_logId, 0, 0))
            .Add(panel => panel.IsActive, true));

        Assert.Contains(Localized("Correlation_NoActivityId"), cut.Markup);
        _service.DidNotReceive().BuildAsync(Arg.Any<EventLocator>(), Arg.Any<CancellationToken>());
    }

    private static ResolvedEvent FocusEvent() =>
        new("live", LogPathType.Channel)
        {
            Id = 1,
            ActivityId = s_focus,
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static ResolvedEvent LeanEvent() =>
        new("live", LogPathType.Channel)
        {
            Id = 4624,
            Level = "Error",
            Source = "TestSource",
            Description = "Something went wrong in the operation.",
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static ResolvedEvent LeanEventWith(string description) =>
        new("live", LogPathType.Channel)
        {
            Id = 4624,
            Level = "Error",
            Source = "TestSource",
            Description = description,
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private CorrelationContentToken FreshToken() => new(_logId, 0, 1, 1);

    private string Localized(string key) => Services.GetRequiredService<IStringLocalizer<SharedResource>>()[key].Value;

    private string Localized(string key, params object[] arguments) => Services.GetRequiredService<IStringLocalizer<SharedResource>>()[key, arguments].Value;

    private IRenderedComponent<PanelComponent> RenderActive() =>
        Render<PanelComponent>(parameters => parameters
            .Add(panel => panel.SelectedEvent, FocusEvent())
            .Add(panel => panel.FocusedHandle, new EventLocator(_logId, 0, 0))
            .Add(panel => panel.IsActive, true));

    private void StubBuild(ActivityCorrelationView view) =>
        _service.BuildAsync(Arg.Any<EventLocator>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ActivityCorrelationView?>(view));

    private ActivityCorrelationView ViewWithEvent(EventLocator eventLocator, int errorCount = 0, int warningCount = 0)
    {
        long ticks = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        var correlatedEvent = new CorrelatedEvent(eventLocator, ticks);

        var focus = new ActivityNode
        {
            ActivityId = s_focus,
            Role = ActivityNodeRole.Focus,
            EventCount = 1,
            MinTicks = ticks,
            MaxTicks = ticks,
            IsSharedOversized = false,
            Parents = [],
            CriticalCount = 0,
            ErrorCount = errorCount,
            WarningCount = warningCount,
            Events = [correlatedEvent],
            EventsTruncated = false
        };

        return new ActivityCorrelationView
        {
            LogId = _logId,
            FocusActivityId = s_focus,
            Token = FreshToken(),
            Activities = [focus],
            HasHierarchy = false
        };
    }
}
