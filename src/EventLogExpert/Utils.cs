// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics;

namespace EventLogExpert;

internal class Utils
{
    private static readonly long _maxLogSize = 10 * 1024 * 1024;

    internal static void InitTracing()
    {
        // Set up tracing to a file
        var eventLogExpertDataFolder = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EventLogExpert");
        var debugLogPath = Path.Join(eventLogExpertDataFolder, "debug.log");
        var fileInfo = new FileInfo(debugLogPath);
        if (fileInfo.Exists && fileInfo.Length > _maxLogSize)
        {
            fileInfo.Delete();
        }

        System.Diagnostics.Trace.Listeners.Add(new TextWriterTraceListener(debugLogPath, "myListener"));
        System.Diagnostics.Trace.AutoFlush = true;

        // Trace all exceptions
        AppDomain.CurrentDomain.FirstChanceException += (o, args) =>
        {
           Trace($"{args.Exception}");
        };
    }

    internal static void Trace(string message) => System.Diagnostics.Trace.WriteLine($"{DateTime.Now:o} {message}");
}
