// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Logging.Routing;

/// <summary>
///     <see cref="ITraceLogger" /> implementation that streams every emitted message as a <see cref="LogRecord" />
///     through an <see cref="IProgress{T}" /> sink, forwarding operation output to a consumer (for example a UI) in real
///     time.
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
///         scenario. A <see cref="Progress{T}" /> sink captures the <see cref="SynchronizationContext" /> at construction
///         (the UI thread when registered from MAUI) and posts callbacks there, so concurrent <c>Report</c> calls from
///         worker threads end up serialized through that SC. If no <c>SynchronizationContext</c> is captured (e.g.
///         constructed from a thread-pool thread), <see cref="Progress{T}" /> instead queues each callback on the
///         thread-pool with NO serialization. Custom <see cref="IProgress{T}" /> implementations passed by other callers
///         MUST provide their own concurrency discipline; this logger does not serialize calls internally.
///     </para>
/// </remarks>
public sealed class StreamingTraceLogger(
    IProgress<LogRecord> progress,
    LogLevel minimumLevel = LogLevel.Information,
    string category = "") : ITraceLogger
{
    private readonly IProgress<LogRecord> _progress = progress ?? throw new ArgumentNullException(nameof(progress));

    public LogLevel MinimumLevel { get; } = minimumLevel;

    public void Critical([InterpolatedStringHandlerArgument("")] CriticalLogHandler handler) =>
        Emit(LogLevel.Critical, handler.ToStringAndClear());

    public void Debug([InterpolatedStringHandlerArgument("")] DebugLogHandler handler) =>
        Emit(LogLevel.Debug, handler.ToStringAndClear());

    public void Error([InterpolatedStringHandlerArgument("")] ErrorLogHandler handler) =>
        Emit(LogLevel.Error, handler.ToStringAndClear());

    public ITraceLogger ForCategory(string category)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);

        return new StreamingTraceLogger(_progress, MinimumLevel, category);
    }

    public void Information([InterpolatedStringHandlerArgument("")] InformationLogHandler handler) =>
        Emit(LogLevel.Information, handler.ToStringAndClear());

    public void Trace([InterpolatedStringHandlerArgument("")] TraceLogHandler handler) =>
        Emit(LogLevel.Trace, handler.ToStringAndClear());

    public void Warning([InterpolatedStringHandlerArgument("")] WarningLogHandler handler) =>
        Emit(LogLevel.Warning, handler.ToStringAndClear());

    private void Emit(LogLevel level, string message)
    {
        if (level < MinimumLevel) { return; }

        if (string.IsNullOrEmpty(message)) { return; }

        _progress.Report(new LogRecord(DateTime.UtcNow, level, message, category));
    }
}
