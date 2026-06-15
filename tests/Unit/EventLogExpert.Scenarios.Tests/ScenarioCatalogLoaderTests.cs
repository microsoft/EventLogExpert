// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Serialization;
using System.Text;

namespace EventLogExpert.Scenarios.Tests;

public sealed class ScenarioCatalogLoaderTests
{
    [Fact]
    public void Load_AdminChannelWithoutRequiresAdmin_IsError()
    {
        var result = Load(Wrap("""
            { "id": "x", "name": "X", "purpose": "p", "group": "Security",
              "channels": [ "Security" ],
              "filters": [ { "comparison": { "property": "Id", "value": "4624" } } ] }
            """));

        Assert.Contains(result.Errors, error => error.Contains("requiresAdmin"));
    }

    [Fact]
    public void Load_AggregatesEveryError()
    {
        var result = Load(Wrap("""
            { "id": "", "name": "", "purpose": "", "group": "SystemHealth", "channels": [ "System" ],
              "filters": [ { "comparison": { "property": "Id", "value": "1" } } ] }
            """));

        Assert.True(result.Errors.Count >= 3, $"Expected >= 3 errors, got: {string.Join("; ", result.Errors)}");
    }

    [Theory]
    [InlineData("\"property\": \"LogNam\"")]
    [InlineData("\"property\": \"Log Name\"")]
    [InlineData("\"property\": \"Id\", \"operator\": \"Not Equal\"")]
    [InlineData("\"property\": \"Id\", \"matchMode\": \"Multi\"")]
    public void Load_BadEnumToken_IsError(string fragment)
    {
        var result = Load(Wrap($$"""
            { "id": "x", "name": "X", "purpose": "p", "group": "SystemHealth",
              "channels": [ "System" ],
              "filters": [ { "comparison": { {{fragment}}, "value": "1" } } ] }
            """));

        Assert.NotEmpty(result.Errors);
        Assert.Empty(result.Scenarios);
    }

    [Fact]
    public void Load_BadGroupToken_IsError()
    {
        var result = Load(Wrap("""
            { "id": "x", "name": "X", "purpose": "p", "group": "NotARealGroup",
              "channels": [ "System" ],
              "filters": [ { "comparison": { "property": "Id", "value": "1" } } ] }
            """));

        Assert.Contains(result.Errors, error => error.Contains("group"));
    }

    [Theory]
    [InlineData("\"id\": \"\"", "missing id")]
    [InlineData("\"id\": \"Not Kebab\"", "kebab")]
    public void Load_BadId_IsError(string idFragment, string expected)
    {
        var result = Load(Wrap($$"""
            { {{idFragment}}, "name": "X", "purpose": "p", "group": "SystemHealth",
              "channels": [ "System" ],
              "filters": [ { "comparison": { "property": "Id", "value": "1" } } ] }
            """));

        Assert.Contains(result.Errors, error => error.Contains(expected));
    }

    [Fact]
    public void Load_CombinedScenarioNotLogNameScoped_IsError()
    {
        var result = Load(Wrap("""
            { "id": "x", "name": "X", "purpose": "p", "group": "SystemHealth", "requiresAdmin": true,
              "channels": [ "System", "Security" ],
              "filters": [ { "comparison": { "property": "Id", "value": "1" } } ] }
            """));

        Assert.Contains(result.Errors, error => error.Contains("LogName-scoped"));
    }

    [Fact]
    public void Load_CommentsAndTrailingCommas_AreTolerated()
    {
        var result = Load("""
            {
              "schemaVersion": 1,
              // a leading comment
              "scenarios": [
                { "id": "x", "name": "X", "purpose": "p", "group": "SystemHealth",
                  "channels": [ "System" ],
                  "filters": [ { "comparison": { "property": "Id", "value": "1000" } } ], }
              ],
            }
            """);

        Assert.Empty(result.Errors);
        Assert.Single(result.Scenarios);
    }

    [Fact]
    public void Load_DuplicateId_IsError()
    {
        var result = Load("""
            { "schemaVersion": 1, "scenarios": [
              { "id": "dup", "name": "A", "purpose": "p", "group": "SystemHealth", "channels": ["System"],
                "filters": [ { "comparison": { "property": "Id", "value": "1" } } ] },
              { "id": "dup", "name": "B", "purpose": "p", "group": "SystemHealth", "channels": ["System"],
                "filters": [ { "comparison": { "property": "Id", "value": "2" } } ] }
            ] }
            """);

        Assert.Contains(result.Errors, error => error.Contains("duplicate id"));
    }

