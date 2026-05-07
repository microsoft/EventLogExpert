// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Logging;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class LoggerUtils
{
    internal sealed class RecordingTraceLogger : ITraceLogger
    {
        public List<string> CriticalMessages { get; } = [];

        public List<string> DebugMessages { get; } = [];

        public List<string> ErrorMessages { get; } = [];

        public List<string> InfoMessages { get; } = [];

        public LogLevel MinimumLevel => LogLevel.Trace;

        public List<string> TraceMessages { get; } = [];

        public List<string> WarnMessages { get; } = [];

        public void Critical(CriticalLogHandler handler) => CriticalMessages.Add(handler.ToStringAndClear());

        public void Debug(DebugLogHandler handler) => DebugMessages.Add(handler.ToStringAndClear());

        public void Error(ErrorLogHandler handler) => ErrorMessages.Add(handler.ToStringAndClear());

        public void Info(InfoLogHandler handler) => InfoMessages.Add(handler.ToStringAndClear());

        public void Trace(TraceLogHandler handler) => TraceMessages.Add(handler.ToStringAndClear());

        public void Warn(WarnLogHandler handler) => WarnMessages.Add(handler.ToStringAndClear());
    }
}
