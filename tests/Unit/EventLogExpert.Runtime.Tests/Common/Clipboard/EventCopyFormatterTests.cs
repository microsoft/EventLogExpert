// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using NSubstitute;
using System.Collections.Immutable;
using System.Security.Principal;

namespace EventLogExpert.Runtime.Tests.Common.Clipboard;

public sealed class EventCopyFormatterTests
{
    private static readonly ImmutableDictionary<ColumnName, bool> s_columns = ImmutableDictionary<ColumnName, bool>.Empty
        .Add(ColumnName.Level, true)
        .Add(ColumnName.Source, true)
        .Add(ColumnName.EventId, true);
    private static readonly EventLogId s_logId = EventLogId.Create();

    private static readonly ImmutableList<ColumnName> s_order =
        ImmutableList.Create(ColumnName.Level, ColumnName.Source, ColumnName.EventId);

    [Fact]
    public async Task FormatAsync_DefaultFormat_RendersTheEnabledColumnsThenDescription()
    {
        var locator = new EventLocator(s_logId, 0, 0);

        var detailResolver = Substitute.For<IEventDetailResolver>();
        detailResolver.TryResolve(locator, out Arg.Any<ResolvedEvent?>())
            .Returns(call => { call[1] = Event(1, 4000, "ProviderA", "Alpha"); return true; });

        var formatter = new EventCopyFormatter(detailResolver, Substitute.For<IEventXmlResolver>());

        string result = await formatter.FormatAsync(
            Request([Entry(locator)], focus: null, EventCopyFormat.Default),
            TestContext.Current.CancellationToken);

        Assert.Contains("\"Information\"", result);
        Assert.Contains("\"ProviderA\"", result);
        Assert.Contains("\"4000\"", result);
        Assert.Contains("\"Alpha\"", result);
    }

