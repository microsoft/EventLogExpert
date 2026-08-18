// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.ActivityCorrelation;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using Microsoft.Extensions.DependencyInjection;
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
        StubBuild(ViewWithLeaf(new EventLocator(_logId, 0, 3)));

        var cut = RenderActive();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-row-message")));
        Assert.Contains("No message", cut.Markup);
    }

    [Fact]
    public void FilterToActivity_DispatchesTheActivityIdLens()
    {
        StubBuild(ViewWithLeaf(new EventLocator(_logId, 0, 0)));

        var cut = RenderActive();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-filter-action")));

        cut.Find(".correlation-filter-action").Click();

        _lensCommands.Received().ShowRelatedByActivityId(s_focus, Arg.Any<string?>());
    }

    [Fact]
    public void HeaderShowsErrorAndWarningCounts()
    {
        StubBuild(ViewWithLeaf(new EventLocator(_logId, 0, 0), errorCount: 2, warningCount: 1));

        var cut = RenderActive();

        cut.WaitForAssertion(() => Assert.Contains("2 errors", cut.Markup));
        Assert.Contains("1 warning", cut.Markup);
    }

    [Fact]
    public void HighlightsTheSelectedEventRow()
    {
        // The rendered focus handle (0,0) matches this leaf, so its row is the selected one.
        StubBuild(ViewWithLeaf(new EventLocator(_logId, 0, 0)));

        var cut = RenderActive();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-row.selected")));
    }

    [Fact]
    public void LeafClick_SelectsAndRequestsAOneShotReveal()
    {
        var leafLocator = new EventLocator(_logId, 0, 3);
        StubBuild(ViewWithLeaf(leafLocator));

        var cut = RenderActive();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-row-button")));

        cut.Find(".correlation-row-button").Click();

        _commands.Received().SetSelectedEvents(Arg.Any<IReadOnlyCollection<SelectionEntry>>(), Arg.Any<SelectionEntry?>());
        _commands.Received().RequestRevealFocus(leafLocator, false);
    }

    [Fact]
    public void LeafClick_WhenSnapshotChangedBeforeTheAsyncNotification_RevalidatesAndBlocksNavigation()
    {
        StubBuild(ViewWithLeaf(new EventLocator(_logId, 0, 3)));

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
    public void MessageSnippet_IsBoundedToTheFirstLineAndKeepsTheFullDescriptionOutOfTheDom()
    {
        string description = "OPERATIONSTART " + new string('x', 300) + "\nSECONDLINEMARKER should not render";
        _detailResolver.TryResolveLean(Arg.Any<EventLocator>(), out Arg.Any<ResolvedEvent?>())
            .Returns(call =>
            {
                call[1] = LeanEventWith(description);

                return true;
            });
        StubBuild(ViewWithLeaf(new EventLocator(_logId, 0, 3)));

        var cut = RenderActive();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-row-message")));
        Assert.Contains("OPERATIONSTART", cut.Markup);
        Assert.DoesNotContain("SECONDLINEMARKER", cut.Markup);
        Assert.DoesNotContain(new string('x', 300), cut.Markup);
    }

    [Fact]
    public void RendersTheFocusTimelineWithMessageSnippetViaLeanResolve()
    {
        StubBuild(ViewWithLeaf(new EventLocator(_logId, 0, 3)));

        var cut = RenderActive();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".correlation-timeline")));
        Assert.NotEmpty(cut.FindAll(".correlation-row"));
        Assert.Contains("Something went wrong", cut.Markup);
        _detailResolver.Received().TryResolveLean(Arg.Any<EventLocator>(), out Arg.Any<ResolvedEvent?>());
        _detailResolver.DidNotReceive().TryResolve(Arg.Any<EventLocator>(), out Arg.Any<ResolvedEvent?>());
    }

    [Fact]
    public void StaleTree_ShowsRefreshAndBlocksLeafNavigation()
    {
        StubBuild(ViewWithLeaf(new EventLocator(_logId, 0, 3)));

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

        Assert.Contains("no Activity ID", cut.Markup);
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

    private IRenderedComponent<PanelComponent> RenderActive() =>
        Render<PanelComponent>(parameters => parameters
            .Add(panel => panel.SelectedEvent, FocusEvent())
            .Add(panel => panel.FocusedHandle, new EventLocator(_logId, 0, 0))
            .Add(panel => panel.IsActive, true));

    private void StubBuild(ActivityCorrelationView view) =>
        _service.BuildAsync(Arg.Any<EventLocator>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ActivityCorrelationView?>(view));

    private ActivityCorrelationView ViewWithLeaf(EventLocator leafLocator, int errorCount = 0, int warningCount = 0)
    {
        long ticks = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        var leaf = new CorrelationEventLeaf(leafLocator, ticks);

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
            Leaves = [leaf],
            LeavesTruncated = false
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
