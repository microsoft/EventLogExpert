// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Runtime.Export;

internal sealed class EventTableExporter(ITabularExportWriter writer) : IEventTableExporter
{
    private const string ExportDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    public async Task ExportAsync(
        Stream destination,
        ExportFormat format,
        IEventColumnView events,
        IReadOnlyList<ColumnName> columns,
        TimeZoneInfo timeZone,
        bool includeDescription,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(timeZone);

        string[] headers = new string[columns.Count + (includeDescription ? 1 : 0)];

        for (int i = 0; i < columns.Count; i++)
        {
            headers[i] = EventTableColumnFormatter.GetColumnHeader(columns[i], timeZone);
        }

        if (includeDescription)
        {
            headers[columns.Count] = EventTableColumnFormatter.DescriptionColumnHeader;
        }

        await writer.WriteAsync(
                destination,
                format,
                headers,
                ProjectRows(events, columns, timeZone, format, includeDescription, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<IReadOnlyList<string?>> ProjectRows(
        IEventColumnView events,
        IReadOnlyList<ColumnName> columns,
        TimeZoneInfo timeZone,
        ExportFormat format,
        bool includeDescription,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        bool neutralizeCsvFormula = format == ExportFormat.Csv;
        int cellCount = columns.Count + (includeDescription ? 1 : 0);

        foreach (ResolvedEvent @event in events.EnumerateDetailLean())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string?[] cells = new string?[cellCount];

            for (int i = 0; i < columns.Count; i++)
            {
                string cell = EventTableColumnFormatter.GetCellText(@event, columns[i], timeZone, ExportDateTimeFormat);
                cells[i] = neutralizeCsvFormula ? TabularCellSanitizer.NeutralizeFormula(cell) : cell;
            }

            if (includeDescription)
            {
                cells[columns.Count] =
                    neutralizeCsvFormula ?
                        TabularCellSanitizer.NeutralizeFormula(@event.Description) :
                        @event.Description;
            }

            yield return cells;
        }
    }
}
