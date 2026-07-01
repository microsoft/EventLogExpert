// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests.Routing;

internal sealed class RecordingSink(Func<string, LogLevel> minimumForOrigin) : ILogSink
{
    public List<LogRecord> Received { get; } = [];

    public List<LogRecord> Written { get; } = [];

    public void Emit(LogRecord record)
    {
        Received.Add(record);

        if (record.Level >= MinimumLevelFor(record.Origin)) { Written.Add(record); }
    }

    public LogLevel MinimumLevelFor(string origin) => minimumForOrigin(origin);
}
