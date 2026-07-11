// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Filtering.Tests.Basic;

public sealed class BasicFilterFormatterTests
{
    [Fact]
    public void TryFormat_WhenLenientAndAnySubFilterInvalid_ShouldSkipInvalidAndReturnTrue()
    {
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "   "
                    },
                    false)
            ]);

        var result = BasicFilterFormatter.TryFormat(source, out var formatted);

        Assert.True(result);
        Assert.Equal("Id == 100", formatted);
    }

    [Fact]
    public void TryFormat_WhenStrictSubFiltersAndAllSubFiltersValid_ShouldReturnTrue()
    {
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    false)
            ]);

        var result = BasicFilterFormatter.TryFormat(source, true, out var formatted);

        Assert.True(result);
        Assert.Equal("Id == 100 && Level == \"Error\"", formatted);
    }

    [Fact]
    public void TryFormat_WhenStrictSubFiltersAndAnySubFilterInvalid_ShouldReturnFalse()
    {
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "   "
                    },
                    false)
            ]);

        var result = BasicFilterFormatter.TryFormat(source, true, out var formatted);

        Assert.False(result);
        Assert.Equal(string.Empty, formatted);
    }

    [Fact]
    public void TryFormat_WhenStrictSubFiltersAndNoSubFilters_ShouldBehaveSameAsLenient()
    {
        var source = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Source,
                Operator = ComparisonOperator.Contains,
                MatchMode = MatchMode.Single,
                Value = "Kernel"
            },
            ImmutableList<FilterPredicate>.Empty);

        var strictResult = BasicFilterFormatter.TryFormat(source, true, out var strict);
        var lenientResult = BasicFilterFormatter.TryFormat(source, out var lenient);

        Assert.True(strictResult);
        Assert.True(lenientResult);
        Assert.Equal(strict, lenient);
    }

    // Full characterization of the formatter's LINQ output: every operator (==, !=, Contains, !Contains) in Single and
    // Many across each distinct field shape (scalar string, bare-integer numeric, Guid, presence-required UserId,
    // Keywords collection, EventData indexer). Pins the exact canonical text so any pattern drift is caught.
    [Theory]
    // Scalar string (Source)
    [InlineData(EventProperty.Source, ComparisonOperator.Equals, false, "Source == \"Val\"")]
    [InlineData(EventProperty.Source, ComparisonOperator.NotEqual, false, "Source != \"Val\"")]
    [InlineData(EventProperty.Source, ComparisonOperator.Contains, false, "Source.Contains(\"Val\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.Source, ComparisonOperator.NotContains, false, "!Source.Contains(\"Val\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.Source, ComparisonOperator.Equals, true, "(new[] {\"Aaa\", \"Bbb\"}).Contains(Source)")]
    [InlineData(EventProperty.Source, ComparisonOperator.NotEqual, true, "!(new[] {\"Aaa\", \"Bbb\"}).Contains(Source)")]
    [InlineData(EventProperty.Source, ComparisonOperator.Contains, true, "(new[] {\"Aaa\", \"Bbb\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))")]
    [InlineData(EventProperty.Source, ComparisonOperator.NotContains, true, "!(new[] {\"Aaa\", \"Bbb\"}).Any(e => Source.Contains(e, StringComparison.OrdinalIgnoreCase))")]
    // Numeric (Id): bare-integer ==/!=, ToString-shorthand contains, equals-any
    [InlineData(EventProperty.Id, ComparisonOperator.Equals, false, "Id == 100")]
    [InlineData(EventProperty.Id, ComparisonOperator.NotEqual, false, "Id != 100")]
    [InlineData(EventProperty.Id, ComparisonOperator.Contains, false, "Id.Contains(\"100\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.Id, ComparisonOperator.NotContains, false, "!Id.Contains(\"100\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.Id, ComparisonOperator.Equals, true, "(new[] {\"Aaa\", \"Bbb\"}).Contains(Id)")]
    // Guid (ActivityId)
    [InlineData(EventProperty.ActivityId, ComparisonOperator.Equals, false, "ActivityId == \"Val\"")]
    [InlineData(EventProperty.ActivityId, ComparisonOperator.NotEqual, false, "ActivityId != \"Val\"")]
    [InlineData(EventProperty.ActivityId, ComparisonOperator.Equals, true, "(new[] {\"Aaa\", \"Bbb\"}).Contains(ActivityId)")]
    // UserId: presence-required single, operator-aware Many
    [InlineData(EventProperty.UserId, ComparisonOperator.Equals, false, "UserId != null && UserId.Value == \"Val\"")]
    [InlineData(EventProperty.UserId, ComparisonOperator.NotEqual, false, "UserId != null && UserId.Value != \"Val\"")]
    [InlineData(EventProperty.UserId, ComparisonOperator.Contains, false, "UserId != null && UserId.Value.Contains(\"Val\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.UserId, ComparisonOperator.NotContains, false, "UserId != null && !UserId.Value.Contains(\"Val\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.UserId, ComparisonOperator.Equals, true, "(new[] {\"Aaa\", \"Bbb\"}).Contains(UserId)")]
    [InlineData(EventProperty.UserId, ComparisonOperator.NotEqual, true, "!(new[] {\"Aaa\", \"Bbb\"}).Contains(UserId)")]
    [InlineData(EventProperty.UserId, ComparisonOperator.Contains, true, "(new[] {\"Aaa\", \"Bbb\"}).Any(e => UserId.Contains(e, StringComparison.OrdinalIgnoreCase))")]
    [InlineData(EventProperty.UserId, ComparisonOperator.NotContains, true, "!(new[] {\"Aaa\", \"Bbb\"}).Any(e => UserId.Contains(e, StringComparison.OrdinalIgnoreCase))")]
    // Keywords collection
    [InlineData(EventProperty.Keywords, ComparisonOperator.Equals, false, "Keywords.Any(e => string.Equals(e, \"Val\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData(EventProperty.Keywords, ComparisonOperator.NotEqual, false, "!Keywords.Any(e => string.Equals(e, \"Val\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData(EventProperty.Keywords, ComparisonOperator.Contains, false, "Keywords.Any(e => e.Contains(\"Val\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData(EventProperty.Keywords, ComparisonOperator.NotContains, false, "!Keywords.Any(e => e.Contains(\"Val\", StringComparison.OrdinalIgnoreCase))")]
    [InlineData(EventProperty.Keywords, ComparisonOperator.Equals, true, "Keywords.Any(e => (new[] {\"Aaa\", \"Bbb\"}).Contains(e))")]
    // EventData indexer
    [InlineData(EventProperty.EventData, ComparisonOperator.Equals, false, "EventData[\"Field\"] == \"Val\"")]
    [InlineData(EventProperty.EventData, ComparisonOperator.NotEqual, false, "EventData[\"Field\"] != \"Val\"")]
    [InlineData(EventProperty.EventData, ComparisonOperator.Contains, false, "EventData[\"Field\"].Contains(\"Val\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.EventData, ComparisonOperator.NotContains, false, "!EventData[\"Field\"].Contains(\"Val\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.EventData, ComparisonOperator.Equals, true, "(new[] {\"Aaa\", \"Bbb\"}).Contains(EventData[\"Field\"])")]
    [InlineData(EventProperty.EventData, ComparisonOperator.Contains, true, "(new[] {\"Aaa\", \"Bbb\"}).Any(e => EventData[\"Field\"].Contains(e, StringComparison.OrdinalIgnoreCase))")]
    // UserData indexer (distinct property-expression path; storage-key path preserved)
    [InlineData(EventProperty.UserData, ComparisonOperator.Equals, false, "UserData[\"Path/Sub\"] == \"Val\"")]
    [InlineData(EventProperty.UserData, ComparisonOperator.NotEqual, false, "UserData[\"Path/Sub\"] != \"Val\"")]
    [InlineData(EventProperty.UserData, ComparisonOperator.Contains, false, "UserData[\"Path/Sub\"].Contains(\"Val\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.UserData, ComparisonOperator.NotContains, false, "!UserData[\"Path/Sub\"].Contains(\"Val\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData(EventProperty.UserData, ComparisonOperator.Equals, true, "(new[] {\"Aaa\", \"Bbb\"}).Contains(UserData[\"Path/Sub\"])")]
    [InlineData(EventProperty.UserData, ComparisonOperator.Contains, true, "(new[] {\"Aaa\", \"Bbb\"}).Any(e => UserData[\"Path/Sub\"].Contains(e, StringComparison.OrdinalIgnoreCase))")]
    public void TryFormatComparison_FullOperatorMatrix_EmitsCanonicalLinq(
        EventProperty property,
        ComparisonOperator op,
        bool isMany,
        string expected)
    {
        var comparison = new FilterComparison
        {
            Property = property,
            Operator = op,
            MatchMode = isMany ? MatchMode.Many : MatchMode.Single,
            Value = isMany
                ? null
                : property is EventProperty.Id or EventProperty.ProcessId or EventProperty.ThreadId ? "100" : "Val",
            Values = isMany ? ImmutableList.Create("Aaa", "Bbb") : [],
            EventDataFieldName = property == EventProperty.EventData ? "Field" : null,
            UserDataFieldName = property == EventProperty.UserData ? "Path/Sub" : null
        };

        Assert.True(BasicFilterFormatter.TryFormatComparison(comparison, null, out var formatted));
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(EventProperty.Id, ComparisonOperator.Contains)]
    [InlineData(EventProperty.Id, ComparisonOperator.NotEqual)]
    [InlineData(EventProperty.ActivityId, ComparisonOperator.Contains)]
    [InlineData(EventProperty.Keywords, ComparisonOperator.Contains)]
    [InlineData(EventProperty.Keywords, ComparisonOperator.NotContains)]
    [InlineData(EventProperty.EventData, ComparisonOperator.NotEqual)]
    [InlineData(EventProperty.EventData, ComparisonOperator.NotContains)]
    [InlineData(EventProperty.UserData, ComparisonOperator.NotEqual)]
    [InlineData(EventProperty.UserData, ComparisonOperator.NotContains)]
    public void TryFormatComparison_NonEqualsManyOnUnsupportedField_IsRejected(
        EventProperty property,
        ComparisonOperator op)
    {
        var comparison = new FilterComparison
        {
            Property = property,
            Operator = op,
            MatchMode = MatchMode.Many,
            Values = ImmutableList.Create("Aaa", "Bbb"),
            EventDataFieldName = property == EventProperty.EventData ? "Field" : null,
            UserDataFieldName = property == EventProperty.UserData ? "Path/Sub" : null
        };

        Assert.False(BasicFilterFormatter.TryFormatComparison(comparison, null, out _));
    }
}
