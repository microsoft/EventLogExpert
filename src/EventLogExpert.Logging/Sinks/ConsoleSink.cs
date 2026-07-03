// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Sinks;

public sealed class ConsoleSink(LogLevel minimumLevel = LogLevel.Information) : ILogSink
{
    private readonly Lock _writeLock = new();

    public void Emit(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Level < minimumLevel || string.IsNullOrEmpty(record.Message)) { return; }

        ConsoleColor? color = record.Level switch
        {
            LogLevel.Trace or LogLevel.Debug => ConsoleColor.Cyan,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
            _ => null
        };

        string line = record.Level == LogLevel.Information ? record.Message : $"[{record.Level}] {record.Message}";

        using (_writeLock.EnterScope())
        {
            // Best-effort sink: a broken stdout must never make logging throw; the color is always reset in finally.
            try
            {
                if (color is { } foreground) { Console.ForegroundColor = foreground; }

                Console.WriteLine(line);
            }
            catch (IOException) { }
            finally
            {
                if (color is not null)
                {
                    try { Console.ResetColor(); }
                    catch (IOException) { }
                }
            }
        }
    }

    public LogLevel MinimumLevelFor(string category) => minimumLevel;
}
