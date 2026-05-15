// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Clipboard;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Settings;

public interface ISettingsPreferencesProvider
{
    EventCopyFormat KeyboardCopyFormatPreference { get; set; }

    LogLevel LogLevelPreference { get; set; }

    bool PreReleasePreference { get; set; }

    Theme ThemePreference { get; set; }

    string TimeZonePreference { get; set; }
}
