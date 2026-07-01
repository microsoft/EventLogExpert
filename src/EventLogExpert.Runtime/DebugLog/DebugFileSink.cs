// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Logging.Sinks;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Settings;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace EventLogExpert.Runtime.DebugLog;

internal sealed class DebugFileSink : ILogSink, IFileLogger, IDisposable, IAsyncDisposable
{
    private const long MaxLogSize = 10 * 1024 * 1024;

    private static readonly TimeSpan s_interprocessLockTimeout = TimeSpan.FromSeconds(2);

    private readonly FileLocationOptions _fileLocationOptions;
    private readonly Mutex _interprocessMutex;
    private readonly LogRoutingPolicy _routingPolicy;
    private readonly ISettingsService _settings;
    private readonly Lock _writeLock = new();

    private bool _disposed;
    private StreamWriter? _writer;

    public DebugFileSink(FileLocationOptions fileLocationOptions, ISettingsService settings, LogRoutingPolicy routingPolicy)
    {
        _fileLocationOptions = fileLocationOptions;
        _settings = settings;
        _routingPolicy = routingPolicy;
        _interprocessMutex = new Mutex(false, DeriveMutexName(_fileLocationOptions.LoggingPath));

        InitTracing();

        _settings.LogLevelChanged += OnLogLevelChanged;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
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
                        _fileLocationOptions.LoggingPath,
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
        try { DetachEvents(); } catch { }

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
        try { DetachEvents(); } catch { }

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
        // Re-check this sink's own threshold: the dispatcher gates on the aggregate across all sinks, which may be lower.
        if (record.Level < _routingPolicy.FileMinimumFor(record.Origin)) { return; }

        WriteOutput(FormatLine(record.TimestampUtc.ToLocalTime(), Environment.CurrentManagedThreadId, record.Level, record.Message));
    }

    public async IAsyncEnumerable<string> LoadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // ReadWrite + Delete share: concurrent writers (second app instance) and rotation-driven deletion.
        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            BufferSize = 4096
        };

        FileStream stream;

        try
        {
            stream = new FileStream(_fileLocationOptions.LoggingPath, options);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Log not yet created (fresh install) or rotation deleted it; treat as empty.
            yield break;
        }

        await using (stream)
        {
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                yield return line;
            }
        }
    }

    public LogLevel MinimumLevelFor(string origin) => _routingPolicy.FileMinimumFor(origin);

    private static string DeriveMutexName(string path)
    {
        // Canonicalize: equivalent paths (relative, separator drift) must hash identically.
        var canonical = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var bytes = Encoding.UTF8.GetBytes(canonical.ToUpperInvariant());
        var hash = SHA256.HashData(bytes);
        return $"Local\\EventLogExpert.DebugLog.{Convert.ToHexString(hash, 0, 8)}";
    }

    private static string FormatLine(DateTime localTimestamp, int threadId, LogLevel level, string message) =>
        $"[{localTimestamp:o}] [{threadId}] [{level}] {message}";

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private void DetachEvents()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        _settings.LogLevelChanged -= OnLogLevelChanged;
    }

    private void EnsureWriter()
    {
        if (_writer is not null) { return; }

        // OpenOrCreate (not Append) avoids stale-position writes after cross-instance SetLength(0).
        var stream = new FileStream(
            _fileLocationOptions.LoggingPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096);

        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    private void InitTracing()
    {
        var fileInfo = new FileInfo(_fileLocationOptions.LoggingPath);

        if (fileInfo is { Exists: true, Length: > MaxLogSize })
        {
            fileInfo.Delete();
        }
    }

    private void OnLogLevelChanged()
    {
        _routingPolicy.UpdateGlobalBaseline(_settings.LogLevel);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        WriteOutput(FormatLine(DateTime.Now, Environment.CurrentManagedThreadId, LogLevel.Critical, $"Unhandled Exception: {e.ExceptionObject}"));
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
