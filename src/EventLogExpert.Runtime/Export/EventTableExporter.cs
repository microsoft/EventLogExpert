// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Runtime.Export;

internal sealed class EventTableExporter(ITabularExportWriter writer) : IEventTableExporter
{
    private const string ExportDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly SearchValues<char> s_formulaInjectionTriggers = SearchValues.Create("=+-@");

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

    private static string NeutralizeCsvFormula(string value)
    {
        if (value.Length == 0) { return value; }

        // A leading TAB or CR is itself a CSV-injection vector; neutralize it directly (it is whitespace,
        // so the scan below would otherwise skip past it).
        if (value[0] is '\t' or '\r') { return "'" + value; }

        // Spreadsheet apps trim leading whitespace before evaluating a cell, so a value like " =SUM(A1)"
        // is still a formula. Neutralize when the first non-whitespace character is a formula trigger.
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character)) { continue; }

            return s_formulaInjectionTriggers.Contains(character) ? "'" + value : value;
        }

        return value;
    }

    private static async IAsyncEnumerable<IReadOnlyList<string?>> ProjectRows(
        IEventColumnView events,
        IReadOnlyList<ColumnName> columns,
        TimeZoneInfo timeZone,
        ExportFormat format,
        bool includeDescription,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // This await only satisfies the async-iterator contract; the projection itself is synchronous over the view's
        // on-demand rehydrate.
        await Task.CompletedTask.ConfigureAwait(false);

        bool neutralizeCsvFormula = format == ExportFormat.Csv;
        int cellCount = columns.Count + (includeDescription ? 1 : 0);

        // Enumerate, never index: EnumerateDetail is an O(n) single-cursor rehydrate, so indexed access would be O(n^2).
        foreach (ResolvedEvent @event in events.EnumerateDetail())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string?[] cells = new string?[cellCount];

            for (int i = 0; i < columns.Count; i++)
            {
                string cell = EventTableColumnFormatter.GetCellText(@event, columns[i], timeZone, ExportDateTimeFormat);
                cells[i] = neutralizeCsvFormula ? NeutralizeCsvFormula(cell) : cell;
            }

            if (includeDescription)
            {
                cells[columns.Count] = neutralizeCsvFormula
                    ? NeutralizeCsvFormula(@event.Description)
                    : @event.Description;
            }

            yield return cells;
        }
    }
}
