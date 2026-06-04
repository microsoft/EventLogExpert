// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.DatabaseTools;

/// <summary>
///     <see cref="ITraceLogger" /> implementation that streams every emitted message as a
///     <see cref="DatabaseToolsLogEntry" /> through an <see cref="IProgress{T}" /> sink. Used by
///     <see cref="DatabaseToolsService" /> to forward operation output to the UI in real time.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="MinimumLevel" /> defaults to <see cref="LogLevel.Information" /> so noisy Trace/Debug messages
///         from underlying provider/reader code stay out of the UI. The interpolated-string handlers (e.g.
///         <see cref="TraceLogHandler" />) read this property at construction time and short-circuit before allocating any
///         strings when the call site is below the threshold, so filtered calls are effectively free.
///     </para>
///     <para>
///         Callers MUST pass an <see cref="IProgress{T}" /> sink whose concurrency model matches the production
///         scenario. <see cref="DatabaseToolsService" /> always wires <see cref="Progress{T}" />, which captures the
///         <see cref="SynchronizationContext" /> at construction (the UI thread when registered from MAUI) and posts
///         callbacks there — concurrent <c>Report</c> calls from worker threads end up serialized through that SC. If no
///         <c>SynchronizationContext</c> is captured (e.g. constructed from a thread-pool thread),
///         <see cref="Progress{T}" /> instead queues each callback on the thread-pool with NO serialization. Custom
///         <see cref="IProgress{T}" /> implementations passed by other callers MUST provide their own concurrency
///         discipline — this logger does not serialize calls internally.
///     </para>
/// </remarks>
internal sealed class StreamingTraceLogger(IProgress<DatabaseToolsLogEntry> sink, LogLevel minimumLevel = LogLevel.Information) : ITraceLogger
{
    public LogLevel MinimumLevel { get; } = minimumLevel;

    public void Critical(CriticalLogHandler handler) => Emit(LogLevel.Critical, handler.ToStringAndClear());

    public void Debug(DebugLogHandler handler) => Emit(LogLevel.Debug, handler.ToStringAndClear());

    public void Error(ErrorLogHandler handler) => Emit(LogLevel.Error, handler.ToStringAndClear());

    public void Information(InformationLogHandler handler) => Emit(LogLevel.Information, handler.ToStringAndClear());

    public void Trace(TraceLogHandler handler) => Emit(LogLevel.Trace, handler.ToStringAndClear());

    public void Warning(WarningLogHandler handler) => Emit(LogLevel.Warning, handler.ToStringAndClear());

    private void Emit(LogLevel level, string message)
    {
        if (level < MinimumLevel) { return; }

        if (string.IsNullOrEmpty(message)) { return; }

        sink.Report(new DatabaseToolsLogEntry(DateTime.UtcNow, level, message));
    }
}
