// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Settings;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Adapters.Settings;

internal sealed class SettingsPreferencesAdapter : ISettingsPreferencesProvider
{
    private const string KeyboardCopyFormat = "keyboard-copy-format";
    private const string LoggingLevel = "logging-level";
    private const string PreReleaseEnabled = "prerelease-enabled";
    private const string ThemeName = "theme";
    private const string TimeZone = "timezone";

    public EventCopyFormat KeyboardCopyFormatPreference
    {
        get => Enum.TryParse(Preferences.Default.Get(KeyboardCopyFormat, nameof(EventCopyFormat.Full)),
            out EventCopyFormat value) ?
            value : EventCopyFormat.Full;
        set => Preferences.Default.Set(KeyboardCopyFormat, value.ToString());
    }

    public LogLevel LogLevelPreference
    {
        get => Enum.TryParse(Preferences.Default.Get(LoggingLevel, nameof(LogLevel.Information)),
            out LogLevel value) ?
            value : LogLevel.Information;
        set => Preferences.Default.Set(LoggingLevel, value.ToString());
    }

    public bool PreReleasePreference
    {
        get => Preferences.Default.Get(PreReleaseEnabled, false);
        set => Preferences.Default.Set(PreReleaseEnabled, value);
    }

    public Theme ThemePreference
    {
        get => Enum.TryParse(Preferences.Default.Get(ThemeName, nameof(Theme.System)),
            out Theme value) ?
            value : Theme.System;
        set => Preferences.Default.Set(ThemeName, value.ToString());
    }

    public string TimeZonePreference
    {
        get => Preferences.Default.Get(TimeZone, TimeZoneInfo.Local.Id);
        set => Preferences.Default.Set(TimeZone, value);
    }
}
