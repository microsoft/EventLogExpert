// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Logging;

public sealed class LocalizingLogProgress(INeutralTextResolver resolver, IProgress<LogRecord> inner) : IProgress<LogRecord>
{
    public void Report(LogRecord value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.MessageKey is { Length: > 0 } key && string.IsNullOrEmpty(value.Message))
        {
            inner.Report(value with { Message = resolver.Resolve(key, value.MessageArgs ?? []) });
            return;
        }

        inner.Report(value);
    }
}
