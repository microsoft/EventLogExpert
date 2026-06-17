// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.Scenarios.Serialization;
using System.Text;

namespace EventLogExpert.Scenarios.Tests;

public sealed class ScenarioExporterTests
{
    [Fact]
    public void Export_AdvancedExpressionResolvedToBasic_RoundTrips()
    {
        // A row whose BasicFilter was produced from raw expression text (the Advanced->Basic path the UI uses).
        var basic = SavedFilter.TryCreate("Id == 1000 && Source == \"Application Error\"", mode: FilterMode.Basic)?.BasicFilter;
        Assert.NotNull(basic);

        ScenarioExportRow[] rows = [new(basic, false, HighlightColor.None), Row(HighlightColor.None, EventProperty.Id, "1001")];
        var result = ScenarioExporter.Export(rows, Meta("Application"));

        Assert.Empty(result.Warnings);
        Assert.Empty(ScenarioCatalogLoader.TryLoad([("x.json", Encoding.UTF8.GetBytes(result.Json))]).Errors);
    }

    [Fact]
    public void Export_ColorEmittedAsEnumName()
    {
        ScenarioExportRow[] rows = [Row(HighlightColor.DarkRed, EventProperty.Id, "1"), Row(HighlightColor.Green, EventProperty.Id, "2")];

        var json = ScenarioExporter.Export(rows, Meta("System")).Json;

        Assert.Contains("\"color\": \"DarkRed\"", json);
        Assert.Contains("\"color\": \"Green\"", json);
    }

    [Fact]
    public void Export_DerivesRequiresAdminForSecurityChannel()
    {
        var json = ScenarioExporter.Export([Row(HighlightColor.None, EventProperty.Id, "4624")], Meta("Security")).Json;

        Assert.Contains("\"requiresAdmin\": true", json);
    }

    [Fact]
    public void Export_EmittedRowCount_MatchesEmittedRows()
    {
        Assert.Equal(0, ScenarioExporter.Export([], Meta("System")).EmittedRowCount);
        Assert.Equal(1, ScenarioExporter.Export([Row(HighlightColor.None, EventProperty.Id, "1")], Meta("System")).EmittedRowCount);

        ScenarioExportRow[] two = [Row(HighlightColor.None, EventProperty.Id, "1"), Row(HighlightColor.None, EventProperty.Id, "2")];
        Assert.Equal(2, ScenarioExporter.Export(two, Meta("System")).EmittedRowCount);
    }

    [Fact]
    public void Export_Many_OmitsOperatorAndUsesValues()
    {
        var json = ScenarioExporter.Export([Row(HighlightColor.None, EventProperty.Id, "1", "2", "3")], Meta("System")).Json;

        Assert.Contains("\"matchMode\": \"Many\"", json);
        Assert.Contains("\"values\"", json);
        Assert.DoesNotContain("\"operator\"", json);
    }

    [Fact]
    public void Export_NoChannels_Warns()
    {
        var result = ScenarioExporter.Export(
            [Row(HighlightColor.None, EventProperty.Id, "1")],
            new ScenarioExportMetadata("id", "n", "p", ScenarioGroup.SystemHealth, []));

        Assert.Contains(ScenarioExporter.NoLiveChannelsWarning, result.Warnings);
    }

    [Fact]
    public void Export_NoRows_OmitsSelfValidationNoise()
    {
        var result = ScenarioExporter.Export([], Meta("System"));

        Assert.Equal(0, result.EmittedRowCount);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("must declare at least one filter row"));
    }

    [Fact]
    public void Export_RawShape_IsCamelCaseIndentedWithNoNullKeys()
    {
        var json = ScenarioExporter.Export([Row(HighlightColor.None, EventProperty.Source, "disk")], Meta("System")).Json;

        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"property\": \"Source\"", json);
        Assert.Contains("\"value\": \"disk\"", json);
        Assert.Contains("\n", json);
        Assert.DoesNotContain("null", json);
        Assert.DoesNotContain("\"operator\"", json);
        Assert.DoesNotContain("\"matchMode\"", json);
        Assert.DoesNotContain("\"color\"", json);
    }

    [Fact]
    public void Export_RoundTripsByteIdentical()
    {
        ScenarioExportRow[] rows =
        [
            Row(HighlightColor.Green, EventProperty.Source, "Microsoft-Windows-Kernel-General"),
            Row(HighlightColor.Red, EventProperty.Id, "41", "6008")
        ];

        var first = ScenarioExporter.Export(rows, Meta("System"));
        Assert.Empty(first.Warnings);

        var loaded = ScenarioCatalogLoader.TryLoad([("x.json", Encoding.UTF8.GetBytes(first.Json))]);
        Assert.Empty(loaded.Errors);

        var reExported = ScenarioExporter.Export(
            [.. loaded.Scenarios[0].Filters.Select(row => new ScenarioExportRow(row.Filter, row.IsExcluded, row.Color))],
            Meta("System"));

        Assert.Equal(first.Json, reExported.Json);
    }

    [Fact]
    public void Export_SingleRowWithColor_SurfacesGuardrailWarning()
    {
        var result = ScenarioExporter.Export([Row(HighlightColor.Green, EventProperty.Id, "1")], Meta("System"));

        Assert.Contains(result.Warnings, warning => warning.Contains("single-row"));
    }

    private static ScenarioExportMetadata Meta(params string[] channels) =>
        new("test-scenario", "Test scenario", "A test.", ScenarioGroup.SystemHealth, channels);

    private static ScenarioExportRow Row(HighlightColor color, EventProperty property, params string[] values)
    {
        FilterComparison comparison = values.Length == 1
            ? new() { Property = property, Operator = ComparisonOperator.Equals, MatchMode = MatchMode.Single, Value = values[0] }
            : new() { Property = property, Operator = ComparisonOperator.Equals, MatchMode = MatchMode.Many, Values = [.. values] };

        return new ScenarioExportRow(new BasicFilter(comparison, []), false, color);
    }
}
