// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Sinks;

public sealed class UIStreamingSink(IProgress<LogRecord> progress, LogLevel minimumLevel) : ILogSink
{
    private readonly IProgress<LogRecord> _progress = progress ?? throw new ArgumentNullException(nameof(progress));

    public void Emit(LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Level < minimumLevel) { return; }

        // Diagnostic records (the Trace channel) carry unlocalized English and raw exception text; they belong in the
        // debug/file log only, never the localized user-facing operation log.
        if (record.Audience == LogAudience.Diagnostic) { return; }

        _progress.Report(record with { DebugDetail = null });
    }

    public LogLevel MinimumLevelFor(string category) => minimumLevel;
}
