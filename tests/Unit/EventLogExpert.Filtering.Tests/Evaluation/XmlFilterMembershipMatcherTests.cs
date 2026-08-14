// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;

namespace EventLogExpert.Filtering.Tests.Evaluation;

public sealed class XmlFilterMembershipMatcherTests
{
    private const string OwningLog = "TestLog";

    private static readonly DateTime s_baseTime = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ComputeMembership_ProducesStampMatchingReaderSnapshot()
    {
        ResolvedEvent[] events = [Event(1), Event(2)];
        (IEventColumnReader reader, EventLogId logId) = BuildReader(events, generation: 3, contentVersion: 7);
        XmlFilterMembershipMatcher matcher = new(EmptyScanner());

        XmlFilterMembership membership =
            matcher.ComputeMembership(reader, IncludeFilter("Xml.Contains(\"x\")"), OwningLog, LogPathType.File, Ct);

        Assert.Equal(logId, membership.LogId);
        Assert.Equal(3, membership.Generation);
        Assert.Equal(7, membership.ContentVersion);
        Assert.Equal(2, membership.Count);
    }

    [Fact]
    public void ComputeMembership_WhenCancellationRequested_Throws()
    {
        ResolvedEvent[] events = [Event(1), Event(2)];
        (IEventColumnReader reader, _) = BuildReader(events);
        XmlFilterMembershipMatcher matcher = new(EmptyScanner());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            matcher.ComputeMembership(reader, IncludeFilter("Xml.Contains(\"x\")"), OwningLog, LogPathType.File, cancellation.Token));
    }

    [Fact]
    public void ComputeMembership_WhenCandidateRecordRolledOut_EvaluatesWithEmptyXmlNotUnconditionalNoMatch()
    {
        // record id 5 rolled out of the log (scanner never yields it); record id 6 is present and matches the exclude.
        ResolvedEvent[] events = [Event(5), Event(6)];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string> { [6] = "<Event>secret</Event>" });
        XmlFilterMembershipMatcher matcher = new(scanner);

        XmlFilterMembership membership =
            matcher.ComputeMembership(reader, ExcludeFilter("Xml.Contains(\"secret\")"), OwningLog, LogPathType.File, Ct);

        // Row 5's XML is unresolved (empty) so the exclude does NOT fire -> the row stays visible (member); an
        // unconditional no-match would have wrongly hidden it. Row 6's rendered XML matches the exclude -> hidden.
        Assert.True(membership.IsMember(reader.LocatorAt(0)));
        Assert.False(membership.IsMember(reader.LocatorAt(1)));
    }

    [Fact]
    public void ComputeMembership_WhenRowHasNoRecordId_EvaluatesWithEmptyXml()
    {
        ResolvedEvent[] events = [FilterEventBuilder.CreateTestEvent(id: 1, recordId: null, level: "Error"), Event(2, level: "Information")];
        (IEventColumnReader reader, _) = BuildReader(events);
        XmlFilterMembershipMatcher matcher = new(EmptyScanner());

        XmlFilterMembership membership =
            matcher.ComputeMembership(reader, ExcludeFilter("Xml.Contains(\"secret\")"), OwningLog, LogPathType.File, Ct);

        // The record-id-less row is evaluated with empty XML: the exclude does not fire, so it remains a member.
        Assert.True(membership.IsMember(reader.LocatorAt(0)));
    }

    [Fact]
    public void ComputeMembership_WithDateFilter_ExcludesOutOfRangeRowsAndSkipsTheirXmlRender()
    {
        ResolvedEvent[] events =
        [
            Event(1, minute: 0), Event(2, minute: 1), Event(3, minute: 2), Event(4, minute: 3), Event(5, minute: 4)
        ];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>keep</Event>", [2] = "<Event>keep</Event>", [3] = "<Event>keep</Event>",
            [4] = "<Event>keep</Event>", [5] = "<Event>keep</Event>"
        });
        XmlFilterMembershipMatcher matcher = new(scanner);
        Filter filter = new(EnabledDate(s_baseTime.AddMinutes(1), s_baseTime.AddMinutes(3)), [Include("Xml.Contains(\"keep\")")]);

        XmlFilterMembership membership = matcher.ComputeMembership(reader, filter, OwningLog, LogPathType.File, Ct);

        Assert.False(membership.IsMember(reader.LocatorAt(0)));
        Assert.True(membership.IsMember(reader.LocatorAt(1)));
        Assert.True(membership.IsMember(reader.LocatorAt(2)));
        Assert.True(membership.IsMember(reader.LocatorAt(3)));
        Assert.False(membership.IsMember(reader.LocatorAt(4)));

        // The two date-excluded rows (record ids 1 and 5) must never have their XML rendered.
        Assert.Equal([2, 3, 4], scanner.RenderedRecordIds.Order());
    }

    [Fact]
    public void ComputeMembership_WithMixedCheapAndXmlFilter_MatchesOnlyRowsSatisfyingBoth()
    {
        ResolvedEvent[] events = [Event(1, level: "Error"), Event(2, level: "Error"), Event(3, level: "Warning")];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>data</Event>", [2] = "<Event>none</Event>", [3] = "<Event>data</Event>"
        });
        XmlFilterMembershipMatcher matcher = new(scanner);

        XmlFilterMembership membership = matcher.ComputeMembership(
            reader, IncludeFilter("Level == \"Error\" && Xml.Contains(\"data\")"), OwningLog, LogPathType.File, Ct);

        Assert.True(membership.IsMember(reader.LocatorAt(0)));   // Error + data
        Assert.False(membership.IsMember(reader.LocatorAt(1)));  // Error but no data
        Assert.False(membership.IsMember(reader.LocatorAt(2)));  // data but Warning
    }

    [Fact]
    public void ComputeMembership_WithNullReaderOrEmptyOwningLog_Throws()
    {
        (IEventColumnReader reader, _) = BuildReader([Event(1)]);
        XmlFilterMembershipMatcher matcher = new(EmptyScanner());
        Filter filter = IncludeFilter("Xml.Contains(\"x\")");

        Assert.Throws<ArgumentNullException>(() =>
            matcher.ComputeMembership(null!, filter, OwningLog, LogPathType.File, Ct));
        Assert.Throws<ArgumentException>(() =>
            matcher.ComputeMembership(reader, filter, string.Empty, LogPathType.File, Ct));
    }

    [Fact]
    public void ComputeMembership_WithXmlExcludeFilter_VetoesRowsWhoseRenderedXmlMatches()
    {
        ResolvedEvent[] events = [Event(1), Event(2)];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>secret</Event>", [2] = "<Event>clean</Event>"
        });
        XmlFilterMembershipMatcher matcher = new(scanner);

        XmlFilterMembership membership =
            matcher.ComputeMembership(reader, ExcludeFilter("Xml.Contains(\"secret\")"), OwningLog, LogPathType.File, Ct);

        Assert.False(membership.IsMember(reader.LocatorAt(0)));
        Assert.True(membership.IsMember(reader.LocatorAt(1)));
    }

    [Fact]
    public void ComputeMembership_WithXmlIncludeFilter_MarksOnlyRowsWhoseRenderedXmlMatches()
    {
        ResolvedEvent[] events = [Event(1), Event(2), Event(3)];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>alpha</Event>", [2] = "<Event>beta</Event>", [3] = "<Event>alpha</Event>"
        });
        XmlFilterMembershipMatcher matcher = new(scanner);

        XmlFilterMembership membership =
            matcher.ComputeMembership(reader, IncludeFilter("Xml.Contains(\"alpha\")"), OwningLog, LogPathType.File, Ct);

        Assert.True(membership.IsMember(reader.LocatorAt(0)));
        Assert.False(membership.IsMember(reader.LocatorAt(1)));
        Assert.True(membership.IsMember(reader.LocatorAt(2)));
    }

    [Fact]
    public void ComputeMembership_WithXmlUnderOr_MatchesRowsViaEitherBranch()
    {
        ResolvedEvent[] events = [Event(1, level: "Error"), Event(2, level: "Information"), Event(3, level: "Information")];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>none</Event>", [2] = "<Event>needle</Event>", [3] = "<Event>none</Event>"
        });
        XmlFilterMembershipMatcher matcher = new(scanner);

        XmlFilterMembership membership = matcher.ComputeMembership(
            reader, IncludeFilter("Level == \"Error\" || Xml.Contains(\"needle\")"), OwningLog, LogPathType.File, Ct);

        Assert.True(membership.IsMember(reader.LocatorAt(0)));   // Error branch
        Assert.True(membership.IsMember(reader.LocatorAt(1)));   // Xml branch
        Assert.False(membership.IsMember(reader.LocatorAt(2)));  // neither
    }

    [Fact]
    public void ComputeMembership_WithoutDateFilter_RendersEveryRowAsCandidate()
    {
        ResolvedEvent[] events = [Event(1), Event(2), Event(3)];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event/>", [2] = "<Event/>", [3] = "<Event/>"
        });
        XmlFilterMembershipMatcher matcher = new(scanner);

        matcher.ComputeMembership(reader, IncludeFilter("Xml.Contains(\"x\")"), OwningLog, LogPathType.File, Ct);

        Assert.Equal([1, 2, 3], scanner.RenderedRecordIds.Order());
    }

    [Fact]
    public void Constructor_WithNullScanner_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new XmlFilterMembershipMatcher(null!));

    [Fact]
    public void IsMember_ForForeignLogGenerationOrOutOfRangeLocator_ReturnsFalse()
    {
        ResolvedEvent[] events = [Event(1)];
        (IEventColumnReader reader, EventLogId logId) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string> { [1] = "<Event>alpha</Event>" });
        XmlFilterMembershipMatcher matcher = new(scanner);

        XmlFilterMembership membership =
            matcher.ComputeMembership(reader, IncludeFilter("Xml.Contains(\"alpha\")"), OwningLog, LogPathType.File, Ct);

        Assert.True(membership.IsMember(reader.LocatorAt(0)));
        Assert.False(membership.IsMember(new EventLocator(logId, Generation: 99, Index: 0)));
        Assert.False(membership.IsMember(new EventLocator(EventLogId.Create(), Generation: 0, Index: 0)));
        Assert.False(membership.IsMember(new EventLocator(logId, Generation: 0, Index: 1)));
        Assert.False(membership.IsMember(new EventLocator(logId, Generation: 0, Index: -1)));
    }

    private static (IEventColumnReader Reader, EventLogId LogId) BuildReader(
        IReadOnlyList<ResolvedEvent> events, int generation = 0, long contentVersion = 0)
    {
        EventLogId logId = EventLogId.Create();
        IEventColumnReader reader = EventColumnStore.Build(events, generation, contentVersion).CreateReader(logId);

        return (reader, logId);
    }

    private static FakeBatchScanner EmptyScanner() => new(new Dictionary<long, string>());

    private static DateFilter EnabledDate(DateTime after, DateTime before) =>
        new() { After = after, Before = before, IsEnabled = true };

    private static ResolvedEvent Event(long recordId, string level = "Information", int minute = 0) =>
        FilterEventBuilder.CreateTestEvent(
            id: (int)recordId, recordId: recordId, level: level, timeCreated: s_baseTime.AddMinutes(minute));

    private static SavedFilter Exclude(string expression) =>
        SavedFilter.TryCreate(expression, isExcluded: true) ??
        throw new InvalidOperationException($"Test exclude expression failed to compile: {expression}");

    private static Filter ExcludeFilter(string expression) => new(null, [Exclude(expression)]);

    private static SavedFilter Include(string expression) =>
        SavedFilter.TryCreate(expression) ??
        throw new InvalidOperationException($"Test include expression failed to compile: {expression}");

    private static Filter IncludeFilter(string expression) => new(null, [Include(expression)]);

    private sealed class FakeBatchScanner(IReadOnlyDictionary<long, string> xmlByRecordId) : IEventXmlBatchScanner
    {
        public List<long> RenderedRecordIds { get; } = [];

        public IEnumerable<ScannedEventXml> Scan(
            string owningLog,
            LogPathType pathType,
            Func<long, bool> shouldRenderXml,
            CancellationToken cancellationToken)
        {
            foreach ((long recordId, string xml) in xmlByRecordId)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!shouldRenderXml(recordId)) { continue; }

                RenderedRecordIds.Add(recordId);

                yield return new ScannedEventXml(recordId, xml);
            }
        }
    }
}
