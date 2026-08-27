// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Export;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Tests.LogTable.TestSupport;
using EventLogExpert.Runtime.Tests.TestUtils;
using System.Globalization;
using System.Text;

namespace EventLogExpert.Runtime.Tests.Export;

/// <summary>
///     Locks CSV/JSON export byte-for-byte across regional cultures. Export values are InvariantCulture-formatted (
///     <see cref="EventTableExporter" /> always supplies its fixed date format), so any flip of the export date path to
///     <see cref="CultureInfo.CurrentCulture" /> must fail this guard.
/// </summary>
/// <remarks>
///     Sensitivity RIDES ON the exporter's date format containing a culture-dependent placeholder (the <c>:</c>
///     TimeSeparator). The contrast culture MUST be <c>fi-FI</c> (TimeSeparator <c>.</c>), NOT <c>de-DE</c>: German
///     formats <c>yyyy-MM-dd HH:mm:ss</c> byte-identically to the invariant culture (shared ASCII digits, Gregorian
///     calendar, and <c>:</c> separator), so a de-DE guard could not detect an InvariantCulture-to-CurrentCulture
///     regression. Non-negative IDs avoid fi-FI's U+2212 NegativeSign on the provider-less numeric columns. If the
///     exporter's date format ever loses its separator, this guard goes blind (same class as a stale sentinel name).
/// </remarks>
[Collection(CultureSensitiveCollection.Name)]
public sealed class EventTableExporterCultureTests
{
    private const string ExpectedTimestamp = "2026-08-26 17:57:05";

    private static readonly ColumnName[] s_columns = [ColumnName.EventId, ColumnName.DateAndTime, ColumnName.Source];

    private static readonly ResolvedEvent[] s_events =
    [
        new("Log", LogPathType.Channel)
        {
            Id = 4624,
            Source = "Alpha",
            TimeCreated = new DateTime(2026, 8, 26, 17, 57, 5, DateTimeKind.Utc)
        }
    ];

    private static readonly TimeZoneInfo s_utc = TimeZoneInfo.Utc;

    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Json)]
    public async Task ExportAsync_IsByteIdenticalAcrossRegionalCultures(ExportFormat format)
    {
        byte[] neutral = await ExportUnderCultureAsync(format, CultureInfo.GetCultureInfo("en-US"));
        byte[] contrast = await ExportUnderCultureAsync(format, CultureInfo.GetCultureInfo("fi-FI"));

        // The date-bearing column must actually be present, else the byte-comparison would pass without ever
        // exercising the culture-sensitive date formatting this guard exists to lock.
        Assert.Contains(ExpectedTimestamp, Encoding.UTF8.GetString(neutral), StringComparison.Ordinal);
        Assert.Equal(neutral, contrast);
    }

    private static IEventColumnView BuildView(IReadOnlyList<ResolvedEvent> events)
    {
        var reader = EventColumnStore.Build(events, generation: 0, contentVersion: 0).CreateReader(EventLogId.Create());
        int[] survivors = [.. Enumerable.Range(0, events.Count)];

        return AosReferenceView.Create(
            reader,
            survivors,
            orderBy: null,
            isDescending: false,
            groupBy: null,
            isGroupDescending: false);
    }

    private static async Task<byte[]> ExportUnderCultureAsync(ExportFormat format, CultureInfo culture)
    {
        var priorCulture = CultureInfo.CurrentCulture;
        var priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en"); // isolate the regional axis from the localization axis

            EventTableExporter exporter = new(new TabularExportWriter());
            using MemoryStream stream = new();

            await exporter.ExportAsync(
                stream, format, BuildView(s_events), s_columns, s_utc, includeDescription: false, TestContext.Current.CancellationToken);

            return stream.ToArray();
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }
}
