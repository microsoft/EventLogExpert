// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.UI.Options;
using System.Diagnostics;

namespace EventLogExpert.UI.Services;

public class DebugLogService : ITraceLogger
{
    private static readonly long _maxLogSize = 10 * 1024 * 1024;

    private readonly FileLocationOptions _fileLocationOptions;

    public DebugLogService(FileLocationOptions fileLocationOptions)
    {
        _fileLocationOptions = fileLocationOptions;
        InitTracing();
    }

    private void InitTracing()
    {
        // Set up tracing to a file
        var fileInfo = new FileInfo(_fileLocationOptions.LoggingPath);

        if (fileInfo.Exists && fileInfo.Length > _maxLogSize)
        {
            fileInfo.Delete();
        }

        System.Diagnostics.Trace.Listeners.Add(new TextWriterTraceListener(_fileLocationOptions.LoggingPath, "myListener"));
        System.Diagnostics.Trace.AutoFlush = true;

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
                Trace($"{args.Exception}");
                firstChanceSemaphore.Release();
            }
        };
    }

    public void Trace(string message) =>
        System.Diagnostics.Trace.WriteLine($"{DateTime.Now:o} {Environment.CurrentManagedThreadId} {message}");
}
