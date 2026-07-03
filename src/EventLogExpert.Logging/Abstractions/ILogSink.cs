// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Abstractions;

public interface ILogSink
{
    void Emit(LogRecord record);

    LogLevel MinimumLevelFor(string category);
}
