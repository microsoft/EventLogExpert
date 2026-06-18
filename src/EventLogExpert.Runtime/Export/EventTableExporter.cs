// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Runtime.Export;

internal sealed class EventTableExporter(ITabularExportWriter writer) : IEventTableExporter
{
    private const string IsoDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly SearchValues<char> s_formulaInjectionTriggers = SearchValues.Create("=+-@\t\r");

    public async Task ExportAsync(
        Stream destination,
        ExportFormat format,
        IReadOnlyList<ResolvedEvent> events,
        IReadOnlyList<ColumnName> columns,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(timeZone);

        var headers = new string[columns.Count];

        for (int i = 0; i < columns.Count; i++)
        {
            headers[i] = EventTableColumnFormatter.GetColumnHeader(columns[i], timeZone);
        }

        await writer.WriteAsync(
                destination,
                format,
                headers,
                ProjectRows(events, columns, timeZone, format, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string NeutralizeCsvFormula(string value) =>
        value.Length > 0 && s_formulaInjectionTriggers.Contains(value[0]) ? "'" + value : value;

    private static async IAsyncEnumerable<IReadOnlyList<string?>> ProjectRows(
        IReadOnlyList<ResolvedEvent> events,
        IReadOnlyList<ColumnName> columns,
        TimeZoneInfo timeZone,
        ExportFormat format,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The projection is synchronous over a materialized list; this await only satisfies the async-iterator
        // contract (no per-row scheduling overhead). The sink applies backpressure on the write side.
        await Task.CompletedTask.ConfigureAwait(false);

        bool neutralizeCsvFormula = format == ExportFormat.Csv;

        // Enumerate, never index: events may be a CombinedEventView whose indexer is O(n) per access (it merge-walks
        // from a seek point), so indexed iteration would be O(n^2). foreach is the O(n) designed access path.
        foreach (ResolvedEvent @event in events)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cells = new string?[columns.Count];

            for (int i = 0; i < columns.Count; i++)
            {
                string cell = EventTableColumnFormatter.GetCellText(@event, columns[i], timeZone, IsoDateTimeFormat);
                cells[i] = neutralizeCsvFormula ? NeutralizeCsvFormula(cell) : cell;
            }

            yield return cells;
        }
    }
}
