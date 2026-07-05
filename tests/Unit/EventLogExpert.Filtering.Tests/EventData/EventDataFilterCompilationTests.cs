// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Filtering.Parsing;
using System.Globalization;

namespace EventLogExpert.Filtering.Tests.EventData;

/// <summary>
///     End-to-end compile + evaluate coverage for EventData named-field filtering: the presence-required semantics
///     (R1″), the typed value-equality model (R4), and the closed-vocabulary rejections.
/// </summary>
public sealed class EventDataFilterCompilationTests
{
    private static readonly Guid s_sampleGuid = new("11111111-2222-3333-4444-555555555555");

    // --- Presence-required (R1″): absent field / EventDataKind.None never matches, positive OR negative. ---

    public static IEnumerable<object[]> AllOperators() =>
    [
        ["EventData[\"User\"] == \"admin\""],
        ["EventData[\"User\"] != \"admin\""],
        ["EventData[\"User\"].Contains(\"adm\", StringComparison.OrdinalIgnoreCase)"],
        ["!EventData[\"User\"].Contains(\"adm\", StringComparison.OrdinalIgnoreCase)"],
        ["(new[] {\"admin\", \"root\"}).Contains(EventData[\"User\"])"]
    ];

    [Theory]
    [MemberData(nameof(AllOperators))]
    public void AbsentField_NeverMatches(string filter)
    {
        var predicate = Compile(filter);

        // Event has EventData, but not the "User" field.
        Assert.False(predicate(Event(("Other", "x"))));
    }

    [Fact]
    public void AnyOf_MatchesAnyListedValue()
    {
        var predicate = Compile("(new[] {\"admin\", \"root\"}).Contains(EventData[\"User\"])");

        Assert.True(predicate(Event(("User", "admin"))));
        Assert.True(predicate(Event(("User", "root"))));
        Assert.False(predicate(Event(("User", "guest"))));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    public void BooleanEquality_IsCaseInsensitive(string literal)
    {
        var predicate = Compile($"EventData[\"Flag\"] == \"{literal}\"");

        Assert.True(predicate(Event(("Flag", true))));
        Assert.False(predicate(Event(("Flag", false))));
    }

    [Fact]
    public void BooleanField_DoesNotMatchNumericLiteral()
    {
        var predicate = Compile("EventData[\"Flag\"] == \"1\"");

        Assert.False(predicate(Event(("Flag", true))));
    }

    [Fact]
    public void Contains_IsCaseInsensitiveSubstring()
    {
        var predicate = Compile("EventData[\"Path\"].Contains(\"system32\", StringComparison.OrdinalIgnoreCase)");

        Assert.True(predicate(Event(("Path", @"C:\Windows\System32\cmd.exe"))));
        Assert.False(predicate(Event(("Path", @"C:\Temp\x"))));
    }

    [Fact]
    public void DateTimeEquality_MatchesRoundTripString()
    {
        var when = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var predicate = Compile($"EventData[\"When\"] == \"{when.ToString("O", CultureInfo.InvariantCulture)}\"");

        Assert.True(predicate(Event(("When", when))));
        Assert.False(predicate(Event(("When", when.AddDays(1)))));
    }

    [Fact]
    public void DateTimeEquality_RejectsNonRoundTripLiteral()
    {
        // Only the canonical "O" round-trip form parses to a DateTime candidate; a lenient date string does not,
        // so it never matches a DateTime field (even the same instant).
        var predicate = Compile("EventData[\"When\"] == \"2024-01-01\"");

        Assert.False(predicate(Event(("When", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)))));
    }

    [Fact]
    public void DoubleEquality_MatchesNaN()
    {
        var predicate = Compile("EventData[\"Ratio\"] == \"NaN\"");

        Assert.True(predicate(Event(("Ratio", double.NaN))));
        Assert.False(predicate(Event(("Ratio", 1.5d))));
    }

    [Fact]
    public void DoubleNegatedContains_EqualsContains()
    {
        var contains = Compile("EventData[\"User\"].Contains(\"adm\", StringComparison.OrdinalIgnoreCase)");
        var doubleNegated = Compile("!!EventData[\"User\"].Contains(\"adm\", StringComparison.OrdinalIgnoreCase)");

        foreach (var evt in new[] { Event(("User", "admin")), Event(("User", "guest")), Event(("Other", "x")) })
        {
            Assert.Equal(contains(evt), doubleNegated(evt));
        }
    }

    [Fact]
    public void DuplicateFieldName_ResolvesToFirstOccurrence()
    {
        var predicate = Compile("EventData[\"Data\"] == \"first\"");

        Assert.True(predicate(Event(("Data", "first"), ("Data", "second"))));
    }

    // --- Negation normalization equivalence (R1″). ---

    [Theory]
    [InlineData("admin")]
    [InlineData("guest")]
    public void ExplicitNegatedEquality_EqualsNotEqual(string userValue)
    {
        var notEqual = Compile("EventData[\"User\"] != \"admin\"");
        var negatedEqual = Compile("!(EventData[\"User\"] == \"admin\")");

        var withField = Event(("User", userValue));
        var absent = Event(("Other", "x"));

        Assert.Equal(notEqual(withField), negatedEqual(withField));
        Assert.Equal(notEqual(absent), negatedEqual(absent));
    }

    [Fact]
    public void FieldNameKey_IsCaseSensitiveOrdinal()
    {
        var predicate = Compile("EventData[\"user\"] == \"admin\"");

        // Field is named "User"; the Ordinal schema lookup does not match the lowercase key.
        Assert.False(predicate(Event(("User", "admin"))));
    }

