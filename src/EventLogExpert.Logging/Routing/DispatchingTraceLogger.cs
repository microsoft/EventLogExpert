// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Logging.Routing;

public sealed class DispatchingTraceLogger(
    IReadOnlyList<ILogSink> sinks,
    string category,
    ProcessOrigin processOrigin) : ITraceLogger
{
    private readonly IReadOnlyList<ILogSink> _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));

    public LogLevel MinimumLevel
    {
        get
        {
            LogLevel aggregate = LogLevel.None;

            foreach (ILogSink sink in _sinks)
            {
                LogLevel level = sink.MinimumLevelFor(category);

                if (level < aggregate) { aggregate = level; }
            }

            return aggregate;
        }
    }

    public void Critical([InterpolatedStringHandlerArgument("")] CriticalLogHandler handler) =>
        Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Critical);

    public void Debug([InterpolatedStringHandlerArgument("")] DebugLogHandler handler) =>
        Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Debug);

    public void Error([InterpolatedStringHandlerArgument("")] ErrorLogHandler handler) =>
        Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Error);

    public ITraceLogger ForCategory(string category)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);

        return new DispatchingTraceLogger(_sinks, category, processOrigin);
    }

    public void Information([InterpolatedStringHandlerArgument("")] InformationLogHandler handler) =>
        Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Information);

    public void Trace([InterpolatedStringHandlerArgument("")] TraceLogHandler handler) =>
        Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Trace);

    public void Warning([InterpolatedStringHandlerArgument("")] WarningLogHandler handler) =>
        Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Warning);

    private void Dispatch(bool isEnabled, string message, LogLevel level)
    {
        if (!isEnabled || string.IsNullOrEmpty(message)) { return; }

        LogRecord record = new(DateTime.UtcNow, level, message, category, processOrigin);

        foreach (ILogSink sink in _sinks)
        {
            try
            {
                sink.Emit(record);
            }
            catch
            {
                // Best-effort: a sink fault (e.g. transient file I/O) must not crash the caller or skip the other sinks.
            }
        }
    }
}
