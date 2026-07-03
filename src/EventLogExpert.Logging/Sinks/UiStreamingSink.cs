// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Sinks;

public sealed class UiStreamingSink(IProgress<LogRecord> progress, LogLevel minimumLevel) : ILogSink
{
    private readonly IProgress<LogRecord> _progress = progress ?? throw new ArgumentNullException(nameof(progress));

    public LogLevel MinimumLevelFor(string category) => minimumLevel;

    public void Emit(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Level < minimumLevel) { return; }

        _progress.Report(record);
    }
}