    [Fact]
    public async Task FormatAsync_DropsSelectedEntriesThatNoLongerResolve()
    {
        var locatorA = new EventLocator(s_logId, 0, 0);
        var locatorB = new EventLocator(s_logId, 0, 1);
        var eventA = Event(recordId: 1, id: 4000, source: "ProviderA", description: "Alpha");

        var detailResolver = Substitute.For<IEventDetailResolver>();
        detailResolver.TryResolve(locatorA, out Arg.Any<ResolvedEvent?>())
            .Returns(call => { call[1] = eventA; return true; });

        var formatter = new EventCopyFormatter(detailResolver, Substitute.For<IEventXmlResolver>());

        string result = await formatter.FormatAsync(
            Request([Entry(locatorA), Entry(locatorB)], focus: null, EventCopyFormat.Simple),
            TestContext.Current.CancellationToken);

        Assert.Contains("ProviderA", result);
        Assert.Contains("Alpha", result);

        Assert.Single(result.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task FormatAsync_EmptySelectionAndNoFocus_ReturnsEmpty()
    {
        var formatter = new EventCopyFormatter(
            Substitute.For<IEventDetailResolver>(),
            Substitute.For<IEventXmlResolver>());

        string result = await formatter.FormatAsync(
            Request([], focus: null, EventCopyFormat.Full),
            TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task FormatAsync_EmptySelection_FallsBackToTheFocusedEntry()
    {
        var focusLocator = new EventLocator(s_logId, 0, 3);

        var detailResolver = Substitute.For<IEventDetailResolver>();
        detailResolver.TryResolve(focusLocator, out Arg.Any<ResolvedEvent?>())
            .Returns(call => { call[1] = Event(7, 6000, "FocusProvider", "FocusedDescription"); return true; });

        var formatter = new EventCopyFormatter(detailResolver, Substitute.For<IEventXmlResolver>());

        string result = await formatter.FormatAsync(
            Request([], Entry(focusLocator), EventCopyFormat.Simple),
            TestContext.Current.CancellationToken);

        Assert.Contains("FocusProvider", result);
        Assert.Contains("FocusedDescription", result);
    }

    [Fact]
    public async Task FormatAsync_FullFormat_IncludesUserSidWhenItDiffersFromResolvedName()
    {
        var locator = new EventLocator(s_logId, 0, 0);
        var @event = new ResolvedEvent("Application", LogPathType.Channel)
        {
            RecordId = 1,
            Id = 4000,
            Level = "Information",
            Source = "ProviderA",
            Description = "Alpha",
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UserDisplayName = @"NT AUTHORITY\SYSTEM",
            UserId = new SecurityIdentifier("S-1-5-18")
        };

        var detailResolver = Substitute.For<IEventDetailResolver>();
        detailResolver.TryResolve(locator, out Arg.Any<ResolvedEvent?>())
            .Returns(call => { call[1] = @event; return true; });

        var formatter = new EventCopyFormatter(detailResolver, Substitute.For<IEventXmlResolver>());

        string result = await formatter.FormatAsync(
            Request([Entry(locator)], focus: null, EventCopyFormat.Full),
            TestContext.Current.CancellationToken);

        Assert.Contains(@"User: NT AUTHORITY\SYSTEM", result);
        Assert.Contains("User SID: S-1-5-18", result);
    }

    [Fact]
    public async Task FormatAsync_FullFormat_OmitsUserSidWhenResolvedNameIsTheRawSid()
    {
        // Raw-SID fallback: the resolved name already IS the SID, so a separate "User SID" line would just duplicate it.
        var locator = new EventLocator(s_logId, 0, 0);
        var @event = new ResolvedEvent("Application", LogPathType.Channel)
        {
            RecordId = 1,
            Id = 4000,
            Level = "Information",
            Source = "ProviderA",
            Description = "Alpha",
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UserDisplayName = "S-1-5-21-1-2-3-4",
            UserId = new SecurityIdentifier("S-1-5-21-1-2-3-4")
        };

        var detailResolver = Substitute.For<IEventDetailResolver>();
        detailResolver.TryResolve(locator, out Arg.Any<ResolvedEvent?>())
            .Returns(call => { call[1] = @event; return true; });

        var formatter = new EventCopyFormatter(detailResolver, Substitute.For<IEventXmlResolver>());

        string result = await formatter.FormatAsync(
            Request([Entry(locator)], focus: null, EventCopyFormat.Full),
            TestContext.Current.CancellationToken);

        Assert.Contains("User: S-1-5-21-1-2-3-4", result);
        Assert.DoesNotContain("User SID:", result);
    }

    [Fact]
    public async Task FormatAsync_MarkdownFormat_BuildsATableWithAHeaderRow()
    {
        var locator = new EventLocator(s_logId, 0, 0);

        var detailResolver = Substitute.For<IEventDetailResolver>();
        detailResolver.TryResolve(locator, out Arg.Any<ResolvedEvent?>())
            .Returns(call => { call[1] = Event(1, 4000, "ProviderA", "Alpha"); return true; });

        var formatter = new EventCopyFormatter(detailResolver, Substitute.For<IEventXmlResolver>());

        string result = await formatter.FormatAsync(
            Request([Entry(locator)], focus: null, EventCopyFormat.Markdown),
            TestContext.Current.CancellationToken);

        Assert.StartsWith("|", result);
        Assert.Contains("Description", result);
        Assert.Contains("---", result);
        Assert.Contains("ProviderA", result);
    }

    [Fact]
    public async Task FormatAsync_MultipleEntriesXml_MapsEachEventToItsOwnResolvedXml()
    {
        var locatorA = new EventLocator(s_logId, 0, 0);
        var locatorB = new EventLocator(s_logId, 0, 1);
        var eventA = Event(1, 4000, "ProviderA", "Alpha");
        var eventB = Event(2, 5000, "ProviderB", "Beta");

        var detailResolver = Substitute.For<IEventDetailResolver>();
        detailResolver.TryResolve(locatorA, out Arg.Any<ResolvedEvent?>()).Returns(call => { call[1] = eventA; return true; });
        detailResolver.TryResolve(locatorB, out Arg.Any<ResolvedEvent?>()).Returns(call => { call[1] = eventB; return true; });

        var xmlResolver = Substitute.For<IEventXmlResolver>();
        xmlResolver.GetXmlAsync(eventA, Arg.Any<CancellationToken>()).Returns(new ValueTask<string>("<Event>AAA</Event>"));
        xmlResolver.GetXmlAsync(eventB, Arg.Any<CancellationToken>()).Returns(new ValueTask<string>("<Event>BBB</Event>"));

        var formatter = new EventCopyFormatter(detailResolver, xmlResolver);

        string result = await formatter.FormatAsync(
            Request([Entry(locatorA), Entry(locatorB)], focus: null, EventCopyFormat.Full),
            TestContext.Current.CancellationToken);

        int providerA = result.IndexOf("ProviderA", StringComparison.Ordinal);
        int aaa = result.IndexOf("AAA", StringComparison.Ordinal);
        int providerB = result.IndexOf("ProviderB", StringComparison.Ordinal);
        int bbb = result.IndexOf("BBB", StringComparison.Ordinal);

        Assert.True(providerA >= 0 && aaa >= 0 && providerB >= 0 && bbb >= 0);
        Assert.True(providerA < aaa && aaa < providerB && providerB < bbb,
            $"expected ProviderA < AAA < ProviderB < BBB, got {providerA}, {aaa}, {providerB}, {bbb}");
    }

    [Fact]
    public async Task FormatAsync_RendersEverySurvivingEntry_WhenAllResolve()
    {
        var locatorA = new EventLocator(s_logId, 0, 0);
        var locatorB = new EventLocator(s_logId, 0, 1);

        var detailResolver = Substitute.For<IEventDetailResolver>();
        detailResolver.TryResolve(locatorA, out Arg.Any<ResolvedEvent?>())
            .Returns(call => { call[1] = Event(1, 4000, "ProviderA", "Alpha"); return true; });
        detailResolver.TryResolve(locatorB, out Arg.Any<ResolvedEvent?>())
            .Returns(call => { call[1] = Event(2, 5000, "ProviderB", "Beta"); return true; });

        var formatter = new EventCopyFormatter(detailResolver, Substitute.For<IEventXmlResolver>());

        string result = await formatter.FormatAsync(
            Request([Entry(locatorA), Entry(locatorB)], focus: null, EventCopyFormat.Simple),
            TestContext.Current.CancellationToken);

        Assert.Contains("Alpha", result);
        Assert.Contains("Beta", result);
        Assert.Equal(2, result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task FormatAsync_XmlFormat_RendersTheResolvedXml()
    {
        var locator = new EventLocator(s_logId, 0, 0);

        var detailResolver = Substitute.For<IEventDetailResolver>();
        detailResolver.TryResolve(locator, out Arg.Any<ResolvedEvent?>())
            .Returns(call => { call[1] = Event(1, 4000, "ProviderA", "Alpha"); return true; });

        var xmlResolver = Substitute.For<IEventXmlResolver>();
        xmlResolver.GetXmlAsync(Arg.Any<ResolvedEvent>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<string>("<Event><Data>payload</Data></Event>"));

        var formatter = new EventCopyFormatter(detailResolver, xmlResolver);

        string result = await formatter.FormatAsync(
            Request([Entry(locator)], focus: null, EventCopyFormat.Xml),
            TestContext.Current.CancellationToken);

        Assert.Contains("payload", result);
    }

    private static SelectionEntry Entry(EventLocator locator) => new(locator, locator, null);

    private static ResolvedEvent Event(long recordId, int id, string source, string description) =>
        new("Application", LogPathType.Channel)
        {
            RecordId = recordId,
            Id = id,
            Level = "Information",
            Source = source,
            Description = description,
            TimeCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static EventCopyRequest Request(
        ImmutableList<SelectionEntry> selection,
        SelectionEntry? focus,
        EventCopyFormat format) =>
        new(selection, focus, s_columns, s_order, format, TimeZoneInfo.Utc);
}
