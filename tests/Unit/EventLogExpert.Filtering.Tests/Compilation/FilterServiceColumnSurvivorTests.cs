// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;

namespace EventLogExpert.Filtering.Tests.Compilation;

/// <summary>
///     Differential parity gate asserting <see cref="FilterService.GetSurvivingOrder" /> over a real
///     <c>EventColumnStore</c> reader returns exactly the AoS oracle survivor set (
///     <c>MatchesFilters &amp;&amp; MatchesDateFilter</c>) for every battery filter.
/// </summary>
public sealed class FilterServiceColumnSurvivorTests
{
    private static readonly DateTime s_baseTime = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<ResolvedEvent> s_corpus = BuildCorpus();

    private static readonly IEventColumnReader s_reader =
        EventColumnStore.Build(s_corpus, generation: 0, contentVersion: 0).CreateReader(EventLogId.Create());

    // The filter combos exercised against the oracle. Names are xUnit-serializable; ResolveFilter maps each to a Filter.
    public static TheoryData<string> Battery =>
    [
        "empty-no-filters",
        "include-source",
        "include-level-error",
        "include-level-error-or-warning",
        "exclude-source",
        "include-and-exclude",
        "date-only",
        "date-plus-include",
        "userdata-include",
        "userdata-exclude",
        "eventdata-include",
        "wildcard-eventdata-include",
        "contains-source",
        "multi-equals-id",
        "null-compiled-only",
        "null-compiled-plus-include",
        "date-plus-exclude",
        "date-disabled-with-bounds",
        "date-after-only-null-before"
    ];

    [Theory]
    [MemberData(nameof(Battery))]
    public void GetSurvivingOrder_ForEveryBatteryFilter_MatchesAosOracleSet(string caseName)
    {
        Filter filter = ResolveFilter(caseName);

        var expected = new HashSet<int>();

        for (var index = 0; index < s_corpus.Count; index++)
        {
            if (s_corpus[index].MatchesFilters(filter.Filters) &&
                s_corpus[index].MatchesDateFilter(filter.DateFilter))
            {
                expected.Add(index);
            }
        }

        IReadOnlyList<int> survivors = FilterService.GetSurvivingOrder(s_reader, filter);

        Assert.True(
            expected.SetEquals(survivors),
            $"Survivor divergence for '{caseName}': oracle={Format(expected)}, column={Format(survivors)}.");

        AssertAscendingDistinctPhysicalIndices(survivors);
    }

    [Fact]
    public void GetSurvivingOrder_WhenDateRange_IncludesBothBoundariesInclusive()
    {
        DateTime after = s_corpus[2].TimeCreated;
        DateTime before = s_corpus[7].TimeCreated;
        var filter = new Filter(EnabledDate(after, before), []);

        var survivors = new HashSet<int>(FilterService.GetSurvivingOrder(s_reader, filter));

        // Both endpoints are inclusive: the row exactly at After (2) and the row exactly at Before (7) survive.
        Assert.Contains(2, survivors);
        Assert.Contains(7, survivors);

        // Rows immediately outside the range do not.
        Assert.DoesNotContain(1, survivors);
        Assert.DoesNotContain(8, survivors);
    }

    [Fact]
    public void GetSurvivingOrder_WhenExcludeFilterYieldsUnknown_HidesOnlyDecisiveMatch()
    {
        SavedFilter exclude = Exclude("UserData[\"Secret\"] == \"hidden\"");
        CompiledFilter compiled = exclude.Compiled!;

        Assert.Equal(FilterMatch.Unknown, Oracle(compiled, s_corpus[7]));
        Assert.Equal(FilterMatch.Match, Oracle(compiled, s_corpus[8]));
        Assert.Equal(FilterMatch.Unknown, Oracle(compiled, s_corpus[9]));

        var survivors = new HashSet<int>(FilterService.GetSurvivingOrder(s_reader, new Filter(null, [exclude])));

        // Exclude hides only the decisive Match (8); the Unknown rows (7, 9) stay visible.
        Assert.DoesNotContain(8, survivors);
        Assert.Contains(7, survivors);
        Assert.Contains(9, survivors);
    }

