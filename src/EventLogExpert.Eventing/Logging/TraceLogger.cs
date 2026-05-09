// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.Eventing.Logging;

public class TraceLogger(LogLevel loggingLevel) : ITraceLogger
{
    public LogLevel MinimumLevel => loggingLevel;

    public void Critical(CriticalLogHandler handler) => Write(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Critical);

    public void Debug(DebugLogHandler handler) => Write(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Debug);

    public void Error(ErrorLogHandler handler) => Write(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Error);

    public void Information(InformationLogHandler handler) => Write(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Information);

    public void Trace(TraceLogHandler handler) => Write(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Trace);

    public void Warning(WarningLogHandler handler) => Write(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Warning);

    private static void Write(bool isEnabled, string message, LogLevel level)
    {
        if (!isEnabled) { return; }

        switch (level)
        {
            case LogLevel.Trace:
            case LogLevel.Debug:
                Console.ForegroundColor = ConsoleColor.Cyan;

                Console.WriteLine($"[{level}] {message}");
                break;
            case LogLevel.Information:
                Console.WriteLine($"{message}");

                break;
            case LogLevel.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[{level}] {message}");

                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[{level}] {message}");

                break;
        }

        Console.ResetColor();
    }
}
