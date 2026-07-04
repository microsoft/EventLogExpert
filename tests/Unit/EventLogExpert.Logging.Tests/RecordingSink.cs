// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests;

    internal sealed class RecordingSink(Func<string, LogLevel> minimumForCategory) : ILogSink
    {
        public List<LogRecord> Received { get; } = [];

        public List<LogRecord> Written { get; } = [];

        public void Emit(LogRecord record)
        {
            Received.Add(record);

            if (record.Level >= MinimumLevelFor(record.Category)) { Written.Add(record); }
        }

        public LogLevel MinimumLevelFor(string category) => minimumForCategory(category);
    }
