// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.EventDbTool;

public class TraceLogger(LogLevel loggingLevel) : ITraceLogger
{
    public void Trace(string message, LogLevel level = LogLevel.Information)
    {
        if (level < loggingLevel) { return; }

        Console.WriteLine($"[{DateTime.Now:o}] [{Environment.CurrentManagedThreadId}] [{level}] {message}");
    }
}
