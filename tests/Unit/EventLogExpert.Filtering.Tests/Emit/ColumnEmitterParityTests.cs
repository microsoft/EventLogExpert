// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Filtering.Parsing;
using EventLogExpert.Filtering.Tests.TestUtils;

namespace EventLogExpert.Filtering.Tests.Emit;

/// <summary>
///     Differential parity gate for the column-direct backend. Every battery expression is compiled by both backends;
///     for each synthetic corpus event the column tri-state result must equal the row oracle (
///     <c>Evaluate ?? (Predicate ? Match : NoMatch)</c>). Running the column emitter over the row-backed
///     <see cref="LegacyEventColumnReader" /> isolates the emitter-arm logic; the store parity tests already prove the
///     real column store reads identically to the legacy reader, so column-over-store equals column-over-legacy
///     transitively. The corpus deliberately exercises every null/absence case (null UserId, absent nullable ids,
///     empty/present Xml, multi-value EventData with an absent named field, present + truncated + absent UserData paths).
/// </summary>
public sealed class ColumnEmitterParityTests
{
    private static readonly ResolvedEvent s_eventDataOther =
        EventDataTestFactory.CreateEventWithData(("Other", "x"));
    private static readonly ResolvedEvent s_eventDataRich = EventDataTestFactory.CreateEventWithData(
        ("User", "admin"),
        ("Code", 5L),
        ("Flag", true),
        ("Path", @"C:\Windows\System32\cmd.exe"),
        ("Dup", "first"),
        ("Dup", "second"));
    private static readonly ResolvedEvent s_readerEnablerEvent = new("TestLog", LogPathType.Channel)
    {
        Id = 700,
        Opcode = "Start",
        RelatedActivityId = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        Keywords = [],
        TimeCreated = FilterTestFixtures.FixedTimestamp
    };
    private static readonly ResolvedEvent s_userDataIncompleteEvent = new("TestLog", LogPathType.Channel)
    {
        Id = 501,
        UserData = [new UserDataField("Path", ["v1"], IsTruncated: false)],
        UserDataIncomplete = true
    };
    private static readonly ResolvedEvent s_userDataPresentTruncated = new("TestLog", LogPathType.Channel)
    {
        Id = 500,
        UserData =
        [
            new UserDataField("Foo", ["adminvalue"], IsTruncated: false),
            new UserDataField("Trunc", ["y"], IsTruncated: true)
        ]
    };
    private static readonly IReadOnlyList<ResolvedEvent> s_corpus =
    [
        FilterTestFixtures.FullyPopulated,
        FilterTestFixtures.NoNullables,
        FilterTestFixtures.KernelPower,
        FilterTestFixtures.ApplicationError,
        FilterTestFixtures.WerSystemError,
        FilterTestFixtures.WithEscapes,
        s_eventDataRich,
        s_eventDataOther,
        s_userDataPresentTruncated,
        s_userDataIncompleteEvent,
        s_readerEnablerEvent
    ];

    // Index 1 is FilterTestFixtures.NoNullables: the null-UserId + absent-nullable-ids event that drives the null-vs-present asymmetry.

    // Multi-value EventData: typed values (Int64/bool), a duplicate name (first-wins), and a probed-absent name ("Missing").

    private static readonly LegacyEventColumnReader s_reader =
        new(EventLogId.Create(), generation: 1, contentVersion: 1, s_corpus);

    // UserDataIncomplete: an absent key yields a keep-visible truncated (not decisive-absent) result.

