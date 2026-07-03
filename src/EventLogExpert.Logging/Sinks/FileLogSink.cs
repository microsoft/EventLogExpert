// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace EventLogExpert.Logging.Sinks;

/// <summary>
///     Write-only file sink. Formats each record via the injected <paramref name="formatter" /> and appends it to a
///     shared log file, serializing in-process writes with a lock and cross-process writes with a named mutex derived from
///     the canonical path. Reading the file back is deliberately NOT part of this sink (an application concern).
/// </summary>
public sealed class FileLogSink : ILogSink, IDisposable, IAsyncDisposable
{
    private const long MaxLogSize = 10 * 1024 * 1024;

    private static readonly TimeSpan s_interprocessLockTimeout = TimeSpan.FromSeconds(2);

    private readonly Func<LogRecord, string> _formatter;
    private readonly Mutex _interprocessMutex;
    private readonly string _path;
    private readonly LogRoutingPolicy _routingPolicy;
    private readonly Lock _writeLock = new();

    private bool _disposed;
    private StreamWriter? _writer;

    public FileLogSink(string path, LogRoutingPolicy routingPolicy, Func<LogRecord, string> formatter)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(routingPolicy);
        ArgumentNullException.ThrowIfNull(formatter);

        _path = path;
        _routingPolicy = routingPolicy;
        _formatter = formatter;
        _interprocessMutex = new Mutex(false, DeriveMutexName(path));

        InitTracing();
    }

    public Task ClearAsync()
    {
        using (_writeLock.EnterScope())
        {
            if (_disposed) { return Task.CompletedTask; }

            CloseWriter();

            WithInterprocessLock(
                () =>
                {
                    // Share with concurrent writers; SetLength(0) truncates without closing them.
                    using var stream = new FileStream(
                        _path,
                        FileMode.OpenOrCreate,
                        FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete);

                    stream.SetLength(0);
                },
                throwOnTimeout: true);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        using (_writeLock.EnterScope())
        {
            if (_disposed) { return; }

            _disposed = true;
        }

        try
        {
            CloseWriter();
        }
        finally
        {
            _interprocessMutex.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        StreamWriter? writer;

        using (_writeLock.EnterScope())
        {
            if (_disposed) { return; }

            _disposed = true;
            writer = _writer;
            _writer = null;
        }

        try
        {
            if (writer is not null) { await writer.DisposeAsync(); }
        }
        finally
        {
            _interprocessMutex.Dispose();
        }
    }

    public void Emit(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Re-check this sink's own threshold: the dispatcher gates on the aggregate across all sinks, which may be lower.
        if (record.Level < _routingPolicy.FileMinimumFor(record.Category)) { return; }

        WriteOutput(_formatter(record));
    }

    /// <summary>
    ///     Writes a record bypassing the routing threshold, for fatal records (e.g. an unhandled exception) that must be
    ///     persisted regardless of the configured minimum level.
    /// </summary>
    public void EmitUnfiltered(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            WriteOutput(_formatter(record));
        }
        catch
        {
            // Runs from the unhandled-exception handler; a write fault here must not mask the original crash.
        }
    }

    public LogLevel MinimumLevelFor(string category) => _routingPolicy.FileMinimumFor(category);

    private static string DeriveMutexName(string path)
    {
        // Canonicalize: equivalent paths (relative, separator drift) must hash identically.
        var canonical = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var bytes = Encoding.UTF8.GetBytes(canonical.ToUpperInvariant());
        var hash = SHA256.HashData(bytes);

        return $"Local\\EventLogExpert.DebugLog.{Convert.ToHexString(hash, 0, 8)}";
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private void EnsureWriter()
    {
        if (_writer is not null) { return; }

        // OpenOrCreate (not Append) avoids stale-position writes after cross-instance SetLength(0).
        var stream = new FileStream(
            _path,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096);

        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    private void InitTracing()
    {
        // Rotate under the interprocess lock (throwOnTimeout: false so transient contention skips rotation rather than
        // failing sink construction) and truncate instead of deleting so a concurrent opener never loses the file.
        WithInterprocessLock(
            () =>
            {
                var fileInfo = new FileInfo(_path);

                if (fileInfo is not { Exists: true, Length: > MaxLogSize }) { return; }

                try
                {
                    using var stream = new FileStream(
                        _path,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete);

                    stream.SetLength(0);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The file was deleted, locked, or read-only between the size check and the open; skip rotation (best-effort).
                }
            },
            throwOnTimeout: false);
    }

    private void WithInterprocessLock(Action action, bool throwOnTimeout)
    {
        bool acquired;

        try
        {
            acquired = _interprocessMutex.WaitOne(s_interprocessLockTimeout);
        }
        catch (AbandonedMutexException)
        {
            // Prior owner crashed without releasing; we now hold it and proceed.
            acquired = true;
        }

        if (!acquired)
        {
            if (throwOnTimeout)
            {
                throw new TimeoutException(
                    $"Timed out acquiring debug-log interprocess lock after {s_interprocessLockTimeout.TotalSeconds:F0}s.");
            }

            // Hot path: drop this trace rather than risk interleaved/corrupted writes.
            return;
        }

        try
        {
            action();
        }
        finally
        {
            _interprocessMutex.ReleaseMutex();
        }
    }

    private void WriteOutput(string output)
    {
        using (_writeLock.EnterScope())
        {
            if (_disposed) { return; }

            EnsureWriter();

            // Mutex serializes line writes so concurrent instances don't interleave.
            WithInterprocessLock(
                () =>
                {
                    if (_writer is null) { return; }

                    // Re-seek to EOF: another instance may have written or truncated the file.
                    _writer.BaseStream.Seek(0, SeekOrigin.End);
                    _writer.WriteLine(output);
                },
                throwOnTimeout: false);

#if DEBUG
            Debug.WriteLine(output);
#endif
        }
    }
}
