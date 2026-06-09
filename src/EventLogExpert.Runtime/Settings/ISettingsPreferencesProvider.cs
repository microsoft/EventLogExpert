// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Clipboard;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.Settings;

public interface ISettingsPreferencesProvider
{
    bool HasEverEnabledPreReleasePreference { get; set; }

    EventCopyFormat KeyboardCopyFormatPreference { get; set; }

    LogLevel LogLevelPreference { get; set; }

    bool PreReleasePreference { get; set; }

    Theme ThemePreference { get; set; }

    string TimeZonePreference { get; set; }
}
