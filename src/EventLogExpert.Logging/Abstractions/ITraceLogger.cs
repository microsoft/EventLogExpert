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

    /// <summary>
    ///     Returns a logger that stamps <paramref name="category" /> on every emitted record; the default returns this
    ///     same instance unchanged, so an implementation that supports categories MUST override it or categorization is
    ///     silently lost.
    /// </summary>
    ITraceLogger ForCategory(string category) => this;

    void Information([InterpolatedStringHandlerArgument("")] InformationLogHandler handler);

    void Trace([InterpolatedStringHandlerArgument("")] TraceLogHandler handler);

    void Warning([InterpolatedStringHandlerArgument("")] WarningLogHandler handler);
}
