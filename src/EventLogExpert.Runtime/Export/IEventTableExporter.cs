// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Export;

public interface IEventTableExporter
{
    Task ExportAsync(
        Stream destination,
        ExportFormat format,
        IReadOnlyList<ResolvedEvent> events,
        IReadOnlyList<ColumnName> columns,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken);
}
