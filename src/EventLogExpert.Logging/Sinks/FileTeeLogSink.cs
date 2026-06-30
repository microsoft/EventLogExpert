// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Sinks;

/// <summary>
///     Wraps an inner <see cref="IProgress{T}" /> sink and, for every record at <see cref="LogLevel.Warning" /> or
///     above, also writes the line to a persistent <see cref="ITraceLogger" /> (the application file logger) so operation
///     failures survive in the on-disk debug log instead of living only in the transient UI list. Records below
///     <see cref="LogLevel.Warning" /> are forwarded to the inner sink only, keeping high-volume Information progress out
///     of the file log.
/// </summary>
/// <remarks>
///     The file write is best-effort: a logging failure (for example a disk-full condition or an abandoned mutex)
///     must never break the running operation or the UI, so it is swallowed. The record is delivered to the inner sink in
///     every case and stays visible in the UI log. The file log is a diagnostic aid, not a system of record.
/// </remarks>
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
                // Best-effort tee: never let a file-logging failure break the operation or the UI. The record is
                // still delivered to the inner sink below and remains visible in the UI log.
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
