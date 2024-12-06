// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Interfaces;

public interface ISettingsService
{
    CopyType CopyType { get; set; }

    Action? CopyTypeChanged { get; set; }

    bool IsPreReleaseEnabled { get; set; }

    Action? Loaded { get; set; }

    LogLevel LogLevel { get; set; }

    Action? LogLevelChanged { get; set; }

    bool ShowDisplayPaneOnSelectionChange { get; set; }

    EventHandler<TimeZoneInfo>? TimeZoneChanged { get; set; }

    string TimeZoneId { get; set; }

    TimeZoneInfo TimeZoneInfo { get; }

    void Load();
}