    [Fact]
    public void Load_MalformedJson_IsAggregatedError_NotThrow()
    {
        var result = Load("{ this is not json");

        Assert.Empty(result.Scenarios);
        Assert.Contains(result.Errors, error => error.Contains("invalid JSON"));
    }

    [Fact]
    public void Load_NullScenarioElement_IsAggregatedError_NotThrow()
    {
        var result = Load("""
            { "schemaVersion": 1, "scenarios": [ null ] }
            """);

        Assert.Empty(result.Scenarios);
        Assert.Contains(result.Errors, error => error.Contains("scenario is null"));
    }

    [Fact]
    public void Load_NullFilterRowElement_IsAggregatedError_NotThrow()
    {
        var result = Load(Wrap("""
            { "id": "x", "name": "X", "purpose": "p", "group": "SystemHealth",
              "channels": [ "System" ],
              "filters": [ null ] }
            """));

        Assert.Empty(result.Scenarios);
        Assert.Contains(result.Errors, error => error.Contains("filter row is null"));
    }

    [Fact]
    public void Load_NullPredicateElement_IsAggregatedError_NotThrow()
    {
        var result = Load(Wrap("""
            { "id": "x", "name": "X", "purpose": "p", "group": "SystemHealth",
              "channels": [ "System" ],
              "filters": [ { "comparison": { "property": "Id", "value": "1" }, "predicates": [ null ] } ] }
            """));

        Assert.Empty(result.Scenarios);
        Assert.Contains(result.Errors, error => error.Contains("predicate is null"));
    }

    [Fact]
    public void Load_MinimalValidScenario_Succeeds()
    {
        var result = Load(Wrap("""
            { "id": "x", "name": "X", "purpose": "p", "group": "SystemHealth",
              "channels": [ "System" ],
              "filters": [ { "comparison": { "property": "Id", "value": "1000" } } ] }
            """));

        Assert.Empty(result.Errors);
        Assert.Single(result.Scenarios);
        Assert.Equal("x", result.Scenarios[0].Id);
    }

    [Fact]
    public void Load_MissingChannels_IsError()
    {
        var result = Load(Wrap("""
            { "id": "x", "name": "X", "purpose": "p", "group": "SystemHealth",
              "filters": [ { "comparison": { "property": "Id", "value": "1" } } ] }
            """));

        Assert.Contains(result.Errors, error => error.Contains("channels"));
    }

    [Fact]
    public void Load_MissingFilters_IsError()
    {
        var result = Load(Wrap("""
            { "id": "x", "name": "X", "purpose": "p", "group": "SystemHealth",
              "channels": [ "System" ], "filterRows": [ { "comparison": { "property": "Id", "value": "1" } } ] }
            """));

        Assert.Empty(result.Scenarios);
        Assert.Contains(result.Errors, error => error.Contains("at least one filter row"));
    }

    [Fact]
    public void Load_SourceRegistrationGating_IsRejected()
    {
        var result = Load(Wrap("""
            { "id": "x", "name": "X", "purpose": "p", "group": "Applications", "gating": "SourceRegistration",
              "channels": [ "Application" ],
              "filters": [ { "comparison": { "property": "Id", "value": "1" } } ] }
            """));

        Assert.Contains(result.Errors, error => error.Contains("SourceRegistration"));
    }

    [Fact]
    public void Load_UnknownMembers_AreIgnored()
    {
        var result = Load(Wrap("""
            { "id": "x", "name": "X", "purpose": "p", "group": "SystemHealth",
              "channels": [ "System" ], "futureField": { "nested": true }, "anotherUnknown": 5,
              "filters": [ { "comparison": { "property": "Id", "value": "1000" } } ] }
            """));

        Assert.Empty(result.Errors);
        Assert.Single(result.Scenarios);
    }

    [Fact]
    public void Load_UnsupportedSchemaVersion_IsError()
    {
        var result = Load("""{ "schemaVersion": 999, "scenarios": [] }""");

        Assert.Empty(result.Scenarios);
        Assert.Contains(result.Errors, error => error.Contains("schemaVersion"));
    }

    private static ScenarioCatalogLoadResult Load(string json) =>
        ScenarioCatalogLoader.TryLoad([("test.json", Encoding.UTF8.GetBytes(json))]);

    private static string Wrap(string scenarioJson) =>
        $$"""{ "schemaVersion": 1, "scenarios": [ {{scenarioJson}} ] }""";
}