    /// <summary>
    ///     The parity battery, each expression tagged with the arm it exercises and the null/absence case(s) it
    ///     exercises. Every expression compiles on both backends and is differentially checked against all eleven corpus
    ///     events.
    /// </summary>
    public static TheoryData<string> DifferentialBattery =>
    [
        // AndNode / OrNode: lazy tri-state combine (2/3-way, nested, short-circuit).
        "Id == 100 && Source == \"TestSource\"",
        "Id == 100 || Id == 200",
        "(Id == 100 || Id == 200) && Level == \"Error\"",
        "Id == 100 && Source == \"TestSource\" && Level == \"Error\" && ComputerName == \"SERVER01\"",
        "Id == 100 || Id == 200 || Id == 300 || Id == 400",

        // NotNode: general !inner lifted (over scalar, string, and nullable comparisons).
        "!(Id == 100)",
        "!(Source == \"TestSource\")",
        "!(ProcessId == 4)",
        "!(ProcessId != 4)",

        // ComparisonNode typed scalar: Id (non-nullable) and nullable ids.
        "Id == 100",
        "Id != 100",
        "Id > 100",
        "Id >= 100",
        "Id < 100",
        "Id <= 100",
        "ProcessId == 4",
        "ProcessId != 5",
        "ProcessId > 3",
        "ProcessId < 10",
        "ThreadId == 8",
        "ThreadId != 8",
        "RecordId == 1234567890123",
        "RecordId != 1234567890123",
        "ActivityId == \"11111111-2222-3333-4444-555555555555\"",
        "ActivityId != \"11111111-2222-3333-4444-555555555555\"",

        // ComparisonNode string-form: string props (always String kind), Id/nullable-id ToString ("" branch).
        "Source == \"TestSource\"",
        "Source != \"TestSource\"",
        "ComputerName == \"SERVER01\"",
        "Level == \"Error\"",
        "Id.ToString() == \"100\"",
        "Id.ToString() != \"100\"",
        "ProcessId.ToString() == \"4\"",
        "ProcessId.ToString() != \"4\"",

        // UserId string-form (presence-required): the paired null-guard folds into a single UserId comparison.
        "UserId != null && UserId.Value == \"S-1-5-18\"",
        "UserId != null && UserId.Value != \"S-1-5-18\"",

        // ContainsNode: string props, numeric-id ToString form, UserId (presence-required), and the NOT(UserId.Contains) special.
        "Source.Contains(\"Test\")",
        "Source.Contains(\"test\", StringComparison.OrdinalIgnoreCase)",
        "Description.Contains(\"error occurred\", StringComparison.OrdinalIgnoreCase)",
        "ActivityId.ToString().Contains(\"1111\", StringComparison.OrdinalIgnoreCase)",
        "Xml.Contains(\"data\", StringComparison.OrdinalIgnoreCase)",
        "!Source.Contains(\"Test\")",
        "UserId != null && UserId.Value.Contains(\"S-1-5\", StringComparison.OrdinalIgnoreCase)",
        "UserId != null && !UserId.Value.Contains(\"S-1-5-99\", StringComparison.OrdinalIgnoreCase)",

        // ComparisonNode null-literal (EmitNullComparison): nullable ids, UserId, string props, Id.
        "ProcessId == null",
        "ProcessId != null",
        "ThreadId == null",
        "RecordId == null",
        "ActivityId == null",
        "ActivityId != null",
        "UserId == null",
        "UserId != null",
        "ComputerName == null",
        "ComputerName != null",
        "Id == null",
        "Id != null",

        // MultiEqualsNode: Int (Id), String (Level, Source), nullable-int absent guard (ProcessId), the
        // presence-required UserId arm (exercised over the null-UserId corpus event), and the RecordId/ActivityId
        // dispatch. All reachable via the ordinary array-contains path (Lowerer.ResolveFieldOrToString ->
        // PropertyResolver), NOT the UserId null-guard collapse.
        "(new[] {\"100\", \"200\"}).Contains(Id.ToString())",
        "(new[] {\"Error\", \"Warning\"}).Contains(Level.ToString())",
        "(new[] {\"TestSource\", \"OtherSource\"}).Contains(Source)",
        "(new[] {\"4\", \"5\"}).Contains(ProcessId.ToString())",
        "(new[] {\"S-1-5-18\", \"S-1-6-99\"}).Contains(UserId)",
        "(new[] {\"1234567890123\", \"999\"}).Contains(RecordId.ToString())",
        "(new[] {\"11111111-2222-3333-4444-555555555555\"}).Contains(ActivityId.ToString())",

        // EventData exact-name (presence-required, all ops; typed coercion; duplicate-name first-wins; absent name).
        "EventData[\"User\"] == \"admin\"",
        "EventData[\"User\"] != \"admin\"",
        "EventData[\"User\"] != \"other\"",
        "EventData[\"Code\"] == \"5\"",
        "EventData[\"Flag\"] == \"true\"",
        "EventData[\"User\"].Contains(\"adm\", StringComparison.OrdinalIgnoreCase)",
        "!EventData[\"User\"].Contains(\"xyz\")",
        "(new[] {\"admin\", \"root\"}).Contains(EventData[\"User\"])",
        "EventData[\"Dup\"] == \"first\"",
        "EventData[\"Missing\"] == \"x\"",
        "EventData[\"Missing\"] != \"x\"",

        // UserData exact-path (tri-state via the shared UserDataMatch core): present, truncated, incomplete, absent, nested.
        "UserData[\"Foo\"] == \"adminvalue\"",
        "UserData[\"Foo\"] != \"adminvalue\"",
        "UserData[\"Foo\"].Contains(\"admin\", StringComparison.OrdinalIgnoreCase)",
        "!UserData[\"Foo\"].Contains(\"zzz\")",
        "(new[] {\"adminvalue\", \"root\"}).Contains(UserData[\"Foo\"])",
        "UserData[\"Trunc\"] == \"y\"",
        "UserData[\"Path\"] == \"v1\"",
        "UserData[\"Absent\"] == \"x\"",
        "UserData[\"Foo/Bar\"] == \"x\"",

        // Mixed UserData + scalar: tri-state combine of a decisive scalar arm with a tri-state UserData arm.
        "Id == 100 && UserData[\"Foo\"] == \"adminvalue\"",
        "Id == 999 || UserData[\"Foo\"] == \"adminvalue\"",
        "Id == 100 && Xml.Contains(\"data\")",

        // P2-4b Keywords.Any (three lowered shapes): positive over corpus[0] (Keywords [Audit, System]), negative
        // elsewhere. The ordinal MatchAnyOf shape with lowercase needles matches nothing; the "System" shape matches corpus[0].
        "Keywords.Any(e => string.Equals(e, \"Audit\", StringComparison.OrdinalIgnoreCase))",
        "Keywords.Any(e => e.Contains(\"audit\", StringComparison.OrdinalIgnoreCase))",
        "Keywords.Any(e => (new[] {\"audit\", \"system\"}).Contains(e))",
        "Keywords.Any(e => (new[] {\"System\", \"Nope\"}).Contains(e))",

        // P2-4b wildcard EventData field names (positional enumeration over corpus[6] rich EventData): exact-op equality
        // AND inequality, Contains, MultiEquals, the non-first duplicate ("Du*" must reach the 2nd Dup -> "second"), and
        // empty-match globs.
        "EventData[\"Us*\"] == \"admin\"",
        "EventData[\"Us*\"] != \"admin\"",
        "EventData[\"Pat*\"].Contains(\"cmd\", StringComparison.OrdinalIgnoreCase)",
        "(new[] {\"admin\", \"root\"}).Contains(EventData[\"Us*\"])",
        "EventData[\"Du*\"] == \"second\"",
        "EventData[\"*zzz*\"] == \"x\"",
        "EventData[\"*cert*\"] == \"x\"",

        // Opcode (pooled string) + RelatedActivityId (nullable Guid): the reader-enabler fields. Opcode exercises the
        // string-form, Contains, and MultiEquals(String) arms; RelatedActivityId exercises the typed-Guid, ToString
        // Contains/MultiEquals, and null-comparison arms. corpus[10] sets both; every other event leaves them empty/null.
        "Opcode == \"Start\"",
        "Opcode != \"Start\"",
        "Opcode.Contains(\"tar\", StringComparison.OrdinalIgnoreCase)",
        "(new[] {\"Start\", \"Stop\"}).Contains(Opcode)",
        "RelatedActivityId == \"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"",
        "RelatedActivityId != \"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"",
        "RelatedActivityId == null",
        "RelatedActivityId != null",
        "RelatedActivityId.ToString().Contains(\"aaaa\", StringComparison.OrdinalIgnoreCase)",
        "(new[] {\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\"}).Contains(RelatedActivityId.ToString())",

        // P2-4b glob UserData paths (tri-state): present match on corpus[8] Foo, truncated Trunc -> Unknown, and the
        // incomplete-tail Unknown over corpus[9] (UserDataIncomplete, no path matches -> keep-visible Unknown).
        "UserData[\"*oo\"] == \"adminvalue\"",
        "UserData[\"Tr*\"].Contains(\"y\")",
        "UserData[\"*nomatch*\"] == \"x\"",
        "UserData[\"*cert*\"] == \"x\""
    ];

