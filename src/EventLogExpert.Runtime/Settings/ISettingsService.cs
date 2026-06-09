// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Clipboard;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.Settings;

public interface ISettingsService
{
    EventCopyFormat CopyFormat { get; set; }

    Action? CopyFormatChanged { get; set; }

    bool HasEverEnabledPreRelease { get; }

    bool IsPreReleaseEnabled { get; set; }

    LogLevel LogLevel { get; set; }

    Action? LogLevelChanged { get; set; }

    Theme Theme { get; set; }

    Action? ThemeChanged { get; set; }

    EventHandler<TimeZoneInfo>? TimeZoneChanged { get; set; }

    string TimeZoneId { get; set; }

    TimeZoneInfo TimeZoneInfo { get; }
}
