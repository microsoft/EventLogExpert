// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using AngleSharp.Dom;
using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using DetailsPaneComponent = EventLogExpert.UI.DetailsPane.DetailsPane;

namespace EventLogExpert.UI.Tests.DetailsPane;

public sealed class DetailsPaneTests : BunitContext
{
    private readonly IStateSelection<LogTableState, EventLogId?> _activeLog = Substitute.For<IStateSelection<LogTableState, EventLogId?>>();
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();
    private readonly IFilterLensCommands _filterLensCommands = Substitute.For<IFilterLensCommands>();
    private readonly IStateSelection<EventLogState, SelectionEntry?> _focus = Substitute.For<IStateSelection<EventLogState, SelectionEntry?>>();
    private readonly EventLogId _logId = EventLogId.Create();
    private readonly IState<LogTableState> _logTableState = Substitute.For<IState<LogTableState>>();
    private readonly IDetailsPanePreferencesProvider _preferences = Substitute.For<IDetailsPanePreferencesProvider>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ITraceLogger _traceLogger = Substitute.For<ITraceLogger>();
    private readonly IEventXmlResolver _xmlResolver = Substitute.For<IEventXmlResolver>();

    public DetailsPaneTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/DetailsPane/DetailsPane.razor.js");

        _settings.TimeZoneInfo.Returns(TimeZoneInfo.Utc);
        _focus.Value.Returns((SelectionEntry?)null);

