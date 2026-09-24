// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Abstractions;

public interface IOperationLog
{
    ITraceLogger Trace { get; }

    void Data(LogLevel level, string text);

    IOperationLog ForCategory(string category);

    void User(LogLevel level, LocalizableText message);

    void User(LogLevel level, LocalizableText message, Exception diagnostic);
}
