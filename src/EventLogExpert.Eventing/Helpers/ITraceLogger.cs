// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.Eventing.Helpers;

public interface ITraceLogger
{
    void Trace(string message, LogLevel level = LogLevel.Information);
}
