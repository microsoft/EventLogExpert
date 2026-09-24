// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Loggers;

public sealed class StreamingOperationLog(
    IProgress<LogRecord> progress,
    LogLevel minimumLevel = LogLevel.Information,
    string category = "") : IOperationLog
{
    private readonly IProgress<LogRecord> _progress = progress ?? throw new ArgumentNullException(nameof(progress));

    public ITraceLogger Trace => new StreamingTraceLogger(_progress, minimumLevel, category);

    public void Data(LogLevel level, string text) =>
        Emit(level, text, messageKey: null, messageArgs: null, debugDetail: null);

    public IOperationLog ForCategory(string category)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);

        return new StreamingOperationLog(_progress, minimumLevel, category);
    }

    public void User(LogLevel level, LocalizableText message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Emit(level, message: string.Empty, message.Key, message.Args, debugDetail: null);
    }

    public void User(LogLevel level, LocalizableText message, Exception diagnostic)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(diagnostic);

        Emit(level, message: string.Empty, message.Key, message.Args, diagnostic.ToString());
    }

    private void Emit(
        LogLevel level,
        string message,
        string? messageKey,
        IReadOnlyList<string>? messageArgs,
        string? debugDetail)
    {
        if (level < minimumLevel) { return; }

        if (string.IsNullOrEmpty(message) && string.IsNullOrEmpty(messageKey)) { return; }

        _progress.Report(new LogRecord(DateTime.UtcNow,
            level,
            message,
            category,
            MessageKey: messageKey,
            MessageArgs: messageArgs,
            DebugDetail: debugDetail));
    }
}
