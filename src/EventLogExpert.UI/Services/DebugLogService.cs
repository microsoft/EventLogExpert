// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace EventLogExpert.UI.Services;

public sealed class DebugLogService : ITraceLogger
{
    private const long MaxLogSize = 10 * 1024 * 1024;
    private static readonly ReaderWriterLockSlim s_loggingFileLock = new();

    private readonly FileLocationOptions _fileLocationOptions;
    private readonly IState<SettingsState> _settingsState;

    public DebugLogService(FileLocationOptions fileLocationOptions, IState<SettingsState> settingsState)
    {
        _fileLocationOptions = fileLocationOptions;
        _settingsState = settingsState;

        InitTracing();
    }

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

    public void Trace(string message, LogLevel level = LogLevel.Information)
    {
        if (level < _settingsState.Value.Config.LogLevel) { return; }

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
        // Set up tracing to a file
        var fileInfo = new FileInfo(_fileLocationOptions.LoggingPath);

        if (fileInfo is { Exists: true, Length: > MaxLogSize })
        {
            fileInfo.Delete();
        }

        // Disabling first chance exception logging unless LogLevel is at Trace level
        // since it is noisy and is logging double information for exceptions that are being handled.
        // This also saves any potential performance hit for when we aren't worried about tracking first chance exceptions.
        if (_settingsState.Value.Config.LogLevel > LogLevel.Trace) { return; }

        var firstChanceSemaphore = new SemaphoreSlim(1);

        // Trace all exceptions
        AppDomain.CurrentDomain.FirstChanceException += (o, args) =>
        {
            // Unless we're already tracing one.
            //
            // When an instance of EventLogExpert is launched by double-clicking an evtx file
            // and no databases are present, we get into a condition where we're trying to trace
            // a first-chance exception, and the act of tracing causes another first-chance
            // exception, which we then try to trace, until we hit a stack overflow.
            if (firstChanceSemaphore.Wait(100))
            {
                Trace($"{args.Exception}", LogLevel.Trace);
                firstChanceSemaphore.Release();
            }
        };
    }
}
