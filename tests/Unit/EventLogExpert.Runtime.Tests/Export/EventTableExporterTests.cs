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

    [Fact]
    public async Task ExportAsync_Csv_ExcludeDescription_OmitsColumn()
    {
        ResolvedEvent[] events = [new("Log", LogPathType.Channel) { Id = 1, Source = "Alpha", Description = "Ignored" }];

        byte[] bytes = await ExportAsync(ExportFormat.Csv, events, s_columns, includeDescription: false);

        Assert.Equal("Event ID,Source\r\n1,Alpha\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task ExportAsync_Csv_IncludeDescription_AppendsColumn()
    {
        ResolvedEvent[] events =
        [
            new("Log", LogPathType.Channel) { Id = 1, Source = "Alpha", Description = "First" },
            new("Log", LogPathType.Channel) { Id = 2, Source = "Beta" }
        ];

        byte[] bytes = await ExportAsync(ExportFormat.Csv, events, s_columns, includeDescription: true);

        Assert.Equal(
            "Event ID,Source,Description\r\n1,Alpha,First\r\n2,Beta,\r\n",
            ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task ExportAsync_Csv_IncludeDescription_LargeDescriptionRoundTrips()
    {
        string largeDescription = new('x', 70_000);
        ResolvedEvent[] events = [new("Log", LogPathType.Channel) { Id = 1, Description = largeDescription }];

        byte[] bytes = await ExportAsync(ExportFormat.Csv, events, [ColumnName.EventId], includeDescription: true);

        Assert.Equal(
            $"Event ID,Description\r\n1,{largeDescription}\r\n",
            ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Theory]
    [InlineData("plain", "Event ID,Description\r\n1,plain\r\n")]
    [InlineData("=cmd", "Event ID,Description\r\n1,'=cmd\r\n")]
    [InlineData("+cmd", "Event ID,Description\r\n1,'+cmd\r\n")]
    [InlineData("-cmd", "Event ID,Description\r\n1,'-cmd\r\n")]
    [InlineData("@cmd", "Event ID,Description\r\n1,'@cmd\r\n")]
    [InlineData("\tcmd", "Event ID,Description\r\n1,'\tcmd\r\n")]
    [InlineData("\rcmd", "Event ID,Description\r\n1,\"'\rcmd\"\r\n")]
    [InlineData("a\"b", "Event ID,Description\r\n1,\"a\"\"b\"\r\n")]
    [InlineData("a\nb", "Event ID,Description\r\n1,\"a\nb\"\r\n")]
    [InlineData("a\rb", "Event ID,Description\r\n1,\"a\rb\"\r\n")]
    [InlineData("a\r\nb", "Event ID,Description\r\n1,\"a\r\nb\"\r\n")]
    [InlineData("=cmd\r\nx", "Event ID,Description\r\n1,\"'=cmd\r\nx\"\r\n")]
    public async Task ExportAsync_Csv_IncludeDescription_NeutralizesAndQuotes(string description, string expectedCsv)
    {
        ResolvedEvent[] events = [new("Log", LogPathType.Channel) { Id = 1, Description = description }];

        byte[] bytes = await ExportAsync(ExportFormat.Csv, events, [ColumnName.EventId], includeDescription: true);

        Assert.Equal(expectedCsv, ExportTestHelpers.DecodeWithoutBom(bytes));
    }

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
    public async Task ExportAsync_Json_IncludeDescription_AppendsProperty()
    {
        ResolvedEvent[] events = [new("Log", LogPathType.Channel) { Id = 1, Source = "Alpha", Description = "=cmd" }];

        byte[] bytes = await ExportAsync(ExportFormat.Json, events, [ColumnName.Source], includeDescription: true);

        using JsonDocument document = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes));
        Assert.Equal("=cmd", document.RootElement[0].GetProperty("Description").GetString());
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
        ExportFormat format,
        IReadOnlyList<ResolvedEvent> events,
        IReadOnlyList<ColumnName> columns,
        bool includeDescription = false)
    {
        EventTableExporter exporter = new(new TabularExportWriter());
        using MemoryStream stream = new();

        await exporter.ExportAsync(
            stream, format, events, columns, s_utc, includeDescription, TestContext.Current.CancellationToken);

        return stream.ToArray();
    }
}
