// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace EventLogExpert.ElevationHelper.Ipc;

/// <summary>
///     Helper-side reader for the incoming direction of the duplex IPC pipe. Reads line-delimited JSON envelopes OR a
///     single request envelope. Owns a <see cref="StreamReader" /> wrapping the pipe (leaveOpen=true so the pipe can also
///     be written to by the writer side).
/// </summary>
/// <remarks>
///     Concurrency contract: this reader is used by ONE task at a time. The helper uses it serially: (1) read the
///     request envelope; (2) start a control-reader loop watching for <see cref="CancelEnvelope" />. There is never
///     concurrent use of the reader from multiple tasks.
/// </remarks>
internal sealed class IpcEnvelopeReader(NamedPipeClientStream pipe) : IDisposable
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly StreamReader _reader = new(pipe, s_utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

    public void Dispose() => _reader.Dispose();

    /// <summary>
    ///     Read one envelope from the pipe. Returns null on EOF (pipe closed). Throws on malformed JSON (caller should
    ///     treat as fatal and abort).
    /// </summary>
    public async Task<DatabaseToolsIpcEnvelope?> ReadEnvelopeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken);

            if (line is null) { return null; }
            if (string.IsNullOrWhiteSpace(line)) { continue; }

            return JsonSerializer.Deserialize<DatabaseToolsIpcEnvelope>(line, DatabaseToolsIpcSerializer.Options)
                ?? throw new JsonException("Deserialized envelope was null.");
        }
    }

    /// <summary>
    ///     Read one request envelope from the pipe. Returns null on EOF. Throws on malformed JSON. Used at helper startup
    ///     to receive the operation parameters from the runner.
    /// </summary>
    public async Task<DatabaseToolsIpcRequest?> ReadRequestAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken);

            if (line is null) { return null; }
            if (string.IsNullOrWhiteSpace(line)) { continue; }

            return JsonSerializer.Deserialize<DatabaseToolsIpcRequest>(line, DatabaseToolsIpcSerializer.Options)
                ?? throw new JsonException("Deserialized request was null.");
        }
    }
}
