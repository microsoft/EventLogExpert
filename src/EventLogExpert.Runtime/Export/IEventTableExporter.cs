// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Export;

public interface IEventTableExporter
{
    Task ExportAsync(
        Stream destination,
        ExportFormat format,
        IEventColumnView events,
        IReadOnlyList<ColumnName> columns,
        TimeZoneInfo timeZone,
        bool includeDescription,
        CancellationToken cancellationToken);
}
