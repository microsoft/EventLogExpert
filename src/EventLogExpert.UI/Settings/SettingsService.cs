// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Clipboard;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Settings;

internal sealed class SettingsService(ISettingsPreferencesProvider preferences) : ISettingsService
{
    private readonly ISettingsPreferencesProvider _preferences = preferences;

    private EventCopyFormat? _copyFormat;
    private bool? _isPreReleaseEnabled;
    private LogLevel? _logLevel;
    private Theme? _theme;
    private string? _timeZoneId;

    public EventCopyFormat CopyFormat
    {
        get
        {
            _copyFormat ??= _preferences.KeyboardCopyFormatPreference;

            return _copyFormat ?? EventCopyFormat.Default;
        }
        set
        {
            if (_copyFormat == value) { return; }

            _copyFormat = value;
            _preferences.KeyboardCopyFormatPreference = value;
            CopyFormatChanged?.Invoke();
        }
    }

    public Action? CopyFormatChanged { get; set; }

    public bool IsPreReleaseEnabled
    {
        get
        {
            _isPreReleaseEnabled ??= _preferences.PreReleasePreference;

            return _isPreReleaseEnabled ?? false;
        }
        set
        {
            if (_isPreReleaseEnabled == value) { return; }

            _isPreReleaseEnabled = value;
            _preferences.PreReleasePreference = value;
        }
    }

    public LogLevel LogLevel
    {
        get
        {
            _logLevel ??= _preferences.LogLevelPreference;

            return _logLevel ?? LogLevel.Information;
        }
        set
        {
            if (_logLevel == value) { return; }

            _logLevel = value;
            _preferences.LogLevelPreference = value;
            LogLevelChanged?.Invoke();
        }
    }

    public Action? LogLevelChanged { get; set; }

    public Theme Theme
    {
        get
        {
            _theme ??= _preferences.ThemePreference;

            return _theme ?? Theme.System;
        }
        set
        {
            if (_theme == value) { return; }

            _theme = value;
            _preferences.ThemePreference = value;
            ThemeChanged?.Invoke();
        }
    }

    public Action? ThemeChanged { get; set; }

    public EventHandler<TimeZoneInfo>? TimeZoneChanged { get; set; }

    public string TimeZoneId
    {
        get
        {
            _timeZoneId ??= _preferences.TimeZonePreference;

            return _timeZoneId ?? TimeZoneInfo.Local.Id;
        }
        set
        {
            if (_timeZoneId == value) { return; }

            _timeZoneId = value;
            _preferences.TimeZonePreference = value;
            TimeZoneChanged?.Invoke(this, TimeZoneInfo);
        }
    }

    public TimeZoneInfo TimeZoneInfo => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}
