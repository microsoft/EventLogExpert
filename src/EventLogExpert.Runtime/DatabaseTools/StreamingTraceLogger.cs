// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.DatabaseTools;

/// <summary>
///     <see cref="ITraceLogger"/> implementation that streams every emitted message as a
///     <see cref="DatabaseToolsLogEntry"/> through an <see cref="IProgress{T}"/> sink. Used by
///     <see cref="DatabaseToolsService"/> to forward operation output to the UI in real time.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="MinimumLevel"/> is fixed at <see cref="LogLevel.Trace"/>: each interpolated-string-handler
///         constructor reads this property to set its internal <c>IsEnabled</c> flag — at any higher minimum, the
///         handler short-circuits and <see cref="LogHandlerCore.ToStringAndClear"/> returns an empty string.
///     </para>
///     <para>
///         <see cref="IProgress{T}.Report"/> is thread-safe: <see cref="Progress{T}"/> captures the
///         <see cref="SynchronizationContext"/> at construction (the UI thread when registered from MAUI) and posts
///         callbacks there; off-the-UI-thread callers (test code, raw thread-pool consumers) fall through to the
///         thread-pool. No manual locking is required.
///     </para>
/// </remarks>
internal sealed class StreamingTraceLogger(IProgress<DatabaseToolsLogEntry> sink) : ITraceLogger
{
    public LogLevel MinimumLevel => LogLevel.Trace;

    public void Critical(CriticalLogHandler handler) => Emit(LogLevel.Critical, handler.ToStringAndClear());

    public void Debug(DebugLogHandler handler) => Emit(LogLevel.Debug, handler.ToStringAndClear());

    public void Error(ErrorLogHandler handler) => Emit(LogLevel.Error, handler.ToStringAndClear());

    public void Information(InformationLogHandler handler) => Emit(LogLevel.Information, handler.ToStringAndClear());

    public void Trace(TraceLogHandler handler) => Emit(LogLevel.Trace, handler.ToStringAndClear());

    public void Warning(WarningLogHandler handler) => Emit(LogLevel.Warning, handler.ToStringAndClear());

    private void Emit(LogLevel level, string message) =>
        sink.Report(new DatabaseToolsLogEntry(DateTime.UtcNow, level, message));
}
