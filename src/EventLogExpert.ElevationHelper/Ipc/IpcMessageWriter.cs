// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using System.Text;
using System.Text.Json;

namespace EventLogExpert.ElevationHelper.Ipc;

/// <summary>
///     Writes <see cref="DatabaseToolsIpcMessage" /> instances as line-delimited UTF-8 JSON through a stream. Each
///     <see cref="WriteAsync" /> call is serialized via an internal <see cref="SemaphoreSlim" /> so concurrent writes from
///     different tasks don't interleave on the wire.
/// </summary>
internal sealed class IpcMessageWriter(Stream destination) : IAsyncDisposable
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly CancellationTokenSource _shutdownCts = new();

    private readonly SemaphoreSlim _writeLock = new(initialCount: 1, maxCount: 1);
    private readonly StreamWriter _writer = new(destination, s_utf8NoBom, bufferSize: 4096, leaveOpen: true) { NewLine = "\n", AutoFlush = false };

    private volatile bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }

        try { _shutdownCts.Cancel(); }
        catch (ObjectDisposedException) { return; }

        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed) { return; }

            _disposed = true;

            try { await _writer.FlushAsync().ConfigureAwait(false); } catch { /* best effort during dispose */ }

            await _writer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try { _writeLock.Release(); } catch (ObjectDisposedException) { }

            try { _writeLock.Dispose(); } catch (ObjectDisposedException) { }

            _shutdownCts.Dispose();
        }
    }

    public async Task WriteAsync(DatabaseToolsIpcMessage message, CancellationToken cancellationToken)
    {
        if (_disposed) { return; }

        var json = JsonSerializer.Serialize(message, DatabaseToolsIpcSerializer.Options);

        CancellationTokenSource linkedCts;

        try
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            try
            {
                await _writeLock.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                if (_disposed) { return; }

                await _writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try { _writeLock.Release(); } catch (ObjectDisposedException) { }
            }
        }
        finally
        {
            linkedCts.Dispose();
        }
    }
}
