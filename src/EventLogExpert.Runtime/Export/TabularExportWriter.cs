// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Export;

internal sealed class TabularExportWriter : ITabularExportWriter
{
    public async Task WriteAsync(
        Stream destination,
        ExportFormat format,
        IReadOnlyList<string> headers,
        IAsyncEnumerable<IReadOnlyList<string?>> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        string[] columnHeaders = ValidateAndCopyHeaders(headers);

        await using ExportSink sink = CreateSink(format, destination, columnHeaders);

        await sink.WriteHeaderAsync(cancellationToken).ConfigureAwait(false);

        await foreach (IReadOnlyList<string?> row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await sink.WriteRowAsync(row, cancellationToken).ConfigureAwait(false);
        }

        await sink.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ExportSink CreateSink(ExportFormat format, Stream destination, string[] columnHeaders) =>
        format switch
        {
            ExportFormat.Csv => new CsvExportSink(destination, columnHeaders),
            ExportFormat.Json => new JsonExportSink(destination, columnHeaders),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported export format.")
        };

    private static string[] ValidateAndCopyHeaders(IReadOnlyList<string> headers)
    {
        if (headers.Count == 0)
        {
            throw new ArgumentException("At least one column header is required.", nameof(headers));
        }

        string[] columnHeaders = new string[headers.Count];
        HashSet<string> seenHeaders = new(StringComparer.Ordinal);

        for (int i = 0; i < headers.Count; i++)
        {
            string header = headers[i];

            if (string.IsNullOrEmpty(header))
            {
                throw new ArgumentException($"The column header at index {i} is null or empty.", nameof(headers));
            }

            if (!seenHeaders.Add(header))
            {
                throw new ArgumentException($"Duplicate column header '{header}'.", nameof(headers));
            }

            columnHeaders[i] = header;
        }

        return columnHeaders;
    }
}
