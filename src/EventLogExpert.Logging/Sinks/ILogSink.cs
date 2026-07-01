// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Sinks;

public interface ILogSink
{
    void Emit(LogRecord record);

    LogLevel MinimumLevelFor(string origin);
}
