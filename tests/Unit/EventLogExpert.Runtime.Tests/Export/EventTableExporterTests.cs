// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Export;
using EventLogExpert.Runtime.LogTable;
using System.Text.Json;

namespace EventLogExpert.Runtime.Tests.Export;

public sealed class EventTableExporterTests
{
    private static readonly ColumnName[] s_columns = [ColumnName.EventId, ColumnName.Source];

    private static readonly ResolvedEvent[] s_events =
    [
        new("Log", LogPathType.Channel) { Id = 1, Source = "Alpha" },
        new("Log", LogPathType.Channel) { Id = 2, Source = "Beta" }
    ];
    private static readonly TimeZoneInfo s_utc = TimeZoneInfo.Utc;

    [Theory]
    [InlineData("=SUM(A1)", "Source\r\n'=SUM(A1)\r\n")]
    [InlineData("+1+1", "Source\r\n'+1+1\r\n")]
    [InlineData("-2-2", "Source\r\n'-2-2\r\n")]
    [InlineData("@cmd", "Source\r\n'@cmd\r\n")]
    [InlineData("\tTabLed", "Source\r\n'\tTabLed\r\n")]
    [InlineData("\rCarriage", "Source\r\n\"'\rCarriage\"\r\n")]
    [InlineData("Alpha", "Source\r\nAlpha\r\n")]
    [InlineData("a=b", "Source\r\na=b\r\n")]
    public async Task ExportAsync_Csv_NeutralizesLeadingFormulaTriggers(string sourceValue, string expectedCsv)
    {
        ResolvedEvent[] events = [new("Log", LogPathType.Channel) { Id = 1, Source = sourceValue }];

        byte[] bytes = await ExportAsync(ExportFormat.Csv, events, [ColumnName.Source]);

        Assert.Equal(expectedCsv, ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task ExportAsync_Csv_WritesHeaderAndRows()
    {
        byte[] bytes = await ExportAsync(ExportFormat.Csv, s_events, s_columns);

        Assert.Equal("Event ID,Source\r\n1,Alpha\r\n2,Beta\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task ExportAsync_Json_DoesNotNeutralizeFormula()
    {
        ResolvedEvent[] events = [new("Log", LogPathType.Channel) { Id = 1, Source = "=SUM(A1)" }];

        byte[] bytes = await ExportAsync(ExportFormat.Json, events, [ColumnName.Source]);

        using JsonDocument document = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes));
        Assert.Equal("=SUM(A1)", document.RootElement[0].GetProperty("Source").GetString());
    }

    [Fact]
    public async Task ExportAsync_Json_WritesArrayOfObjects()
    {
        byte[] bytes = await ExportAsync(ExportFormat.Json, s_events, s_columns);

        using JsonDocument document = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes));
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal("1", document.RootElement[0].GetProperty("Event ID").GetString());
        Assert.Equal("Beta", document.RootElement[1].GetProperty("Source").GetString());
    }

    [Fact]
    public async Task ExportAsync_NoEvents_Csv_WritesHeaderOnly()
    {
        byte[] bytes = await ExportAsync(ExportFormat.Csv, [], s_columns);

        Assert.Equal("Event ID,Source\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    private static async Task<byte[]> ExportAsync(
        ExportFormat format, IReadOnlyList<ResolvedEvent> events, IReadOnlyList<ColumnName> columns)
    {
        var exporter = new EventTableExporter(new TabularExportWriter());
        using var stream = new MemoryStream();

        await exporter.ExportAsync(stream, format, events, columns, s_utc, TestContext.Current.CancellationToken);

        return stream.ToArray();
    }
}
