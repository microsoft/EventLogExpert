// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Routing;

public sealed class DispatchingTraceLogger(
    IReadOnlyList<ILogSink> sinks,
    string category,
    ProcessOrigin processOrigin) : ITraceLogger
{
    public LogLevel MinimumLevel
    {
        get
        {
            LogLevel aggregate = LogLevel.None;

            foreach (ILogSink sink in sinks)
            {
                LogLevel level = sink.MinimumLevelFor(category);

                if (level < aggregate) { aggregate = level; }
            }

            return aggregate;
        }
    }

    public void Critical(CriticalLogHandler handler) => Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Critical);

    public void Debug(DebugLogHandler handler) => Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Debug);

    public void Error(ErrorLogHandler handler) => Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Error);

    public ITraceLogger ForCategory(string category) => new DispatchingTraceLogger(sinks, category, processOrigin);

    public void Information(InformationLogHandler handler) => Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Information);

    public void Trace(TraceLogHandler handler) => Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Trace);

    public void Warning(WarningLogHandler handler) => Dispatch(handler.IsEnabled, handler.ToStringAndClear(), LogLevel.Warning);

    private void Dispatch(bool isEnabled, string message, LogLevel level)
    {
        if (!isEnabled || string.IsNullOrEmpty(message)) { return; }

        LogRecord record = new(DateTime.UtcNow, level, message, category, processOrigin);

        foreach (ILogSink sink in sinks)
        {
            sink.Emit(record);
        }
    }
}
