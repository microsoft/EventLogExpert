// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Configuration;

public sealed class LogSinkOptions
{
    public Dictionary<string, LogLevel> Categories { get; set; } = [];
}
