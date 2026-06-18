// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Export;

internal interface ITabularExportWriter
{
    Task WriteAsync(
        Stream destination,
        ExportFormat format,
        IReadOnlyList<string> headers,
        IAsyncEnumerable<IReadOnlyList<string?>> rows,
        CancellationToken cancellationToken);
}
