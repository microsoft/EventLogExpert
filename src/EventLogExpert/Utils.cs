// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics;

namespace EventLogExpert;

internal static class Utils
{
    private static readonly long _maxLogSize = 10 * 1024 * 1024;

    public static string DatabasePath => Path.Join(FileSystem.AppDataDirectory, "Databases");

    public static string LoggingPath => Path.Join(FileSystem.AppDataDirectory, "debug.log");

    public static string SettingsPath => Path.Join(FileSystem.AppDataDirectory, "settings.json");

    internal static DateTime ConvertTimeZone(this DateTime time, TimeZoneInfo? destinationTime) =>
        destinationTime is null ? time : TimeZoneInfo.ConvertTimeFromUtc(time, destinationTime);

    internal static bool HasProviderDatabases()
    {
        try
        {
            return Directory.EnumerateFiles(DatabasePath, "*.db").Any();
        }
        catch
        {
            return false;
        }
    }

    internal static void InitTracing()
    {
        // Set up tracing to a file
        var fileInfo = new FileInfo(LoggingPath);

        if (fileInfo.Exists && fileInfo.Length > _maxLogSize)
        {
            fileInfo.Delete();
        }

        System.Diagnostics.Trace.Listeners.Add(new TextWriterTraceListener(LoggingPath, "myListener"));
        System.Diagnostics.Trace.AutoFlush = true;

        // Trace all exceptions
        AppDomain.CurrentDomain.FirstChanceException += (o, args) =>
        {
            Trace($"{args.Exception}");
        };
    }

    internal static void Trace(string message) => System.Diagnostics.Trace.WriteLine($"{DateTime.Now:o} {message}");
}
