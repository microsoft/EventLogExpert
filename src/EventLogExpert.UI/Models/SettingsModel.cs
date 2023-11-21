// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Models;

public sealed record SettingsModel
{
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    public TimeZoneInfo TimeZoneInfo => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public IList<string> DisabledDatabases { get; set; } = [];

    public bool ShowDisplayPaneOnSelectionChange { get; set; }

    public CopyType CopyType { get; set; } = CopyType.Full;

    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    public bool IsPrereleaseEnabled { get; set; }
}