    [Fact]
    public void GetSurvivingOrder_WhenIncludeFilterYieldsUnknown_KeepsThoseRowsVisible()
    {
        SavedFilter include = Include("UserData[\"Secret\"] == \"hidden\"");
        CompiledFilter compiled = include.Compiled!;

        // Precondition: the tri-state oracle yields Unknown for the truncated (7) and incomplete-absent (9) rows and a
        // decisive Match for the complete row (8). Without this the "keeps visible" assertion below would be vacuous.
        Assert.Equal(FilterMatch.Unknown, Oracle(compiled, s_corpus[7]));
        Assert.Equal(FilterMatch.Match, Oracle(compiled, s_corpus[8]));
        Assert.Equal(FilterMatch.Unknown, Oracle(compiled, s_corpus[9]));

        var survivors = new HashSet<int>(FilterService.GetSurvivingOrder(s_reader, new Filter(null, [include])));

        // Include keeps a row on Match OR Unknown; all three UserData rows survive.
        Assert.Contains(7, survivors);
        Assert.Contains(8, survivors);
        Assert.Contains(9, survivors);
    }

    private static void AssertAscendingDistinctPhysicalIndices(IReadOnlyList<int> survivors)
    {
        for (var position = 0; position < survivors.Count; position++)
        {
            Assert.InRange(survivors[position], 0, s_corpus.Count - 1);

            if (position > 0)
            {
                Assert.True(
                    survivors[position] > survivors[position - 1],
                    $"Survivors must be strictly ascending physical indices, got [{string.Join(", ", survivors)}].");
            }
        }
    }

    private static IReadOnlyList<ResolvedEvent> BuildCorpus() =>
    [
        // 0-4: scalar events driving the include/exclude scalar, contains, multi-equals and date arms.
        FilterEventBuilder.CreateTestEvent(
            id: 100, source: "TestSource", level: "Error", computerName: "SERVER01",
            timeCreated: s_baseTime.AddMinutes(0)),
        FilterEventBuilder.CreateTestEvent(
            id: 200, source: "TestSource", level: "Warning", computerName: "SERVER01",
            timeCreated: s_baseTime.AddMinutes(1)),
        FilterEventBuilder.CreateTestEvent(
            id: 300, source: "OtherSource", level: "Error", computerName: "SERVER02",
            timeCreated: s_baseTime.AddMinutes(2)),
        FilterEventBuilder.CreateTestEvent(
            id: 400, source: "OtherSource", level: "Information", computerName: "SERVER02",
            timeCreated: s_baseTime.AddMinutes(3)),
        FilterEventBuilder.CreateTestEvent(
            id: 100, source: "TestSource", level: "Information", computerName: "WORKSTATION",
            timeCreated: s_baseTime.AddMinutes(4)),

        // 5-6: EventData events for the presence-required named-field (exact and wildcard) arms.
        EventDataTestFactory.CreateEventWithData(("User", "admin"))
            with { Id = 500, TimeCreated = s_baseTime.AddMinutes(5) },
        EventDataTestFactory.CreateEventWithData(("User", "guest"))
            with { Id = 600, TimeCreated = s_baseTime.AddMinutes(6) },

        // 7: UserData present, truncated, and non-matching -> tri-state Unknown on '==' (the truncated tail could differ).
        new ResolvedEvent("TestLog", LogPathType.Channel)
        {
            Id = 700,
            TimeCreated = s_baseTime.AddMinutes(7),
            UserData = [new UserDataField("Secret", ["hid"], IsTruncated: true)]
        },

        // 8: UserData present and complete -> decisive Match on '=='.
        new ResolvedEvent("TestLog", LogPathType.Channel)
        {
            Id = 800,
            TimeCreated = s_baseTime.AddMinutes(8),
            UserData = [new UserDataField("Secret", ["hidden"], IsTruncated: false)]
        },

        // 9: UserDataIncomplete with the probed path absent -> keep-visible Unknown on '=='.
        new ResolvedEvent("TestLog", LogPathType.Channel)
        {
            Id = 900,
            TimeCreated = s_baseTime.AddMinutes(9),
            UserDataIncomplete = true
        }
    ];

