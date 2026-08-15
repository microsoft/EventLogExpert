// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;

namespace EventLogExpert.Filtering.Tests.Evaluation;

public sealed class XmlFilterMatcherTests
{
    private const string OwningLog = "TestLog";

    private static readonly DateTime s_baseTime = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Level == \"Error\" && Xml.Contains(\"needle\")", false)]
    [InlineData("Level == \"Error\" || Xml.Contains(\"needle\")", false)]
    [InlineData("!(Level == \"Error\") && Xml.Contains(\"needle\")", false)]
    [InlineData("Source == \"S1\" && Xml.Contains(\"needle\")", false)]
    [InlineData("EventData[\"Missing\"] == \"x\" && Xml.Contains(\"needle\")", false)]
    [InlineData("Level == \"Error\" && Xml.Contains(\"needle\")", true)]
    public void ComputeMatch_AcrossFilterShapes_MatchesTheBruteForceRenderAllReference(string expression, bool isExcluded)
    {
        // Narrowing must never change a row's result: assert ComputeMatch's bitset equals a reference that evaluates
        // EVERY row with its real (or empty) XML through the row path - the pre-narrowing behavior. Covers the risky
        // columnar-cheap-eval vs row-eval directions (NOT, EventData-absent Unknown, multi-shape) end to end.
        ResolvedEvent[] events =
        [
            Event(1, level: "Error", source: "S1"), Event(2, level: "Warning", source: "S1"),
            Event(3, level: "Error", source: "S2"), Event(4, level: "Information", source: "S2"),
            Event(5, level: "Warning", source: "S1")
        ];
        (IEventColumnReader reader, _) = BuildReader(events);
        Dictionary<long, string> xmlByRecordId = new()
        {
            [1] = "<Event>needle</Event>", [2] = "<Event>needle</Event>", [3] = "<Event>none</Event>",
            [4] = "<Event>needle</Event>", [5] = "<Event>none</Event>"
        };
        XmlFilterMatcher matcher = new(new FakeBatchScanner(xmlByRecordId));
        Filter filter = new(null, [isExcluded ? Exclude(expression) : Include(expression)]);

        XmlFilterMatch match = matcher.ComputeMatch(reader, filter, OwningLog, LogPathType.File, Ct);

        bool[] expected = BruteForceReference(reader, filter, xmlByRecordId);

        for (int index = 0; index < events.Length; index++)
        {
            Assert.Equal(expected[index], match.IsMatch(reader.LocatorAt(index)));
        }
    }

    [Fact]
    public void ComputeMatch_ProducesStampMatchingReaderSnapshot()
    {
        ResolvedEvent[] events = [Event(1), Event(2)];
        (IEventColumnReader reader, EventLogId logId) = BuildReader(events, generation: 3, contentVersion: 7);
        XmlFilterMatcher matcher = new(EmptyScanner());

        XmlFilterMatch match =
            matcher.ComputeMatch(reader, IncludeFilter("Xml.Contains(\"x\")"), OwningLog, LogPathType.File, Ct);

        Assert.Equal(logId, match.LogId);
        Assert.Equal(3, match.Generation);
        Assert.Equal(7, match.ContentVersion);
        Assert.Equal(2, match.Count);
    }

    [Fact]
    public void ComputeMatch_WhenCancellationRequested_Throws()
    {
        ResolvedEvent[] events = [Event(1), Event(2)];
        (IEventColumnReader reader, _) = BuildReader(events);
        XmlFilterMatcher matcher = new(EmptyScanner());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            matcher.ComputeMatch(reader, IncludeFilter("Xml.Contains(\"x\")"), OwningLog, LogPathType.File, cancellation.Token));
    }

    [Fact]
    public void ComputeMatch_WhenCandidateRecordRolledOut_EvaluatesWithEmptyXmlNotUnconditionalNoMatch()
    {
        // record id 5 rolled out of the log (scanner never yields it); record id 6 is present and matches the exclude.
        ResolvedEvent[] events = [Event(5), Event(6)];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string> { [6] = "<Event>secret</Event>" });
        XmlFilterMatcher matcher = new(scanner);

        XmlFilterMatch match =
            matcher.ComputeMatch(reader, ExcludeFilter("Xml.Contains(\"secret\")"), OwningLog, LogPathType.File, Ct);

        // Row 5's XML is unresolved (empty) so the exclude does NOT fire -> the row stays visible (a match); an
        // unconditional no-match would have wrongly hidden it. Row 6's rendered XML matches the exclude -> hidden.
        Assert.True(match.IsMatch(reader.LocatorAt(0)));
        Assert.False(match.IsMatch(reader.LocatorAt(1)));
    }

    [Fact]
    public void ComputeMatch_WhenEveryRowIsANonCandidate_SkipsTheScanEntirely()
    {
        // No row is at Critical, so the cheap conjunct rules every row out - the native scan is never started (NB4).
        ResolvedEvent[] events = [Event(1, level: "Error"), Event(2, level: "Warning")];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>keep</Event>", [2] = "<Event>keep</Event>"
        });
        XmlFilterMatcher matcher = new(scanner);

        XmlFilterMatch match = matcher.ComputeMatch(
            reader, IncludeFilter("Level == \"Critical\" && Xml.Contains(\"keep\")"), OwningLog, LogPathType.File, Ct);

        Assert.Equal(0, scanner.ScanCallCount);
        Assert.Empty(scanner.RenderedRecordIds);
        Assert.False(match.IsMatch(reader.LocatorAt(0)));
        Assert.False(match.IsMatch(reader.LocatorAt(1)));
    }

    [Fact]
    public void ComputeMatch_WhenNonCandidateSurvivesViaAnotherIncludedFilter_StaysVisibleWithoutRendering()
    {
        // Two included filters: an XML filter (Error + needle) and a cheap-only filter (Id == 2). The Warning/Id=2 row
        // fails the XML filter's cheap conjunct - it is a non-candidate and never rendered - but the Id filter keeps it
        // visible. Skipping its render must NOT drop it (the OR-across-included-filters soundness case).
        ResolvedEvent[] events = [Event(1, level: "Error"), Event(2, level: "Warning"), Event(3, level: "Warning")];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>needle</Event>", [2] = "<Event>needle</Event>", [3] = "<Event>none</Event>"
        });
        XmlFilterMatcher matcher = new(scanner);
        Filter filter = new(null, [Include("Level == \"Error\" && Xml.Contains(\"needle\")"), Include("Id == 2")]);

        XmlFilterMatch match = matcher.ComputeMatch(reader, filter, OwningLog, LogPathType.File, Ct);

        Assert.True(match.IsMatch(reader.LocatorAt(0)));   // Error + needle via the XML filter
        Assert.True(match.IsMatch(reader.LocatorAt(1)));   // visible via Id == 2 despite failing the XML filter's cheap conjunct
        Assert.False(match.IsMatch(reader.LocatorAt(2)));  // Warning, Id 3: neither filter

        // Only the Error row is a candidate; the two Warning rows are non-candidates and are never rendered.
        Assert.Equal([1], scanner.RenderedRecordIds.Order());
    }

    [Fact]
    public void ComputeMatch_WhenRowHasNoRecordId_EvaluatesWithEmptyXml()
    {
        ResolvedEvent[] events = [FilterEventBuilder.CreateTestEvent(id: 1, recordId: null, level: "Error"), Event(2, level: "Information")];
        (IEventColumnReader reader, _) = BuildReader(events);
        XmlFilterMatcher matcher = new(EmptyScanner());

        XmlFilterMatch match =
            matcher.ComputeMatch(reader, ExcludeFilter("Xml.Contains(\"secret\")"), OwningLog, LogPathType.File, Ct);

        // The record-id-less row is evaluated with empty XML: the exclude does not fire, so it remains a match.
        Assert.True(match.IsMatch(reader.LocatorAt(0)));
    }

    [Fact]
    public void ComputeMatch_WithCheapConjunct_RendersOnlyRowsThatSurviveTheCheapConditions()
    {
        // The XML filter's cheap conjunct (Level == Error) rules out the Warning row without rendering its XML.
        ResolvedEvent[] events = [Event(1, level: "Error"), Event(2, level: "Error"), Event(3, level: "Warning")];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>data</Event>", [2] = "<Event>none</Event>", [3] = "<Event>data</Event>"
        });
        XmlFilterMatcher matcher = new(scanner);

        XmlFilterMatch match = matcher.ComputeMatch(
            reader, IncludeFilter("Level == \"Error\" && Xml.Contains(\"data\")"), OwningLog, LogPathType.File, Ct);

        Assert.True(match.IsMatch(reader.LocatorAt(0)));   // Error + data
        Assert.False(match.IsMatch(reader.LocatorAt(1)));  // Error but no data (rendered, then excluded)
        Assert.False(match.IsMatch(reader.LocatorAt(2)));  // Warning: never a candidate

        // The Warning row (record 3) is a non-candidate and is never rendered; only the two Error rows are.
        Assert.Equal([1, 2], scanner.RenderedRecordIds.Order());
    }

    [Fact]
    public void ComputeMatch_WithDateFilter_ExcludesOutOfRangeRowsAndSkipsTheirXmlRender()
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
        XmlFilterMatcher matcher = new(scanner);
        Filter filter = new(EnabledDate(s_baseTime.AddMinutes(1), s_baseTime.AddMinutes(3)), [Include("Xml.Contains(\"keep\")")]);

        XmlFilterMatch match = matcher.ComputeMatch(reader, filter, OwningLog, LogPathType.File, Ct);

        Assert.False(match.IsMatch(reader.LocatorAt(0)));
        Assert.True(match.IsMatch(reader.LocatorAt(1)));
        Assert.True(match.IsMatch(reader.LocatorAt(2)));
        Assert.True(match.IsMatch(reader.LocatorAt(3)));
        Assert.False(match.IsMatch(reader.LocatorAt(4)));

        // The two date-excluded rows (record ids 1 and 5) must never have their XML rendered.
        Assert.Equal([2, 3, 4], scanner.RenderedRecordIds.Order());
    }

    [Fact]
    public void ComputeMatch_WithMixedCheapAndXmlFilter_MatchesOnlyRowsSatisfyingBoth()
    {
        ResolvedEvent[] events = [Event(1, level: "Error"), Event(2, level: "Error"), Event(3, level: "Warning")];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>data</Event>", [2] = "<Event>none</Event>", [3] = "<Event>data</Event>"
        });
        XmlFilterMatcher matcher = new(scanner);

        XmlFilterMatch match = matcher.ComputeMatch(
            reader, IncludeFilter("Level == \"Error\" && Xml.Contains(\"data\")"), OwningLog, LogPathType.File, Ct);

        Assert.True(match.IsMatch(reader.LocatorAt(0)));   // Error + data
        Assert.False(match.IsMatch(reader.LocatorAt(1)));  // Error but no data
        Assert.False(match.IsMatch(reader.LocatorAt(2)));  // data but Warning
    }

    [Fact]
    public void ComputeMatch_WithNullReaderOrEmptyOwningLog_Throws()
    {
        (IEventColumnReader reader, _) = BuildReader([Event(1)]);
        XmlFilterMatcher matcher = new(EmptyScanner());
        Filter filter = IncludeFilter("Xml.Contains(\"x\")");

        Assert.Throws<ArgumentNullException>(() =>
            matcher.ComputeMatch(null!, filter, OwningLog, LogPathType.File, Ct));
        Assert.Throws<ArgumentException>(() =>
            matcher.ComputeMatch(reader, filter, string.Empty, LogPathType.File, Ct));
    }

    [Fact]
    public void ComputeMatch_WithXmlExcludeFilter_VetoesRowsWhoseRenderedXmlMatches()
    {
        ResolvedEvent[] events = [Event(1), Event(2)];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>secret</Event>", [2] = "<Event>clean</Event>"
        });
        XmlFilterMatcher matcher = new(scanner);

        XmlFilterMatch match =
            matcher.ComputeMatch(reader, ExcludeFilter("Xml.Contains(\"secret\")"), OwningLog, LogPathType.File, Ct);

        Assert.False(match.IsMatch(reader.LocatorAt(0)));
        Assert.True(match.IsMatch(reader.LocatorAt(1)));
    }

    [Fact]
    public void ComputeMatch_WithXmlIncludeFilter_MarksOnlyRowsWhoseRenderedXmlMatches()
    {
        ResolvedEvent[] events = [Event(1), Event(2), Event(3)];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>alpha</Event>", [2] = "<Event>beta</Event>", [3] = "<Event>alpha</Event>"
        });
        XmlFilterMatcher matcher = new(scanner);

        XmlFilterMatch match =
            matcher.ComputeMatch(reader, IncludeFilter("Xml.Contains(\"alpha\")"), OwningLog, LogPathType.File, Ct);

        Assert.True(match.IsMatch(reader.LocatorAt(0)));
        Assert.False(match.IsMatch(reader.LocatorAt(1)));
        Assert.True(match.IsMatch(reader.LocatorAt(2)));
    }

    [Fact]
    public void ComputeMatch_WithXmlUnderOr_MatchesRowsViaEitherBranch()
    {
        ResolvedEvent[] events = [Event(1, level: "Error"), Event(2, level: "Information"), Event(3, level: "Information")];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>none</Event>", [2] = "<Event>needle</Event>", [3] = "<Event>none</Event>"
        });
        XmlFilterMatcher matcher = new(scanner);

        XmlFilterMatch match = matcher.ComputeMatch(
            reader, IncludeFilter("Level == \"Error\" || Xml.Contains(\"needle\")"), OwningLog, LogPathType.File, Ct);

        Assert.True(match.IsMatch(reader.LocatorAt(0)));   // Error branch
        Assert.True(match.IsMatch(reader.LocatorAt(1)));   // Xml branch
        Assert.False(match.IsMatch(reader.LocatorAt(2)));  // neither
    }

    [Fact]
    public void ComputeMatch_WithXmlUnderOr_RendersEveryRowSinceCheapNarrowingCannotApply()
    {
        // XML sits under a top-level OR, so there are no cheap AND-conjuncts to narrow by: every row stays a candidate.
        ResolvedEvent[] events = [Event(1, level: "Error"), Event(2, level: "Information"), Event(3, level: "Information")];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event>none</Event>", [2] = "<Event>needle</Event>", [3] = "<Event>none</Event>"
        });
        XmlFilterMatcher matcher = new(scanner);

        matcher.ComputeMatch(
            reader, IncludeFilter("Level == \"Error\" || Xml.Contains(\"needle\")"), OwningLog, LogPathType.File, Ct);

        Assert.Equal([1, 2, 3], scanner.RenderedRecordIds.Order());
    }

    [Fact]
    public void ComputeMatch_WithoutDateFilter_RendersEveryRowAsCandidate()
    {
        ResolvedEvent[] events = [Event(1), Event(2), Event(3)];
        (IEventColumnReader reader, _) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string>
        {
            [1] = "<Event/>", [2] = "<Event/>", [3] = "<Event/>"
        });
        XmlFilterMatcher matcher = new(scanner);

        matcher.ComputeMatch(reader, IncludeFilter("Xml.Contains(\"x\")"), OwningLog, LogPathType.File, Ct);

        Assert.Equal([1, 2, 3], scanner.RenderedRecordIds.Order());
    }

    [Fact]
    public void Constructor_WithNullScanner_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new XmlFilterMatcher(null!));

    [Fact]
    public void IsMatch_ForForeignLogGenerationOrOutOfRangeLocator_ReturnsFalse()
    {
        ResolvedEvent[] events = [Event(1)];
        (IEventColumnReader reader, EventLogId logId) = BuildReader(events);
        FakeBatchScanner scanner = new(new Dictionary<long, string> { [1] = "<Event>alpha</Event>" });
        XmlFilterMatcher matcher = new(scanner);

        XmlFilterMatch match =
            matcher.ComputeMatch(reader, IncludeFilter("Xml.Contains(\"alpha\")"), OwningLog, LogPathType.File, Ct);

        Assert.True(match.IsMatch(reader.LocatorAt(0)));
        Assert.False(match.IsMatch(new EventLocator(logId, Generation: 99, Index: 0)));
        Assert.False(match.IsMatch(new EventLocator(EventLogId.Create(), Generation: 0, Index: 0)));
        Assert.False(match.IsMatch(new EventLocator(logId, Generation: 0, Index: 1)));
        Assert.False(match.IsMatch(new EventLocator(logId, Generation: 0, Index: -1)));
    }

    private static bool[] BruteForceReference(
        IEventColumnReader reader, Filter filter, IReadOnlyDictionary<long, string> xmlByRecordId)
    {
        int count = reader.Count;
        long[] recordIds = new long[count];
        bool[] hasRecordId = new bool[count];
        reader.CopyInt64Column(EventFieldId.RecordId, recordIds, hasRecordId);

        bool[] expected = new bool[count];

        for (int index = 0; index < count; index++)
        {
            EventLocator locator = reader.LocatorAt(index);
            string xml = hasRecordId[index] && xmlByRecordId.TryGetValue(recordIds[index], out string? mapped)
                ? mapped
                : string.Empty;
            ResolvedEvent detail = reader.GetDetail(locator) with { Xml = xml };

            expected[index] = detail.MatchesFilters(filter.Filters) && detail.MatchesDateFilter(filter.DateFilter);
        }

        return expected;
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

    private static ResolvedEvent Event(long recordId, string level = "Information", int minute = 0, string source = "TestSource") =>
        FilterEventBuilder.CreateTestEvent(
            id: (int)recordId, recordId: recordId, level: level, source: source, timeCreated: s_baseTime.AddMinutes(minute));

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

        public int ScanCallCount { get; private set; }

        public IEnumerable<ScannedEventXml> Scan(
            string owningLog,
            LogPathType pathType,
            Func<long, bool> shouldRenderXml,
            CancellationToken cancellationToken)
        {
            ScanCallCount++;

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