    [Theory]
    [MemberData(nameof(DifferentialBattery))]
    public void ColumnBackend_MatchesRowOracle_AcrossCorpus(string expression)
    {
        Assert.True(
            FilterParser.TryCompile(expression, out CompiledFilter? row, out var rowError),
            $"Row backend failed to compile '{expression}': {rowError}");
        Assert.True(
            FilterParser.TryCompileColumn(expression, out ColumnCompiledFilter? column, out var columnError),
            $"Column backend failed to compile '{expression}': {columnError}");

        Assert.Equal(row.RequiresXml, column.RequiresXml);

        for (var index = 0; index < s_corpus.Count; index++)
        {
            FilterMatch expected = Oracle(row, s_corpus[index]);
            FilterMatch actual = column.Evaluate(s_reader, s_reader.LocatorAt(index));

            Assert.True(
                expected == actual,
                $"Divergence at corpus[{index}] for '{expression}': row={expected}, column={actual}.");
        }
    }

    // Absolute (not differential) oracle for the Opcode reader-enabler field: exact match results pin the string-compare
    // semantics on BOTH backends, so a same-direction bug in both emitters cannot pass.
    [Fact]
    public void OpcodeEquals_Absolute_MatchesOnlyTheExactValue()
    {
        ResolvedEvent start = new("TestLog", LogPathType.Channel) { Id = 1, Opcode = "Start" };
        ResolvedEvent stop = new("TestLog", LogPathType.Channel) { Id = 2, Opcode = "Stop" };
        ResolvedEvent emptyOpcode = new("TestLog", LogPathType.Channel) { Id = 3 };

        AssertBothBackends("Opcode == \"Start\"", start, FilterMatch.Match);
        AssertBothBackends("Opcode == \"Start\"", stop, FilterMatch.NoMatch);
        AssertBothBackends("Opcode == \"Start\"", emptyOpcode, FilterMatch.NoMatch);
        AssertBothBackends("Opcode.Contains(\"top\", StringComparison.OrdinalIgnoreCase)", stop, FilterMatch.Match);
    }