    private static DateFilter DisabledDate(DateTime after, DateTime before) =>
        new() { After = after, Before = before, IsEnabled = false };

    private static DateFilter EnabledDate(DateTime? after, DateTime? before) =>
        new() { After = after, Before = before, IsEnabled = true };

    private static SavedFilter Exclude(string expression) =>
        SavedFilter.TryCreate(expression, isExcluded: true) ??
        throw new InvalidOperationException($"Test exclude expression failed to compile: {expression}");

    private static string Format(IEnumerable<int> indices) =>
        $"[{string.Join(", ", indices.OrderBy(index => index))}]";

    private static SavedFilter Include(string expression) =>
        SavedFilter.TryCreate(expression) ??
        throw new InvalidOperationException($"Test include expression failed to compile: {expression}");

    private static FilterMatch Oracle(CompiledFilter compiled, ResolvedEvent resolvedEvent) =>
        compiled.Evaluate?.Invoke(resolvedEvent) ??
        (compiled.Predicate(resolvedEvent) ? FilterMatch.Match : FilterMatch.NoMatch);

    private static Filter ResolveFilter(string caseName) =>
        caseName switch
        {
            "empty-no-filters" => new Filter(null, []),
            "include-source" => new Filter(null, [Include("Source == \"TestSource\"")]),
            "include-level-error" => new Filter(null, [Include("Level == \"Error\"")]),
            "include-level-error-or-warning" =>
                new Filter(null, [Include("Level == \"Error\""), Include("Level == \"Warning\"")]),
            "exclude-source" => new Filter(null, [Exclude("Source == \"TestSource\"")]),
            "include-and-exclude" =>
                new Filter(null, [Include("ComputerName == \"SERVER01\""), Exclude("Level == \"Warning\"")]),
            "date-only" =>
                new Filter(EnabledDate(s_baseTime.AddMinutes(2), s_baseTime.AddMinutes(7)), []),
            "date-plus-include" =>
                new Filter(EnabledDate(s_baseTime.AddMinutes(0), s_baseTime.AddMinutes(4)), [Include("Level == \"Error\"")]),
            "userdata-include" => new Filter(null, [Include("UserData[\"Secret\"] == \"hidden\"")]),
            "userdata-exclude" => new Filter(null, [Exclude("UserData[\"Secret\"] == \"hidden\"")]),
            "eventdata-include" => new Filter(null, [Include("EventData[\"User\"] == \"admin\"")]),
            "wildcard-eventdata-include" => new Filter(null, [Include("EventData[\"Us*\"] == \"admin\"")]),
            "contains-source" => new Filter(null, [Include("Source.Contains(\"Source\")")]),
            "multi-equals-id" => new Filter(null, [Include("(new[] {\"100\", \"300\"}).Contains(Id.ToString())")]),
            "null-compiled-only" => new Filter(null, [SavedFilter.Empty]),
            "null-compiled-plus-include" => new Filter(null, [SavedFilter.Empty, Include("Level == \"Error\"")]),
            "date-plus-exclude" =>
                new Filter(EnabledDate(s_baseTime.AddMinutes(0), s_baseTime.AddMinutes(9)), [Exclude("Source == \"TestSource\"")]),
            "date-disabled-with-bounds" =>
                new Filter(DisabledDate(s_baseTime.AddMinutes(2), s_baseTime.AddMinutes(5)), []),
            "date-after-only-null-before" =>
                new Filter(EnabledDate(s_baseTime.AddMinutes(3), null), []),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Unknown battery case.")
        };
}
