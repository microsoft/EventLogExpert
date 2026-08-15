// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.LogTable;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class XmlFilterGateTests
{
    [Fact]
    public void BuildSurvivorPredicate_ForFileNotLoadedWithXml_EvaluatesTheMatchBitset()
    {
        Filter filter = XmlFilter();
        EventLogId logId = EventLogId.Create();
        XmlFilterMatchCache match = MatchWith(filter, logId, new XmlFilterMatch(logId, 0, 0, 2, [true, false]));

        Func<IEventColumnReader, EventLocator, bool> predicate =
            XmlFilterGate.BuildSurvivorPredicate(filter, new EventLogConcurrencyState(), match);
        IEventColumnReader reader = Substitute.For<IEventColumnReader>();

        Assert.True(predicate(reader, new EventLocator(logId, Generation: 0, Index: 0)));
        Assert.False(predicate(reader, new EventLocator(logId, Generation: 0, Index: 1)));
        Assert.False(predicate(reader, new EventLocator(logId, Generation: 0, Index: 2)));
    }

    [Fact]
    public void BuildSurvivorPredicate_ForFileNotLoadedWithXml_WithoutMatch_ExcludesTheRow()
    {
        Filter filter = XmlFilter();
        EventLogId logId = EventLogId.Create();

        Func<IEventColumnReader, EventLocator, bool> predicate =
            XmlFilterGate.BuildSurvivorPredicate(filter, new EventLogConcurrencyState(), new XmlFilterMatchCache());

        Assert.False(predicate(Substitute.For<IEventColumnReader>(), new EventLocator(logId, Generation: 0, Index: 0)));
    }

    [Fact]
    public void IsDeferred_WhenChannelLogIsLoadedWithXml_ReturnsFalse()
    {
        Filter filter = XmlFilter();
        EventLogId logId = EventLogId.Create();
        EventLogConcurrencyState concurrency = new();
        concurrency.MarkLoadedWithXml(logId);

        bool deferred = XmlFilterGate.IsDeferred(
            filter, StateWith(filter, (logId, "A", LogPathType.Channel)), StoreWith(), concurrency, new XmlFilterMatchCache());

        Assert.False(deferred);
    }

    [Fact]
    public void IsDeferred_WhenChannelLogNotLoadedWithXml_ReturnsTrue()
    {
        Filter filter = XmlFilter();
        EventLogId logId = EventLogId.Create();

        bool deferred = XmlFilterGate.IsDeferred(
            filter, StateWith(filter, (logId, "A", LogPathType.Channel)), StoreWith(), new EventLogConcurrencyState(), new XmlFilterMatchCache());

        Assert.True(deferred);
    }

    [Fact]
    public void IsDeferred_WhenFileLogHasNoMatch_ReturnsTrue()
    {
        Filter filter = XmlFilter();
        EventLogId logId = EventLogId.Create();

        bool deferred = XmlFilterGate.IsDeferred(
            filter,
            StateWith(filter, (logId, "A", LogPathType.File)),
            StoreWith((logId, EventColumnStore.Build([], generation: 0, contentVersion: 0))),
            new EventLogConcurrencyState(),
            new XmlFilterMatchCache());

        Assert.True(deferred);
    }

    [Fact]
    public void IsDeferred_WhenFileLogIsLoadedWithXml_ReturnsFalse()
    {
        Filter filter = XmlFilter();
        EventLogId logId = EventLogId.Create();
        EventLogConcurrencyState concurrency = new();
        concurrency.MarkLoadedWithXml(logId);

        bool deferred = XmlFilterGate.IsDeferred(
            filter,
            StateWith(filter, (logId, "A", LogPathType.File)),
            StoreWith((logId, EventColumnStore.Build([], generation: 0, contentVersion: 0))),
            concurrency,
            new XmlFilterMatchCache());

        Assert.False(deferred);
    }

    [Fact]
    public void IsDeferred_WhenFileLogMatchMatchesStoreSnapshot_ReturnsFalse()
    {
        Filter filter = XmlFilter();
        EventLogId logId = EventLogId.Create();
        XmlFilterMatchCache match = MatchWith(filter, logId, new XmlFilterMatch(logId, 0, 0, 0, []));

        bool deferred = XmlFilterGate.IsDeferred(
            filter,
            StateWith(filter, (logId, "A", LogPathType.File)),
            StoreWith((logId, EventColumnStore.Build([], generation: 0, contentVersion: 0))),
            new EventLogConcurrencyState(),
            match);

        Assert.False(deferred);
    }

    [Fact]
    public void IsDeferred_WhenFileLogMatchStampIsStaleAfterAppend_ReturnsTrue()
    {
        Filter filter = XmlFilter();
        EventLogId logId = EventLogId.Create();
        XmlFilterMatchCache match = MatchWith(filter, logId, new XmlFilterMatch(logId, 0, contentVersion: 0, 0, []));

        bool deferred = XmlFilterGate.IsDeferred(
            filter,
            StateWith(filter, (logId, "A", LogPathType.File)),
            StoreWith((logId, EventColumnStore.Build([], generation: 0, contentVersion: 1))),
            new EventLogConcurrencyState(),
            match);

        Assert.True(deferred);
    }

    [Fact]
    public void IsDeferred_WhenFileReadyAndChannelLoaded_ReturnsFalse()
    {
        Filter filter = XmlFilter();
        EventLogId fileId = EventLogId.Create();
        EventLogId channelId = EventLogId.Create();
        EventLogConcurrencyState concurrency = new();
        concurrency.MarkLoadedWithXml(channelId);
        XmlFilterMatchCache match = MatchWith(filter, fileId, new XmlFilterMatch(fileId, 0, 0, 0, []));

        bool deferred = XmlFilterGate.IsDeferred(
            filter,
            StateWith(filter, (fileId, "A", LogPathType.File), (channelId, "B", LogPathType.Channel)),
            StoreWith((fileId, EventColumnStore.Build([], generation: 0, contentVersion: 0))),
            concurrency,
            match);

        Assert.False(deferred);
    }

    [Fact]
    public void IsDeferred_WhenFileReadyButChannelNotLoaded_ReturnsTrue()
    {
        Filter filter = XmlFilter();
        EventLogId fileId = EventLogId.Create();
        EventLogId channelId = EventLogId.Create();
        XmlFilterMatchCache match = MatchWith(filter, fileId, new XmlFilterMatch(fileId, 0, 0, 0, []));

        bool deferred = XmlFilterGate.IsDeferred(
            filter,
            StateWith(filter, (fileId, "A", LogPathType.File), (channelId, "B", LogPathType.Channel)),
            StoreWith((fileId, EventColumnStore.Build([], generation: 0, contentVersion: 0))),
            new EventLogConcurrencyState(),
            match);

        Assert.True(deferred);
    }

    [Fact]
    public void IsDeferred_WhenFilterDoesNotRequireXml_ReturnsFalse()
    {
        Filter filter = NonXmlFilter();
        EventLogId logId = EventLogId.Create();

        bool deferred = XmlFilterGate.IsDeferred(
            filter, StateWith(filter, (logId, "A", LogPathType.File)), StoreWith(), new EventLogConcurrencyState(), new XmlFilterMatchCache());

        Assert.False(deferred);
    }

    [Fact]
    public void IsDeferred_WhenMatchIsForADifferentFilter_ReturnsTrue()
    {
        Filter filter = XmlFilter("needle");
        EventLogId logId = EventLogId.Create();
        XmlFilterMatchCache match = MatchWith(XmlFilter("other"), logId, new XmlFilterMatch(logId, 0, 0, 0, []));

        bool deferred = XmlFilterGate.IsDeferred(
            filter,
            StateWith(filter, (logId, "A", LogPathType.File)),
            StoreWith((logId, EventColumnStore.Build([], generation: 0, contentVersion: 0))),
            new EventLogConcurrencyState(),
            match);

        Assert.True(deferred);
    }

    [Fact]
    public void IsDeferred_WhenNoLogsAreOpen_ReturnsFalse()
    {
        Filter filter = XmlFilter();

        bool deferred = XmlFilterGate.IsDeferred(
            filter, StateWith(filter), StoreWith(), new EventLogConcurrencyState(), new XmlFilterMatchCache());

        Assert.False(deferred);
    }

    private static XmlFilterMatchCache MatchWith(Filter filter, EventLogId logId, XmlFilterMatch match)
    {
        XmlFilterMatchCache state = new();
        state.Set(filter, new Dictionary<EventLogId, XmlFilterMatch> { [logId] = match }, state.NextSequence());

        return state;
    }

    private static Filter NonXmlFilter() =>
        new(null, [SavedFilter.TryCreate("Level == \"Error\"") ?? throw new InvalidOperationException()]);

    private static EventLogState StateWith(Filter filter, params (EventLogId Id, string Name, LogPathType Type)[] logs)
    {
        ImmutableDictionary<string, OpenLogInfo>.Builder builder =
            ImmutableDictionary.CreateBuilder<string, OpenLogInfo>(StringComparer.Ordinal);

        foreach ((EventLogId id, string name, LogPathType type) in logs) { builder[name] = new OpenLogInfo(id, type); }

        return new EventLogState { OpenLogs = builder.ToImmutable(), AppliedFilter = filter };
    }

    private static RawEventStoreState StoreWith(params (EventLogId Id, EventColumnStore Store)[] stores)
    {
        ImmutableDictionary<EventLogId, EventColumnStore>.Builder builder =
            ImmutableDictionary.CreateBuilder<EventLogId, EventColumnStore>();

        foreach ((EventLogId id, EventColumnStore store) in stores) { builder[id] = store; }

        return new RawEventStoreState { ByLog = builder.ToImmutable() };
    }

    private static Filter XmlFilter(string needle = "x") =>
        new(null, [SavedFilter.TryCreate($"Xml.Contains(\"{needle}\")") ?? throw new InvalidOperationException()]);
}
