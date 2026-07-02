// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Configuration;

public sealed class LoggingOptions
{
    public const string FileSink = "DebugFileSink";

    public Dictionary<string, LogSinkOptions> Sinks { get; set; } = [];

    public static void ApplyShippedDefaults(LoggingOptions options)
    {
        options.Sinks = new Dictionary<string, LogSinkOptions>(StringComparer.Ordinal)
        {
            [FileSink] = new LogSinkOptions
            {
                Categories = new Dictionary<string, LogLevel>(StringComparer.Ordinal)
                {
                    [LogCategories.Database] = LogLevel.Warning,
                    [LogCategories.DatabaseTools] = LogLevel.Warning,
                    [LogCategories.Offline] = LogLevel.Warning,
                    [LogCategories.Resolution] = LogLevel.Warning
                }
            }
        };
    }

    public static LoggingOptions CreateShippedDefaults()
    {
        var options = new LoggingOptions();
        ApplyShippedDefaults(options);

        return options;
    }
}
