// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Buffers;
using System.Text;

namespace EventLogExpert.Runtime.Export;

internal sealed class CsvExportSink(Stream destination, string[] headers) : ExportSink(headers.Length)
{
    private const char FieldSeparator = ',';
    private const int WriterBufferSize = 16 * 1024;

    private static readonly ReadOnlyMemory<char> s_byteOrderMark = "\uFEFF".AsMemory();
    private static readonly SearchValues<char> s_charactersRequiringQuotes = SearchValues.Create(",\"\r\n");
    private static readonly ReadOnlyMemory<char> s_rowTerminator = "\r\n".AsMemory();
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly StringBuilder _fieldBuilder = new();
    private readonly string[] _headers = headers;
    private readonly StreamWriter _writer = new(destination, s_utf8NoBom, WriterBufferSize, leaveOpen: true);

    protected override async ValueTask CompleteCoreAsync(CancellationToken cancellationToken) =>
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);

    protected override ValueTask DisposeCoreAsync()
    {
        // CompleteCoreAsync is the single, cancellable flush, so disposal has nothing to do. Disposing the
        // StreamWriter would force an uncancellable underlying-stream flush (and after an aborted export, a torn
        // record). It wraps the caller-owned stream (leaveOpen) and holds only a managed char buffer with no
        // finalizer, so it is safely abandoned for the GC.
        return ValueTask.CompletedTask;
    }

    protected override ValueTask WriteHeaderCoreAsync(CancellationToken cancellationToken) =>
        WriteRecordAsync(_headers, writeByteOrderMark: true, cancellationToken);

    protected override ValueTask WriteRowCoreAsync(IReadOnlyList<string?> cells, CancellationToken cancellationToken) =>
        WriteRecordAsync(cells, writeByteOrderMark: false, cancellationToken);

    private static void AppendField(StringBuilder builder, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (value.AsSpan().IndexOfAny(s_charactersRequiringQuotes) < 0)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');

        foreach (char character in value)
        {
            if (character == '"')
            {
                builder.Append('"');
            }

            builder.Append(character);
        }

        builder.Append('"');
    }

    private async ValueTask WriteRecordAsync(
        IReadOnlyList<string?> fields, bool writeByteOrderMark, CancellationToken cancellationToken)
    {
        if (writeByteOrderMark)
        {
            // Emit the UTF-8 BOM here, not via the encoder, so a cancelled pre-header export writes no bytes at
            // all; Excel needs the BOM to detect UTF-8.
            await _writer.WriteAsync(s_byteOrderMark, cancellationToken).ConfigureAwait(false);
        }

        for (int i = 0; i < fields.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _fieldBuilder.Clear();

            if (i > 0)
            {
                _fieldBuilder.Append(FieldSeparator);
            }

            AppendField(_fieldBuilder, fields[i]);
            await _writer.WriteAsync(_fieldBuilder, cancellationToken).ConfigureAwait(false);
        }

        await _writer.WriteAsync(s_rowTerminator, cancellationToken).ConfigureAwait(false);
    }
}
