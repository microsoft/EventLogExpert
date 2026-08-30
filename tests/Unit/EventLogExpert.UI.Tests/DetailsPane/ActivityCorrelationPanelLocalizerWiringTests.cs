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
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using NSubstitute;
using PanelComponent = EventLogExpert.UI.DetailsPane.ActivityCorrelationPanel;

namespace EventLogExpert.UI.Tests.DetailsPane;

public sealed class ActivityCorrelationPanelLocalizerWiringTests : BunitContext
{
    private static readonly Guid s_child = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_focus = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IEventDetailResolver _detailResolver = Substitute.For<IEventDetailResolver>();
    private readonly EventLogId _logId = EventLogId.Create();
    private readonly IActivityCorrelationService _service = Substitute.For<IActivityCorrelationService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    public ActivityCorrelationPanelLocalizerWiringTests()
    {
        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
        _service.TryGetContentToken(_logId, out Arg.Any<CorrelationContentToken>())
            .Returns(call =>
            {
                call[1] = FreshToken();

                return true;
            });
        _detailResolver.TryResolveLean(Arg.Any<EventLocator>(), out Arg.Any<ResolvedEvent?>())
            .Returns(call =>
            {
                call[1] = new ResolvedEvent("live", LogPathType.Channel) { Id = 7, Source = "Provider", Description = string.Empty };

                return true;
            });

        Services.AddSingleton(_service);
        Services.AddSingleton(Substitute.For<IActivityCorrelationSource>());
        Services.AddSingleton(_detailResolver);
        Services.AddSingleton(Substitute.For<IEventLogCommands>());
        Services.AddSingleton(Substitute.For<IFilterLensCommands>());
        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<ITraceLogger>());
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
    }

    [Fact]
    public void EmptyState_DoesNotLeakResourceKeysWithRealLocalizer()
    {
        Services.RemoveAll<IStringLocalizer<SharedResource>>();
        Services.AddEventLogLocalization();

        var cut = Render<PanelComponent>(parameters => parameters
            .Add(panel => panel.SelectedEvent, new ResolvedEvent("live", LogPathType.Channel) { Id = 1 })
            .Add(panel => panel.FocusedHandle, new EventLocator(_logId, 0, 0))
            .Add(panel => panel.IsActive, true));

        Assert.DoesNotContain("Correlation_", cut.Markup);
    }

    [Fact]
    public void RenderedTextAndBadges_AreDrivenByTheLocalizer()
    {
        _service.BuildAsync(Arg.Any<EventLocator>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ActivityCorrelationView?>(ViewWithRelatedActivity()));

        var cut = RenderActive();
        cut.WaitForAssertion(() => Assert.Contains("[[Correlation_EventOne(1)]]", cut.Markup));
        cut.Find(".correlation-chip-head").Click();

        Assert.Contains("[[Correlation_NoMessage]]", cut.Markup);
        Assert.Contains("[[Correlation_ErrorMany(2)]]", cut.Markup);
        Assert.Contains("[[Correlation_WarningOne(1)]]", cut.Markup);
        Assert.Contains("[[Correlation_FilterButton]]", cut.Markup);
        Assert.Contains("[[Correlation_Role_Child]]", cut.Markup);
        Assert.Contains("[[Correlation_CycleBadge]]", cut.Markup);
        Assert.Contains("[[Correlation_MultipleParentsBadge]]", cut.Markup);
        Assert.Contains("[[Correlation_SharedActivityBadge]]", cut.Markup);
        Assert.Contains("[[Correlation_NoEvents]]", cut.Markup);
    }

    private static ResolvedEvent FocusEvent() =>
        new("live", LogPathType.Channel)
        {
            Id = 1,
            ActivityId = s_focus,
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private CorrelationContentToken FreshToken() => new(_logId, 0, 1, 1);

    private IRenderedComponent<PanelComponent> RenderActive() =>
        Render<PanelComponent>(parameters => parameters
            .Add(panel => panel.SelectedEvent, FocusEvent())
            .Add(panel => panel.FocusedHandle, new EventLocator(_logId, 0, 0))
            .Add(panel => panel.IsActive, true));

    private ActivityCorrelationView ViewWithRelatedActivity()
    {
        long ticks = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        ActivityNode focus = new()
        {
            ActivityId = s_focus,
            Role = ActivityNodeRole.Focus,
            EventCount = 1,
            MinTicks = ticks,
            MaxTicks = ticks,
            IsSharedOversized = true,
            ErrorCount = 2,
            WarningCount = 1,
            Events = [new CorrelatedEvent(new EventLocator(_logId, 0, 0), ticks)]
        };
        ActivityNode child = new()
        {
            ActivityId = s_child,
            Role = ActivityNodeRole.Child,
            EventCount = 0,
            MinTicks = 0,
            MaxTicks = 0,
            IsSharedOversized = true,
            Parents = [Guid.NewGuid(), Guid.NewGuid()],
            IsCycle = true,
            Events = []
        };

        return new ActivityCorrelationView
        {
            LogId = _logId,
            FocusActivityId = s_focus,
            Token = FreshToken(),
            Activities = [focus, child],
            HasHierarchy = true
        };
    }
}
