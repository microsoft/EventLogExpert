// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Sinks;

public sealed class FileTeeLogSink(IProgress<LogRecord> inner, ITraceLogger fileLogger) : IProgress<LogRecord>
{
    public void Report(LogRecord value)
    {
        if (value.Level >= LogLevel.Warning)
        {
            try
            {
                WriteToFile(value);
            }
            catch
            {
                // Best-effort tee: file logging must never break the operation or UI delivery.
            }
        }

        inner.Report(value);
    }

    private void WriteToFile(LogRecord value)
    {
        switch (value.Level)
        {
            case LogLevel.Warning:
                fileLogger.Warning($"{value.Message}");
                break;
            case LogLevel.Error:
                fileLogger.Error($"{value.Message}");
                break;
            case LogLevel.Critical:
                fileLogger.Critical($"{value.Message}");
                break;
        }
    }
}