    [Fact]
    public void GroupedNegation_WithoutEventData_StillCompiles()
    {
        // Regression guard: negating a group of regular fields is unaffected by the EventData rejection.
        Assert.True(FilterParser.TryCompile("!(Source == \"a\" || Level == \"b\")", out _, out var error), error);
    }

    [Theory]
    [InlineData("11111111-2222-3333-4444-555555555555")]
    [InlineData("{11111111-2222-3333-4444-555555555555}")]
    public void GuidEquality_AcceptsAnyParseableFormat(string literal)
    {
        var predicate = Compile($"EventData[\"Id\"] == \"{literal}\"");

        Assert.True(predicate(Event(("Id", s_sampleGuid))));
        Assert.False(predicate(Event(("Id", Guid.Empty))));
    }

    [Fact]
    public void Int64Equality_MatchesNegativeValue()
    {
        var predicate = Compile("EventData[\"Delta\"] == \"-5\"");

        Assert.True(predicate(Event(("Delta", -5L))));
        Assert.False(predicate(Event(("Delta", 5L))));
    }

    [Theory]
    [MemberData(nameof(AllOperators))]
    public void NoneEventData_NeverMatches(string filter)
    {
        var predicate = Compile(filter);

        Assert.Equal(EventDataKind.None, NoEventData().EventData.Kind);
        Assert.False(predicate(NoEventData()));
    }

    [Fact]
    public void NotContains_RequiresPresence()
    {
        var predicate = Compile("!EventData[\"User\"].Contains(\"adm\", StringComparison.OrdinalIgnoreCase)");

        Assert.True(predicate(Event(("User", "guest"))));
        Assert.False(predicate(Event(("User", "admin"))));
        Assert.False(predicate(Event(("Other", "x")))); // absent -> no match
    }

    [Fact]
    public void NotEqual_RequiresPresence()
    {
        var predicate = Compile("EventData[\"User\"] != \"admin\"");

        Assert.True(predicate(Event(("User", "guest")))); // present and different
        Assert.False(predicate(Event(("User", "admin")))); // present and equal
        Assert.False(predicate(Event(("Other", "x")))); // absent -> no match (not "true")
    }

    [Fact]
    public void SingleEquality_Matches()
    {
        var predicate = Compile("EventData[\"Ratio\"] == \"1.5\"");

        Assert.True(predicate(Event(("Ratio", 1.5f))));
        Assert.False(predicate(Event(("Ratio", 2.5f))));
    }

    [Fact]
    public void StringEquality_MatchesOnlyExactValue()
    {
        var predicate = Compile("EventData[\"User\"] == \"admin\"");

        Assert.True(predicate(Event(("User", "admin"))));
        Assert.False(predicate(Event(("User", "guest"))));
    }

    // --- Key handling. ---

    [Fact]
    public void Target_IsCaseInsensitive()
    {
        var predicate = Compile("eventdata[\"User\"] == \"admin\"");

        Assert.True(predicate(Event(("User", "admin"))));
    }

    [Fact]
    public void TypedEquality_IsScopedToTheFieldKind()
    {
        // "05" matches an Int64 5 by value, but a String field "5" is compared as text and does not equal "05".
        var predicate = Compile("EventData[\"Code\"] == \"05\"");

        Assert.True(predicate(Event(("Code", 5L))));
        Assert.False(predicate(Event(("Code", "5"))));
    }

    [Theory]
    [InlineData("EventData[\"Code\"] == \"5\"")]
    [InlineData("EventData[\"Code\"] == \"05\"")] // typed value equality: "05" parses to the numeric 5
    public void TypedIntegerEquality_IgnoresLeadingZeros(string filter)
    {
        var predicate = Compile(filter);

        Assert.True(predicate(Event(("Code", 5L))));
        Assert.False(predicate(Event(("Code", 6L))));
    }

    [Fact]
    public void UInt64Equality_MatchesValuesAboveInt64Max()
    {
        var predicate = Compile($"EventData[\"Mask\"] == \"{ulong.MaxValue}\"");

        Assert.True(predicate(Event(("Mask", ulong.MaxValue))));
        Assert.False(predicate(Event(("Mask", ulong.MaxValue - 1))));
    }

    // --- Rejections (closed vocabulary). ---

    [Theory]
    [InlineData("EventData[\"Code\"] < \"5\"")] // relational not supported
    [InlineData("EventData[\"Code\"] >= \"5\"")]
    [InlineData("EventData[\"\"] == \"x\"")] // empty key
    [InlineData("EventData[\"   \"] == \"x\"")] // whitespace key
    [InlineData("EventData[5] == \"x\"")] // non-string key
    [InlineData("!(new[] {\"a\", \"b\"}).Contains(EventData[\"User\"])")] // none-of
    [InlineData("!(EventData[\"User\"] == \"a\" || EventData[\"User\"] == \"b\")")] // grouped negation
    [InlineData("!(EventData[\"User\"] == \"a\" && Source == \"x\")")] // grouped negation, mixed
    public void UnsupportedShapes_FailToCompile(string filter)
    {
        Assert.False(FilterParser.TryCompile(filter, out var compiled, out var error));
        Assert.Null(compiled);
        Assert.False(string.IsNullOrEmpty(error));
    }

    private static Func<ResolvedEvent, bool> Compile(string filter)
    {
        Assert.True(FilterParser.TryCompile(filter, out var compiled, out var error), error);

        return compiled.Predicate;
    }

    private static ResolvedEvent Event(params (string Name, object? Value)[] fields) =>
        EventDataTestFactory.CreateEventWithData(fields);

    private static ResolvedEvent NoEventData() => new("TestLog", LogPathType.Channel);
}