    // A nullable numeric '!=' on an absent field is Match (typed lift), unlike the presence-required UserId arms.
    [Fact]
    public void ProcessIdNotEquals_OverAbsentProcessId_IsMatch()
    {
        AssertBothBackends("ProcessId != 5", FilterTestFixtures.NoNullables, FilterMatch.Match);
    }

    // Absolute oracle for the RelatedActivityId reader-enabler field: the typed-Guid equality and the null comparison must
    // distinguish a specific Guid, a different Guid, absent (null), and present-but-Guid.Empty (the has-value flag makes
    // Guid.Empty a real value, NOT the same as absent).
    [Fact]
    public void RelatedActivityIdComparisons_Absolute_DistinguishGuidNullAndEmpty()
    {
        var guidA = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        ResolvedEvent eventA = new("TestLog", LogPathType.Channel) { Id = 1, RelatedActivityId = guidA };
        ResolvedEvent eventB = new("TestLog", LogPathType.Channel)
        {
            Id = 2,
            RelatedActivityId = new Guid("11111111-1111-1111-1111-111111111111")
        };
        ResolvedEvent eventNull = new("TestLog", LogPathType.Channel) { Id = 3 };
        ResolvedEvent eventEmpty = new("TestLog", LogPathType.Channel) { Id = 4, RelatedActivityId = Guid.Empty };

        AssertBothBackends($"RelatedActivityId == \"{guidA}\"", eventA, FilterMatch.Match);
        AssertBothBackends($"RelatedActivityId == \"{guidA}\"", eventB, FilterMatch.NoMatch);
        AssertBothBackends($"RelatedActivityId == \"{guidA}\"", eventNull, FilterMatch.NoMatch);
        AssertBothBackends("RelatedActivityId == null", eventNull, FilterMatch.Match);
        AssertBothBackends("RelatedActivityId == null", eventEmpty, FilterMatch.NoMatch);
        AssertBothBackends("RelatedActivityId != null", eventEmpty, FilterMatch.Match);
    }

    // UserId '==' on an absent UserId is NoMatch (presence-required).
    [Fact]
    public void UserIdEquals_OverNullUserId_IsNoMatch()
    {
        AssertBothBackends(
            "UserId != null && UserId.Value == \"S-1-5-18\"",
            FilterTestFixtures.NoNullables,
            FilterMatch.NoMatch);
    }

    // NOT(UserId.Contains) is presence-required, so an absent UserId is NoMatch (not the naive !inner Match).
    [Fact]
    public void UserIdNotContains_OverNullUserId_IsNoMatch()
    {
        AssertBothBackends(
            "UserId != null && !UserId.Value.Contains(\"S-1-5-99\", StringComparison.OrdinalIgnoreCase)",
            FilterTestFixtures.NoNullables,
            FilterMatch.NoMatch);
    }

    // UserId '!=' on an absent UserId is NoMatch (presence-required), the asymmetry counterpart to ProcessId != 5.
    [Fact]
    public void UserIdNotEquals_OverNullUserId_IsNoMatch()
    {
        AssertBothBackends(
            "UserId != null && UserId.Value != \"S-1-5-18\"",
            FilterTestFixtures.NoNullables,
            FilterMatch.NoMatch);
    }

    private static void AssertBothBackends(string expression, ResolvedEvent resolvedEvent, FilterMatch expected)
    {
        Assert.True(
            FilterParser.TryCompile(expression, out CompiledFilter? row, out var rowError),
            $"Row backend failed to compile '{expression}': {rowError}");
        Assert.True(
            FilterParser.TryCompileColumn(expression, out ColumnCompiledFilter? column, out var columnError),
            $"Column backend failed to compile '{expression}': {columnError}");

        var reader = new LegacyEventColumnReader(EventLogId.Create(), generation: 1, contentVersion: 1, [resolvedEvent]);

        Assert.Equal(expected, Oracle(row, resolvedEvent));
        Assert.Equal(expected, column.Evaluate(reader, reader.LocatorAt(0)));
    }

    private static FilterMatch Oracle(CompiledFilter compiled, ResolvedEvent resolvedEvent) =>
        compiled.Evaluate?.Invoke(resolvedEvent) ?? (compiled.Predicate(resolvedEvent) ? FilterMatch.Match : FilterMatch.NoMatch);
}
