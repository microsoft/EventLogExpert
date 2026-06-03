// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace EventLogExpert.ElevationHelper.IntegrationTests.TestUtils;

internal sealed class IntegrationTraceLogger : ITraceLogger
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public LogLevel MinimumLevel => LogLevel.Trace;

    public void Critical(CriticalLogHandler handler) => Messages.Enqueue($"[CRT] {handler.ToStringAndClear()}");

    public void Debug(DebugLogHandler handler) => Messages.Enqueue($"[DBG] {handler.ToStringAndClear()}");

    public void Error(ErrorLogHandler handler) => Messages.Enqueue($"[ERR] {handler.ToStringAndClear()}");

    public void Information(InformationLogHandler handler) => Messages.Enqueue($"[INF] {handler.ToStringAndClear()}");

    public void Trace(TraceLogHandler handler) => Messages.Enqueue($"[TRC] {handler.ToStringAndClear()}");

    public void Warning(WarningLogHandler handler) => Messages.Enqueue($"[WRN] {handler.ToStringAndClear()}");
}

internal sealed class ListProgress<T> : IProgress<T>
{
    private readonly List<T> _entries = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<T> Entries
    {
        get
        {
            lock (_lock) { return [.. _entries]; }
        }
    }

    public void Report(T value)
    {
        lock (_lock) { _entries.Add(value); }
    }
}
