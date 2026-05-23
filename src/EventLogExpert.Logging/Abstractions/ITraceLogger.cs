// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Logging.Abstractions;

public interface ITraceLogger
{
    LogLevel MinimumLevel { get; }

    void Critical([InterpolatedStringHandlerArgument("")] CriticalLogHandler handler);

    void Debug([InterpolatedStringHandlerArgument("")] DebugLogHandler handler);

    void Error([InterpolatedStringHandlerArgument("")] ErrorLogHandler handler);

    void Information([InterpolatedStringHandlerArgument("")] InformationLogHandler handler);

    void Trace([InterpolatedStringHandlerArgument("")] TraceLogHandler handler);

    void Warning([InterpolatedStringHandlerArgument("")] WarningLogHandler handler);
}
