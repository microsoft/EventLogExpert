// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Parsing;
using EventLogExpert.Filtering.Tests.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;
using System.Reflection;

namespace EventLogExpert.Filtering.Tests.Emit;

public sealed class EmitterParityTests
{
    private static readonly IReadOnlyDictionary<string, string> s_filterByName = BuildFilterLookup();

    public static IEnumerable<object[]> SnapshotRows() =>
        EmitterParitySnapshot.Rows.Select(row => new object[]
        {
            row.FilterName,
            row.EventIndex,
            row.ExpectedPredicate,
            row.ExpectedRequiresXml
        });

    [Theory]
    [MemberData(nameof(SnapshotRows))]
    public void TryCompile_WhenAppliedToFixture_MatchesGoldenSnapshotPredicateAndRequiresXmlFlag(
        string filterName,
        int eventIndex,
        bool expectedPredicate,
        bool expectedRequiresXml)
    {
        var filter = s_filterByName[filterName];
        var emitterOk = FilterParser.TryCompile(filter, out var emitterCompiled, out var emitterError);

        Assert.True(emitterOk, $"Emitter failed to compile '{filterName}' ({filter}): {emitterError}");

        var evt = FilterTestFixtures.All[eventIndex];
        var emitterResult = emitterCompiled!.Predicate(evt);

        Assert.Equal(expectedPredicate, emitterResult);
        Assert.Equal(expectedRequiresXml, emitterCompiled.RequiresXml);
    }

    [Fact]
    public void TryCompile_WhenExpressionFailsLowering_PropagatesDiagnostic()
    {
        var ok = FilterParser.TryCompile("Source.StartsWith(\"X\")", out var compiled, out var error);

        Assert.False(ok);
        Assert.Null(compiled);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryCompile_WhenExpressionFailsToTokenize_PropagatesDiagnostic()
    {
        var ok = FilterParser.TryCompile("Source == \"unterminated", out var compiled, out var error);

        Assert.False(ok);
        Assert.Null(compiled);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryCompile_WhenExpressionIsEmpty_ReturnsFalseWithDiagnostic()
    {
        var ok = FilterParser.TryCompile(string.Empty, out var compiled, out var error);

        Assert.False(ok);
        Assert.Null(compiled);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryCompile_WhenExpressionIsNull_ReturnsFalseWithDiagnostic()
    {
        var ok = FilterParser.TryCompile(null, out var compiled, out var error);

        Assert.False(ok);
        Assert.Null(compiled);
        Assert.NotNull(error);
    }

    private static IReadOnlyDictionary<string, string> BuildFilterLookup()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in typeof(FilterTestConstants).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType == typeof(string) && field.GetValue(null) is string value)
            {
                dict[field.Name] = value;
            }
        }

        return dict;
    }
}
