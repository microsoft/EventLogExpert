// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Runtime.DebugLog;

public interface IOperationLogSinkFactory
{
    IProgress<LogRecord> Create(IProgress<LogRecord> uiProgress, string category, bool verbose);
}