        Services.AddSingleton(_activeLog);
        Services.AddSingleton(_clipboard);
        Services.AddSingleton(_filterLensCommands);
        Services.AddSingleton(_focus);
        Services.AddSingleton(_logTableState);
        Services.AddSingleton(_preferences);
        Services.AddSingleton(_settings);
        Services.AddSingleton(_traceLogger);
        Services.AddSingleton(_xmlResolver);
        Services.AddFluxor(options => options.ScanAssemblies(typeof(DetailsPaneComponent).Assembly));
    }

    [Fact]
    public void CollapseToggle_ControlsTheDetailsBody()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        Assert.Equal("details-body", cut.Find("#details-header").GetAttribute("aria-controls"));
        Assert.NotNull(cut.Find("#details-body"));
    }

    [Fact]
    public void CollapsedPane_HidesTabsAndCopyButKeepsToggleAndLabel()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        cut.Find("#details-header").Click();

        Assert.NotNull(cut.Find("#details-header"));
        Assert.Empty(cut.FindAll(".details-tabs"));
        Assert.Empty(cut.FindAll(".details-copy-event"));
        Assert.Equal("Details", cut.Find(".details-headerbar-label").TextContent.Trim());
    }

    [Fact]
    public void CollapsedPane_ReopensOnNextSelection_WhenPreferenceOn()
    {
        _preferences.DisplayPaneSelectionPreference.Returns(true);
        var @event = EventWithData(("LogonType", 3));
        var cut = SelectAndRender(@event);

        cut.Find("#details-header").Click();
        Assert.Equal("false", cut.Find(".details-pane").GetAttribute("data-toggle"));

        RaiseFocusChanged(@event);

        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find(".details-pane").GetAttribute("data-toggle")));
    }

    [Fact]
    public void CollapsedPane_StaysCollapsedOnNextSelection_WhenPreferenceOff()
    {
        _preferences.DisplayPaneSelectionPreference.Returns(false);
        var @event = EventWithData(("LogonType", 3));
        var cut = SelectAndRender(@event);

        // User collapses the pane, then a later selection arrives: it must respect the collapse.
        cut.Find("#details-header").Click();
        Assert.Equal("false", cut.Find(".details-pane").GetAttribute("data-toggle"));

        RaiseFocusChanged(@event);

        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find(".details-pane").GetAttribute("data-toggle")));
    }

    [Fact]
    public void CopyEventButton_HasVisibleLabel()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        Assert.Contains("Copy event", cut.Find(".details-copy-event").TextContent);
    }

    [Fact]
    public void CopyEventButton_InvokesClipboardWithEventText()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        cut.Find(".details-copy-event").Click();

        _clipboard.Received(1).CopyTextAsync(Arg.Is<string>(text => text != null && text.Contains("LogonType: 3 (Network)")));
    }

    [Fact]
    public void CopyEvent_IsNotAChildOfTheTablist()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        Assert.Empty(cut.Find("[role=tablist]").QuerySelectorAll(".details-copy-event"));
    }

    [Fact]
    public void CorrelationButtons_DispatchLensCommandsForSelectedEventAndOwningLog()
    {
        var activityId = Guid.NewGuid();
        var relatedActivityId = Guid.NewGuid();
        ResolvedEvent @event = BaseEvent() with { ActivityId = activityId, RelatedActivityId = relatedActivityId };

        var cut = SelectAndRender(@event);

        Assert.Equal(2, cut.FindAll(".details-correlation-action").Count);

        // Re-issue FindAll before each click: the first click re-renders, which invalidates handler ids captured earlier.
        cut.FindAll(".details-correlation-action")[0].Click();
        cut.FindAll(".details-correlation-action")[1].Click();

        _filterLensCommands.Received(1).ShowRelatedByActivityId(activityId, "Application");
        _filterLensCommands.Received(1).ShowParentActivity(relatedActivityId, "Application");
    }

    [Fact]
    public void CorrelationSection_HiddenWhenNoActivityIds()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        Assert.Empty(cut.FindAll(".details-correlation"));
    }

    [Fact]
    public void CorrelationSection_ShownForGuidEmptyActivityId_MirroringContextMenu()
    {
        // The log-table context menu enables its lens items on Guid?.HasValue (so a zero-Guid still enables); the pane
        // mirrors that, keeping the two surfaces consistent.
        ResolvedEvent @event = BaseEvent() with { ActivityId = Guid.Empty };

        var cut = SelectAndRender(@event);
        var button = Assert.Single(cut.FindAll(".details-correlation-action"));

        Assert.Contains("Show related events", button.TextContent);
    }

    [Fact]
    public void CorrelationSection_ShowsOnlyParentButton_WhenOnlyRelatedActivityIdPresent()
    {
        ResolvedEvent @event = BaseEvent() with { RelatedActivityId = Guid.NewGuid() };

        var cut = SelectAndRender(@event);
        var button = Assert.Single(cut.FindAll(".details-correlation-action"));

        Assert.Contains("Show parent activity", button.TextContent);
    }

    [Fact]
    public void CorrelationSection_ShowsOnlyRelatedButton_WhenOnlyActivityIdPresent()
    {
        ResolvedEvent @event = BaseEvent() with { ActivityId = Guid.NewGuid() };

        var cut = SelectAndRender(@event);
        var button = Assert.Single(cut.FindAll(".details-correlation-action"));

        Assert.Contains("Show related events", button.TextContent);
    }

    [Fact]
    public void ExpandedPane_RenamesReaderTabToDetails_AndDropsStandaloneLabel()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        Assert.Equal("Details", cut.FindAll(".details-tab")[0].TextContent.Trim());
        Assert.Empty(cut.FindAll(".details-headerbar-label"));
    }

    [Fact]
    public void LegacyEvent_ShowsNoNamedFieldsFallback()
    {
        var cut = SelectAndRender(BaseEvent());

        Assert.Contains("no named data fields", cut.Markup);
        Assert.Empty(cut.FindAll(".details-field"));
    }

    [Fact]
    public void NoSelection_PaneIsHiddenUntilAnEventIsClicked()
    {
        var cut = Render<DetailsPaneComponent>();

        Assert.True(cut.Find(".details-pane").HasAttribute("hidden"));
        Assert.Empty(cut.FindAll(".details-tabs"));
    }

    [Fact]
    public void OpenPane_CollapsesViaHeaderToggle()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        cut.Find("#details-header").Click();

        Assert.Equal("false", cut.Find(".details-pane").GetAttribute("data-toggle"));
        Assert.False(cut.Find(".details-pane").HasAttribute("hidden"));
    }

    [Fact]
    public void PerFieldCopy_UsesTheClickedFieldsValue()
    {
        var cut = SelectAndRender(EventWithData(("SubjectUserName", "SYSTEM"), ("TargetUserName", "ADMIN")));

        cut.FindAll(".details-copy")[0].Click();
        cut.FindAll(".details-copy")[1].Click();

        // A shared-variable closure capture would make every field's button emit the last field's value, so asserting
        // each row copies its OWN value discriminates that regression.
        _clipboard.Received(1).CopyTextAsync("SYSTEM");
        _clipboard.Received(1).CopyTextAsync("ADMIN");
    }

    [Fact]
    public void SelectedEvent_DefaultsToReaderTab()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        Assert.Equal("true", ReaderTab(cut).GetAttribute("aria-selected"));
        Assert.NotNull(cut.Find(".details-reader"));
        Assert.Contains("Event ID", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".details-field"));
        Assert.Contains("Network", cut.Markup);
    }

    [Fact]
    public void SelectingEvent_OpensThePane()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        Assert.False(cut.Find(".details-pane").HasAttribute("hidden"));
        Assert.Equal("true", cut.Find(".details-pane").GetAttribute("data-toggle"));
    }

    [Fact]
    public void SeverityIcon_AbsentForUnknownLevel()
    {
        var cut = SelectAndRender(BaseEvent() with { Level = "Audit Success" });

        Assert.Empty(cut.FindAll(".details-summary-level .level-icon"));
    }

    [Theory]
    [InlineData("Critical", "bi-exclamation-octagon-fill")]
    [InlineData("Error", "bi-exclamation-circle")]
    [InlineData("Warning", "bi-exclamation-triangle")]
    [InlineData("Information", "bi-info-circle")]
    [InlineData("Verbose", "bi-circle")]
    public void SeverityIcon_ShownWithDistinctShapeForKnownLevels(string level, string expectedGlyph)
    {
        var cut = SelectAndRender(BaseEvent() with { Level = level });

        var icon = cut.Find(".details-summary-level .level-icon");
        Assert.Contains(expectedGlyph, icon.GetAttribute("class"));
    }

    [Fact]
    public void ShowMoreToggle_ExposesExpandedStateToAssistiveTech()
    {
        // A 600-char scalar exceeds the preview length, so the field renders the Show more / Show less toggle.
        var cut = SelectAndRender(EventWithData(("CommandLine", new string('a', 600))));

        IElement toggle = cut.Find(".details-show-more");
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));

        toggle.Click();

        Assert.Equal("true", cut.Find(".details-show-more").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void SwitchingToXmlTab_ShowsXmlAndDeselectsReader()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        XmlTab(cut).Click();

        Assert.Equal("true", XmlTab(cut).GetAttribute("aria-selected"));
        Assert.Equal("false", ReaderTab(cut).GetAttribute("aria-selected"));
        Assert.True(cut.Find("#details-tabpanel-reader").HasAttribute("hidden"));
        Assert.False(cut.Find("#details-tabpanel-xml").HasAttribute("hidden"));
    }

    [Fact]
    public void TabResetsToReader_WhenActiveLogChanges()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));
        XmlTab(cut).Click();

        _activeLog.SelectedValueChanged += Raise.Event<EventHandler<EventLogId?>>(_activeLog, (EventLogId?)EventLogId.Create());

        cut.WaitForAssertion(() => Assert.Equal("true", ReaderTab(cut).GetAttribute("aria-selected")));
    }

    [Fact]
    public void Tabs_AreLinkedToTheirPanelsViaAria()
    {
        var cut = SelectAndRender(EventWithData(("LogonType", 3)));

        // Both tabpanels stay mounted (only hidden toggles), so every tab's aria-controls resolves to a panel that is
        // actually in the DOM, and each panel points back at its controlling tab.
        Assert.Equal("details-tabpanel-reader", cut.Find("#details-tab-reader").GetAttribute("aria-controls"));
        Assert.Equal("details-tab-reader", cut.Find("#details-tabpanel-reader").GetAttribute("aria-labelledby"));
        Assert.Equal("details-tabpanel-xml", cut.Find("#details-tab-xml").GetAttribute("aria-controls"));
        Assert.Equal("details-tab-xml", cut.Find("#details-tabpanel-xml").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void UserDataIncomplete_ShowsWarning()
    {
        ResolvedEvent @event = BaseEvent() with
        {
            UserData = [new UserDataField("Config/Setting", ["v1"], false)],
            UserDataIncomplete = true
        };

        var cut = SelectAndRender(@event);

        Assert.NotEmpty(cut.FindAll(".details-warn"));
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

    private static ResolvedEvent EventWithData(params (string Name, object? Value)[] fields) =>
        BaseEvent().WithEventData(fields);

    private static IElement ReaderTab(IRenderedComponent<DetailsPaneComponent> cut) => cut.FindAll(".details-tab")[0];

    private static IElement XmlTab(IRenderedComponent<DetailsPaneComponent> cut) => cut.FindAll(".details-tab")[1];

    private void RaiseFocusChanged(ResolvedEvent @event)
    {
        var handle = new EventLocator(_logId, 0, 0);
        ValueKey.TryCreate(@event, out var reloadKey);
        _focus.SelectedValueChanged += Raise.Event<EventHandler<SelectionEntry?>>(_focus, new SelectionEntry(handle, handle, reloadKey));
    }

    private IRenderedComponent<DetailsPaneComponent> SelectAndRender(ResolvedEvent @event)
    {
        _logTableState.Value.Returns(new LogTableState { ActiveEventLogId = _logId }.WithLogEvents(_logId, @event));

        var handle = new EventLocator(_logId, 0, 0);
        ValueKey.TryCreate(@event, out var reloadKey);
        _focus.Value.Returns(new SelectionEntry(handle, handle, reloadKey));

        return Render<DetailsPaneComponent>();
    }
}
