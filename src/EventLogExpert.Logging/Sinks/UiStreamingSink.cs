// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Sinks;

public sealed class UiStreamingSink(IProgress<LogRecord> progress, LogLevel minimumLevel) : ILogSink
{
    public LogLevel MinimumLevelFor(string category) => minimumLevel;

    public void Emit(LogRecord record)
    {
        if (record.Level < minimumLevel) { return; }

        progress.Report(record);
    }
}
