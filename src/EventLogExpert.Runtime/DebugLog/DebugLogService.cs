// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Settings;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace EventLogExpert.Runtime.DebugLog;

internal sealed class DebugLogService : ITraceLogger, IFileLogger, IDisposable
{
    private const long MaxLogSize = 10 * 1024 * 1024;

    private static readonly TimeSpan s_interprocessLockTimeout = TimeSpan.FromSeconds(2);

    private readonly FileLocationOptions _fileLocationOptions;
    private readonly Mutex _interprocessMutex;
    private readonly ISettingsService _settings;
    private readonly Lock _writeLock = new();

    private volatile LogLevel _cachedLogLevel;
    private StreamWriter? _writer;

    public DebugLogService(FileLocationOptions fileLocationOptions, ISettingsService settings)
    {
        _fileLocationOptions = fileLocationOptions;
        _settings = settings;
        _cachedLogLevel = _settings.LogLevel;
        _interprocessMutex = new Mutex(false, DeriveMutexName(_fileLocationOptions.LoggingPath));

        InitTracing();

        _settings.LogLevelChanged += OnLogLevelChanged;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public LogLevel MinimumLevel => _cachedLogLevel;

    public Task ClearAsync()
    {
        using (_writeLock.EnterScope())
        {
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

    public void Critical([InterpolatedStringHandlerArgument("")] CriticalLogHandler handler)
    {
        if (!handler.IsEnabled) { return; }

        WriteTrace(handler.ToStringAndClear(), LogLevel.Critical);
    }

    public void Debug([InterpolatedStringHandlerArgument("")] DebugLogHandler handler)
    {
        if (!handler.IsEnabled) { return; }

        WriteTrace(handler.ToStringAndClear(), LogLevel.Debug);
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

        _settings.LogLevelChanged -= OnLogLevelChanged;

        using (_writeLock.EnterScope())
        {
            CloseWriter();
        }

        _interprocessMutex.Dispose();
    }

    public void Error([InterpolatedStringHandlerArgument("")] ErrorLogHandler handler)
    {
        if (!handler.IsEnabled) { return; }

        WriteTrace(handler.ToStringAndClear(), LogLevel.Error);
    }

    public void Information([InterpolatedStringHandlerArgument("")] InformationLogHandler handler)
    {
        if (!handler.IsEnabled) { return; }

        WriteTrace(handler.ToStringAndClear(), LogLevel.Information);
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

    public void Trace([InterpolatedStringHandlerArgument("")] TraceLogHandler handler)
    {
        if (!handler.IsEnabled) { return; }

        WriteTrace(handler.ToStringAndClear(), LogLevel.Trace);
    }

    public void Warning([InterpolatedStringHandlerArgument("")] WarningLogHandler handler)
    {
        if (!handler.IsEnabled) { return; }

        WriteTrace(handler.ToStringAndClear(), LogLevel.Warning);
    }

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
        _cachedLogLevel = _settings.LogLevel;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        WriteTrace($"Unhandled Exception: {e.ExceptionObject}", LogLevel.Critical);
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

    private void WriteTrace(string message, LogLevel level)
    {
        using (_writeLock.EnterScope())
        {
            string output = $"[{DateTime.Now:o}] [{Environment.CurrentManagedThreadId}] [{level}] {message}";

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
            System.Diagnostics.Debug.WriteLine(output);
#endif
        }
    }
}
