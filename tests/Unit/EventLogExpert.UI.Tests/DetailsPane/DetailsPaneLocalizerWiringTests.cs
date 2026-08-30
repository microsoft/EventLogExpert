// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.ActivityCorrelation;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using DetailsPaneComponent = EventLogExpert.UI.DetailsPane.DetailsPane;

namespace EventLogExpert.UI.Tests.DetailsPane;

public sealed class DetailsPaneLocalizerWiringTests : BunitContext
{
    private readonly IActiveEventLogSource _activeEventLog = Substitute.For<IActiveEventLogSource>();
    private readonly IEventDetailResolver _detailResolver = Substitute.For<IEventDetailResolver>();
    private readonly IEventFocusSource _eventFocus = Substitute.For<IEventFocusSource>();
    private readonly EventLogId _logId = EventLogId.Create();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    public DetailsPaneLocalizerWiringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/DetailsPane/DetailsPane.razor.js");

        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
        Services.AddSingleton(_activeEventLog);
        Services.AddSingleton(Substitute.For<IClipboardService>());
        Services.AddSingleton(Substitute.For<IFilterLensCommands>());
        Services.AddSingleton(_eventFocus);
        Services.AddSingleton(_detailResolver);
        Services.AddSingleton(Substitute.For<IDetailsPanePreferencesProvider>());
        Services.AddSingleton(_settings);
        Services.AddSingleton(Substitute.For<ITraceLogger>());
        Services.AddSingleton(Substitute.For<IEventXmlResolver>());
        Services.AddSingleton(Substitute.For<IActivityCorrelationService>());
        Services.AddSingleton(Substitute.For<IActivityCorrelationSource>());
        Services.AddSingleton(Substitute.For<IEventLogCommands>());
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
    }

    [Fact]
    public void DetailsChromeLabelsAndExplanations_AreDrivenByTheLocalizer()
    {
        ResolvedEvent @event = BaseEvent().WithEventData(("LogonType", 3), ("SubjectUserName", string.Empty));

        var cut = SelectAndRender(@event);

        Assert.Contains("[[Details_TabLabel]]", cut.Markup);
        Assert.Contains("[[Details_CopyEventButtonLabel]]", cut.Markup);
        Assert.Contains("[[Details_EventId(4624)]]", cut.Markup);
        Assert.Contains("[[Details_Property_Source]]", cut.Markup);
        Assert.Contains("[[Details_Placeholder_Empty]]", cut.Markup);
        Assert.Contains("[[Explain_LogonType]]", cut.Markup);
        Assert.Contains("[[Details_CopyValueAria(LogonType)]]", cut.Markup);
        Assert.DoesNotContain("(empty)", cut.Markup);
    }

    [Fact]
    public void ResolutionStatusRow_LabelAndValue_AreDrivenByTheLocalizer()
    {
        // The <dd> shows ResolutionStatusLocalizer.Display(status) when StatusValue is set. Under the marker localizer
        // both the label key and the status-value key surface, proving the value is routed through the localizer - the
        // neutral localizer cannot prove this because Display and ResolutionStatusTokens.Format return the same string.
        ResolvedEvent @event = BaseEvent() with { ResolutionStatus = EventResolutionStatus.NoProvider };

        var cut = SelectAndRender(@event);

        Assert.Contains("[[Details_Property_ResolutionStatus]]", cut.Markup);
        Assert.Contains("[[ResolutionStatus_NoProvider]]", cut.Markup);
    }

    private static ResolvedEvent BaseEvent() =>
        new("Application", LogPathType.Channel)
        {
            Id = 4624,
            RecordId = 1,
            Level = "Information",
            Source = "Microsoft-Windows-Security-Auditing",
            TimeCreated = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Description = "A logon occurred.",
            Xml = "<Event/>"
        };

    private IRenderedComponent<DetailsPaneComponent> SelectAndRender(ResolvedEvent @event)
    {
        var handle = new EventLocator(_logId, 0, 0);
        ValueKey.TryCreate(@event, out var reloadKey);
        _detailResolver.TryResolve(handle, out Arg.Any<ResolvedEvent?>())
            .Returns(call =>
            {
                call[1] = @event;

                return true;
            });
        _eventFocus.Current.Returns(new SelectionEntry(handle, handle, reloadKey));

        return Render<DetailsPaneComponent>();
    }
}
