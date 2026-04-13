// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Eventing.Helpers;

public interface ITraceLogger
{
    LogLevel MinimumLevel { get; }

    void Trace([InterpolatedStringHandlerArgument("")] TraceLogHandler handler);

    void Debug([InterpolatedStringHandlerArgument("")] DebugLogHandler handler);

    void Info([InterpolatedStringHandlerArgument("")] InfoLogHandler handler);

    void Warn([InterpolatedStringHandlerArgument("")] WarnLogHandler handler);

    void Error([InterpolatedStringHandlerArgument("")] ErrorLogHandler handler);

    void Critical([InterpolatedStringHandlerArgument("")] CriticalLogHandler handler);
}
