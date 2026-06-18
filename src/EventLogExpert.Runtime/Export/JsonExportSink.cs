// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json;

namespace EventLogExpert.Runtime.Export;

internal sealed class JsonExportSink(Stream destination, string[] headers) : ExportSink(headers.Length)
{
    private const int FlushThresholdBytes = 64 * 1024;

    private static readonly JsonWriterOptions s_options = new() { Indented = true };

    private readonly string[] _headers = headers;
    private readonly Utf8JsonWriter _writer = new(destination, s_options);

    protected override async ValueTask CompleteCoreAsync(CancellationToken cancellationToken)
    {
        _writer.WriteEndArray();
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override ValueTask DisposeCoreAsync()
    {
        // CompleteCoreAsync is the single, cancellable flush, so disposal has nothing to do. Disposing the
        // Utf8JsonWriter would force an uncancellable underlying-stream flush (and after an aborted export, partial
        // JSON). It wraps the caller-owned stream (leaveOpen) and holds only a managed byte buffer with no finalizer,
        // so it is safely abandoned for the GC.
        return ValueTask.CompletedTask;
    }

    protected override ValueTask WriteHeaderCoreAsync(CancellationToken cancellationToken)
    {
        _writer.WriteStartArray();

        return ValueTask.CompletedTask;
    }

    protected override async ValueTask WriteRowCoreAsync(IReadOnlyList<string?> cells, CancellationToken cancellationToken)
    {
        _writer.WriteStartObject();

        for (int i = 0; i < _headers.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? cell = cells[i];

            if (cell is null)
            {
                _writer.WriteNull(_headers[i]);
            }
            else
            {
                _writer.WriteString(_headers[i], cell);
            }

            // Utf8JsonWriter buffers in a pooled array until flushed; cap it per cell to stay bounded-memory.
            if (_writer.BytesPending >= FlushThresholdBytes)
            {
                await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        _writer.WriteEndObject();
    }
}
