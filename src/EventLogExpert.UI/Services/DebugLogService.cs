// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Options;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace EventLogExpert.UI.Services;

public sealed partial class DebugLogService : ITraceLogger, IFileLogger, IDisposable
{
    private const long MaxLogSize = 10 * 1024 * 1024;

    private static readonly ReaderWriterLockSlim s_loggingFileLock = new();

    private readonly FileLocationOptions _fileLocationOptions;
    private readonly Lock _firstChanceLock = new();
    private readonly ISettingsService _settings;

    private bool _firstChanceLoggingEnabled;

    public DebugLogService(FileLocationOptions fileLocationOptions, ISettingsService settings)
    {
        _fileLocationOptions = fileLocationOptions;
        _settings = settings;

        InitTracing();

        _settings.LogLevelChanged += OnLogLevelChanged;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public Action? DebugLogLoaded { get; set; }

    public async Task ClearAsync()
    {
        s_loggingFileLock.EnterWriteLock();

        try
        {
            await File.WriteAllTextAsync(_fileLocationOptions.LoggingPath, string.Empty);
        }
        finally
        {
            s_loggingFileLock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

        if (_firstChanceLoggingEnabled)
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
        }

        _settings.LogLevelChanged -= OnLogLevelChanged;
    }

    public async IAsyncEnumerable<string> LoadAsync()
    {
        s_loggingFileLock.EnterReadLock();

        try
        {
            await foreach (var line in File.ReadLinesAsync(_fileLocationOptions.LoggingPath))
            {
                yield return line;
            }
        }
        finally
        {
            s_loggingFileLock.ExitReadLock();
        }
    }

    public void LoadDebugLog() => DebugLogLoaded?.Invoke();

    public void Trace(string message, LogLevel level = LogLevel.Information)
    {
        if (level < _settings.LogLevel) { return; }

        string output = $"[{DateTime.Now:o}] [{Environment.CurrentManagedThreadId}] [{level}] {message}";

        s_loggingFileLock.EnterWriteLock();

        try
        {
            using StreamWriter writer = File.AppendText(_fileLocationOptions.LoggingPath);

            writer.WriteLine(output);
        }
        finally
        {
            s_loggingFileLock.ExitWriteLock();
        }

#if DEBUG
        Debug.WriteLine(output);
#endif
    }

    private void InitTracing()
    {
        _firstChanceLoggingEnabled = false;

        // Set up tracing to a file
        var fileInfo = new FileInfo(_fileLocationOptions.LoggingPath);

        if (fileInfo is { Exists: true, Length: > MaxLogSize })
        {
            fileInfo.Delete();
        }

        // Disabling first chance exception logging unless LogLevel is at Trace level
        // since it is noisy and is logging double information for exceptions that are being handled.
        // This also saves any potential performance hit for when we aren't worried about tracking first chance exceptions.
        if (_settings.LogLevel > LogLevel.Trace) { return; }

        _firstChanceLoggingEnabled = true;

        // Trace all exceptions
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    private void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        // Unless we're already tracing one.
        //
        // When an instance of EventLogExpert is launched by double-clicking an evtx file
        // and no databases are present, we get into a condition where we're trying to trace
        // a first-chance exception, and the act of tracing causes another first-chance
        // exception, which we then try to trace, until we hit a stack overflow.

        if (!_firstChanceLock.TryEnter(100)) { return; }

        try
        {
            Trace($"{e.Exception}", LogLevel.Trace);
        }
        finally
        {
            _firstChanceLock.Exit();
        }
    }

    private void OnLogLevelChanged()
    {
        if (_firstChanceLoggingEnabled)
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
        }

        InitTracing();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Trace($"Unhandled Exception: {e.ExceptionObject}", LogLevel.Critical);
    }
}
