// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.DatabaseTools.IntegrationTests.TestUtils;

internal sealed class CapturingTraceLogger : IOperationLog
{
    private readonly SharedResourceNeutralResolver _resolver = new();

    public List<CapturedLogEntry> Entries { get; } = [];

    public ITraceLogger Trace => new CapturingTrace(Entries);

    public bool Contains(LogLevel level, params string[] fragments) =>
        Entries.Any(entry =>
            entry.Level == level &&
            fragments.All(fragment => entry.Message.Contains(fragment, StringComparison.Ordinal)));

    public void Data(LogLevel level, string text) => Add(level, text);

    public IOperationLog ForCategory(string category) => this;

    public void User(LogLevel level, LocalizableText message) =>
        Entries.Add(new CapturedLogEntry(level,
            _resolver.Resolve(message.Key, message.Args),
            message.Key,
            message.Args,
            DebugDetail: null));

    public void User(LogLevel level, LocalizableText message, Exception diagnostic) =>
        Entries.Add(new CapturedLogEntry(level,
            _resolver.Resolve(message.Key, message.Args),
            message.Key,
            message.Args,
            diagnostic.ToString()));

    private void Add(LogLevel level, string message)
    {
        if (!string.IsNullOrEmpty(message)) { Entries.Add(new CapturedLogEntry(level, message)); }
    }

    private sealed class CapturingTrace(List<CapturedLogEntry> entries) : ITraceLogger
    {
        public LogLevel MinimumLevel => LogLevel.Trace;

        public void Critical(CriticalLogHandler handler) => Add(LogLevel.Critical, handler.ToStringAndClear());

        public void Debug(DebugLogHandler handler) => Add(LogLevel.Debug, handler.ToStringAndClear());

        public void Error(ErrorLogHandler handler) => Add(LogLevel.Error, handler.ToStringAndClear());

        public void Information(InformationLogHandler handler) => Add(LogLevel.Information, handler.ToStringAndClear());

        public void Trace(TraceLogHandler handler) => Add(LogLevel.Trace, handler.ToStringAndClear());

        public void Warning(WarningLogHandler handler) => Add(LogLevel.Warning, handler.ToStringAndClear());

        private void Add(LogLevel level, string message)
        {
            if (!string.IsNullOrEmpty(message)) { entries.Add(new CapturedLogEntry(level, message)); }
        }
    }
}

internal sealed record CapturedLogEntry(
    LogLevel Level,
    string Message,
    string? Key = null,
    IReadOnlyList<string>? Args = null,
    string? DebugDetail = null);
