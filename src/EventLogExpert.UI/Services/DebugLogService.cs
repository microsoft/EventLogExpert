// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Options;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace EventLogExpert.UI.Services;

public sealed partial class DebugLogService : ITraceLogger, IFileLogger, IDisposable
{
    private const long MaxLogSize = 10 * 1024 * 1024;

    private readonly FileLocationOptions _fileLocationOptions;
    private readonly ISettingsService _settings;
    private readonly Lock _writeLock = new();

    private volatile LogLevel _cachedLogLevel;
    private StreamWriter? _writer;

    public DebugLogService(FileLocationOptions fileLocationOptions, ISettingsService settings)
    {
        _fileLocationOptions = fileLocationOptions;
        _settings = settings;
        _cachedLogLevel = _settings.LogLevel;

        InitTracing();

        _settings.LogLevelChanged += OnLogLevelChanged;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public event Action? DebugLogLoaded;

    public LogLevel MinimumLevel => _cachedLogLevel;

    public Task ClearAsync()
    {
        using (_writeLock.EnterScope())
        {
            CloseWriter();
            File.WriteAllText(_fileLocationOptions.LoggingPath, string.Empty);
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
    }

    public void Error([InterpolatedStringHandlerArgument("")] ErrorLogHandler handler)
    {
        if (!handler.IsEnabled) { return; }

        WriteTrace(handler.ToStringAndClear(), LogLevel.Error);
    }

    public void Info([InterpolatedStringHandlerArgument("")] InfoLogHandler handler)
    {
        if (!handler.IsEnabled) { return; }

        WriteTrace(handler.ToStringAndClear(), LogLevel.Information);
    }

    public async IAsyncEnumerable<string> LoadAsync()
    {
        // Read directly from the source file. Writers use File.AppendText which opens with
        // FileShare.Read, allowing concurrent readers. We open with FileShare.ReadWrite | Delete
        // to allow concurrent writers and log rotation/deletion (e.g., InitTracing deletes oversized logs).
        // This avoids the overhead of copying to a temp file and eliminates lock contention.
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
            // The log file doesn't exist yet (fresh install, before first write),
            // or was deleted between check and open (e.g., InitTracing deletes oversized logs).
            yield break;
        }

        await using (stream)
        {
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync() is { } line)
            {
                yield return line;
            }
        }
    }

    public void LoadDebugLog() => DebugLogLoaded?.Invoke();

    public void Trace([InterpolatedStringHandlerArgument("")] TraceLogHandler handler)
    {
        if (!handler.IsEnabled) { return; }

        WriteTrace(handler.ToStringAndClear(), LogLevel.Trace);
    }

    public void Warn([InterpolatedStringHandlerArgument("")] WarnLogHandler handler)
    {
        if (!handler.IsEnabled) { return; }

        WriteTrace(handler.ToStringAndClear(), LogLevel.Warning);
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private void EnsureWriter()
    {
        if (_writer is not null) { return; }

        var stream = new FileStream(
            _fileLocationOptions.LoggingPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read | FileShare.Delete,
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

    private void WriteTrace(string message, LogLevel level)
    {
        string output = $"[{DateTime.Now:o}] [{Environment.CurrentManagedThreadId}] [{level}] {message}";

        using (_writeLock.EnterScope())
        {
            EnsureWriter();
            _writer?.WriteLine(output);
        }

#if DEBUG
        System.Diagnostics.Debug.WriteLine(output);
#endif
    }
}
