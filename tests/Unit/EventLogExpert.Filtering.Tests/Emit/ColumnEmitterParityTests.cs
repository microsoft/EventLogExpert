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

        // Multi-select honoring the operator over scalar strings: Is Any Of / Contains Any / Is None Of / Contains
        // None, including presence-required UserId (corpus[1] NoNullables has a null UserId) and Opcode.
        "(new[] {\"Test\", \"Other\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))",
        "!(new[] {\"TestSource\"}).Contains(Source)",
        "!(new[] {\"Test\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))",
        "(new[] {\"Error\", \"Critical\"}).Any(e => Level.Contains(e, StringComparison.OrdinalIgnoreCase))",
        "!(new[] {\"Information\"}).Contains(Level)",
        "!(new[] {\"Start\"}).Any(e => Opcode.Contains(e, StringComparison.OrdinalIgnoreCase))",
        "(new[] {\"S-1-5-18\", \"S-1-5-19\"}).Any(e => UserId.Contains(e, StringComparison.OrdinalIgnoreCase))",
        "!(new[] {\"S-1-5-99\"}).Contains(UserId)",
        "!(new[] {\"S-1-5-99\"}).Any(e => UserId.Contains(e, StringComparison.OrdinalIgnoreCase))",

        // EventData exact-name (presence-required, all ops; typed coercion; duplicate-name first-wins; absent name).
        "EventData[\"User\"] == \"admin\"",
        "EventData[\"User\"] != \"admin\"",
        "EventData[\"User\"] != \"other\"",
        "EventData[\"Code\"] == \"5\"",
        "EventData[\"Flag\"] == \"true\"",
        "EventData[\"User\"].Contains(\"adm\", StringComparison.OrdinalIgnoreCase)",
        "!EventData[\"User\"].Contains(\"xyz\")",
        "(new[] {\"admin\", \"root\"}).Contains(EventData[\"User\"])",
        "(new[] {\"adm\", \"zzz\"}).Any(e => EventData[\"User\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
        "(new[] {\"zzz\", \"dmi\"}).Any(e => EventData[\"User\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
        "(new[] {\"zzz\", \"nomatch\"}).Any(e => EventData[\"User\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
        "EventData[\"Dup\"] == \"first\"",
        "EventData[\"Missing\"] == \"x\"",
        "EventData[\"Missing\"] != \"x\"",

        // UserData exact-path (tri-state via the shared UserDataMatch core): present, truncated, incomplete, absent, nested.
        "UserData[\"Foo\"] == \"adminvalue\"",
        "UserData[\"Foo\"] != \"adminvalue\"",
        "UserData[\"Foo\"].Contains(\"admin\", StringComparison.OrdinalIgnoreCase)",
        "!UserData[\"Foo\"].Contains(\"zzz\")",
        "(new[] {\"adminvalue\", \"root\"}).Contains(UserData[\"Foo\"])",
        "(new[] {\"admin\", \"zzz\"}).Any(e => UserData[\"Foo\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
        "(new[] {\"zzz\", \"minval\"}).Any(e => UserData[\"Foo\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
        "(new[] {\"zzz\", \"nomatch\"}).Any(e => UserData[\"Trunc\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
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
        "(new[] {\"nomatch\", \"dmin\"}).Any(e => EventData[\"Us*\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
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
        "(new[] {\"zzz\", \"admin\"}).Any(e => UserData[\"*oo\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
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

    // Absolute (not differential) oracle for EventData Contains-Any: first-needle match, second-needle match,
    // present-but-no-needle NoMatch, and an ABSENT named field NoMatch (presence-required). Also pins the wildcard
    // field-name path. Exact results on BOTH backends catch a same-direction emitter bug the differential battery can't.
    [Fact]
    public void EventDataContainsAny_Absolute_MatchesAnyNeedleAndRequiresPresence()
    {
        const string firstNeedle =
            "(new[] {\"adm\", \"zzz\"}).Any(e => EventData[\"User\"].Contains(e, StringComparison.OrdinalIgnoreCase))";
        const string secondNeedle =
            "(new[] {\"zzz\", \"dmi\"}).Any(e => EventData[\"User\"].Contains(e, StringComparison.OrdinalIgnoreCase))";
        const string noNeedle =
            "(new[] {\"zzz\", \"nomatch\"}).Any(e => EventData[\"User\"].Contains(e, StringComparison.OrdinalIgnoreCase))";

        AssertBothBackends(firstNeedle, s_eventDataRich, FilterMatch.Match);
        AssertBothBackends(secondNeedle, s_eventDataRich, FilterMatch.Match);
        AssertBothBackends(noNeedle, s_eventDataRich, FilterMatch.NoMatch);

        // "User" is absent on s_eventDataOther (only "Other"): presence-required Contains-Any is NoMatch.
        AssertBothBackends(firstNeedle, s_eventDataOther, FilterMatch.NoMatch);

        // Wildcard field name resolves to the matching field ("Us*" -> "User"): match and no-match both pinned.
        AssertBothBackends(
            "(new[] {\"nomatch\", \"dmin\"}).Any(e => EventData[\"Us*\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_eventDataRich,
            FilterMatch.Match);
        AssertBothBackends(
            "(new[] {\"nomatch\", \"zzz\"}).Any(e => EventData[\"Us*\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_eventDataRich,
            FilterMatch.NoMatch);

        // A wildcard that matches ZERO fields is presence-required -> NoMatch (distinct from a present-but-non-matching field).
        AssertBothBackends(
            "(new[] {\"admin\"}).Any(e => EventData[\"*zzz*\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_eventDataRich,
            FilterMatch.NoMatch);

        // Duplicate field name is first-wins ("first", not "second"): the first value's needle matches; a second-only needle does not.
        AssertBothBackends(
            "(new[] {\"first\", \"zzz\"}).Any(e => EventData[\"Dup\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_eventDataRich,
            FilterMatch.Match);
        AssertBothBackends(
            "(new[] {\"second\", \"zzz\"}).Any(e => EventData[\"Dup\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_eventDataRich,
            FilterMatch.NoMatch);

        // A typed EventData value (Code = 5L) is coerced to string for the substring test.
        AssertBothBackends(
            "(new[] {\"5\", \"zzz\"}).Any(e => EventData[\"Code\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_eventDataRich,
            FilterMatch.Match);
    }

    // MultiContains on Xml is reachable only via Advanced text (Xml is text-only in Basic), but the compiled filter
    // must still flag RequiresXml on both backends so the columnar reader materializes Xml before evaluation.
    [Fact]
    public void MultiContains_OnXml_FlagsRequiresXmlOnBothBackends()
    {
        const string expression = "(new[] {\"data\"}).Any(e => Xml.Contains(e, StringComparison.OrdinalIgnoreCase))";

        Assert.True(FilterParser.TryCompile(expression, out CompiledFilter? row, out var rowError), rowError);
        Assert.True(row.RequiresXml);

        Assert.True(
            FilterParser.TryCompileColumn(expression, out ColumnCompiledFilter? column, out var columnError),
            columnError);
        Assert.True(column.RequiresXml);
    }

    // Presence-required UserId: an absent UserId is NoMatch for ALL FOUR Many operators on BOTH backends. Locks the
    // negation-presence invariant so "is none of" / "contains none" don't wrongly match a missing UserId.
    [Fact]
    public void MultiSelectOperators_AbsentUserId_IsNoMatchForAllFourOperators()
    {
        ResolvedEvent absentUserId = new("TestLog", LogPathType.Channel) { Id = 1, UserId = null };

        AssertBothBackends("(new[] {\"S-1-5-18\"}).Contains(UserId)", absentUserId, FilterMatch.NoMatch);
        AssertBothBackends(
            "(new[] {\"S-1-5\"}).Any(e => UserId.Contains(e, StringComparison.OrdinalIgnoreCase))",
            absentUserId,
            FilterMatch.NoMatch);
        AssertBothBackends("!(new[] {\"S-1-5-18\"}).Contains(UserId)", absentUserId, FilterMatch.NoMatch);
        AssertBothBackends(
            "!(new[] {\"S-1-5\"}).Any(e => UserId.Contains(e, StringComparison.OrdinalIgnoreCase))",
            absentUserId,
            FilterMatch.NoMatch);
    }

    // Multi-select honoring the operator over a scalar string field: exact (not differential) results pin the four Many
    // shapes (Is Any Of / Contains Any / Is None Of / Contains None) on BOTH backends.
    [Fact]
    public void MultiSelectOperators_Absolute_OverScalarString()
    {
        ResolvedEvent testSource = new("TestLog", LogPathType.Channel) { Id = 1, Source = "TestSource" };
        ResolvedEvent otherSource = new("TestLog", LogPathType.Channel) { Id = 2, Source = "OtherSource" };

        AssertBothBackends("(new[] {\"TestSource\", \"Foo\"}).Contains(Source)", testSource, FilterMatch.Match);
        AssertBothBackends("(new[] {\"TestSource\", \"Foo\"}).Contains(Source)", otherSource, FilterMatch.NoMatch);
        AssertBothBackends(
            "(new[] {\"Test\", \"Foo\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))",
            testSource,
            FilterMatch.Match);
        AssertBothBackends(
            "(new[] {\"Test\", \"Foo\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))",
            otherSource,
            FilterMatch.NoMatch);
        AssertBothBackends("!(new[] {\"TestSource\"}).Contains(Source)", testSource, FilterMatch.NoMatch);
        AssertBothBackends("!(new[] {\"TestSource\"}).Contains(Source)", otherSource, FilterMatch.Match);
        AssertBothBackends(
            "!(new[] {\"Test\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))",
            testSource,
            FilterMatch.NoMatch);
        AssertBothBackends(
            "!(new[] {\"Test\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))",
            otherSource,
            FilterMatch.Match);

        // Second-element match: the "any of" must scan the whole list, not just element 0.
        AssertBothBackends("(new[] {\"Nope\", \"OtherSource\"}).Contains(Source)", otherSource, FilterMatch.Match);
        AssertBothBackends(
            "(new[] {\"Nope\", \"Other\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))",
            otherSource,
            FilterMatch.Match);
        AssertBothBackends("!(new[] {\"Nope\", \"OtherSource\"}).Contains(Source)", otherSource, FilterMatch.NoMatch);
        AssertBothBackends(
            "!(new[] {\"Nope\", \"Other\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))",
            otherSource,
            FilterMatch.NoMatch);
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

    // Absolute oracle for UserData Contains-Any (tri-state via the shared UserDataMatch.MultiContains core): needle match,
    // second-needle match, complete-present no-match (NoMatch), an absent path (decisive NoMatch), and a TRUNCATED
    // non-match (Unknown - the elided tail could contain a needle). Pins the tri-state on BOTH backends so a
    // Unknown->NoMatch regression in the shared core cannot pass the differential battery. Also pins the wildcard path.
    [Fact]
    public void UserDataContainsAny_Absolute_TriStateAcrossPresentAbsentTruncated()
    {
        AssertBothBackends(
            "(new[] {\"admin\", \"zzz\"}).Any(e => UserData[\"Foo\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_userDataPresentTruncated,
            FilterMatch.Match);
        AssertBothBackends(
            "(new[] {\"zzz\", \"minval\"}).Any(e => UserData[\"Foo\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_userDataPresentTruncated,
            FilterMatch.Match);
        AssertBothBackends(
            "(new[] {\"zzz\", \"nomatch\"}).Any(e => UserData[\"Foo\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_userDataPresentTruncated,
            FilterMatch.NoMatch);

        // "Absent" is not present and the event is not UserDataIncomplete: decisive absent -> NoMatch.
        AssertBothBackends(
            "(new[] {\"admin\"}).Any(e => UserData[\"Absent\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_userDataPresentTruncated,
            FilterMatch.NoMatch);

        // "Trunc" is truncated and no visible needle matches -> Unknown (keep-visible; the hidden tail could match).
        AssertBothBackends(
            "(new[] {\"zzz\", \"nomatch\"}).Any(e => UserData[\"Trunc\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_userDataPresentTruncated,
            FilterMatch.Unknown);

        // Wildcard path resolves to "Foo": match pinned.
        AssertBothBackends(
            "(new[] {\"admin\"}).Any(e => UserData[\"*oo\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_userDataPresentTruncated,
            FilterMatch.Match);

        // An ABSENT path on an INCOMPLETE UserData event cannot be decided absent -> Unknown (keep-visible), unlike the
        // decisive NoMatch an absent path yields on a complete event.
        AssertBothBackends(
            "(new[] {\"anything\"}).Any(e => UserData[\"Absent\"].Contains(e, StringComparison.OrdinalIgnoreCase))",
            s_userDataIncompleteEvent,
            FilterMatch.Unknown);
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
