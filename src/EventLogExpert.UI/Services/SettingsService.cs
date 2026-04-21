// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Services;

public sealed class SettingsService(IPreferencesProvider preferences) : ISettingsService
{
    private readonly IPreferencesProvider _preferences = preferences;

    private CopyType? _copyType;
    private bool? _isPreReleaseEnabled;
    private LogLevel? _logLevel;
    private bool? _showDisplayPaneOnSelectionChange;
    private Theme? _theme;
    private string? _timeZoneId;

    public CopyType CopyType
    {
        get
        {
            _copyType ??= _preferences.KeyboardCopyTypePreference;

            return _copyType ?? CopyType.Default;
        }
        set
        {
            if (_copyType == value) { return; }

            _copyType = value;
            _preferences.KeyboardCopyTypePreference = value;
            CopyTypeChanged?.Invoke();
        }
    }

    public Action? CopyTypeChanged { get; set; }

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

    public event Action? Loaded;

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

    public bool ShowDisplayPaneOnSelectionChange
    {
        get
        {
            _showDisplayPaneOnSelectionChange ??= _preferences.DisplayPaneSelectionPreference;

            return _showDisplayPaneOnSelectionChange ?? false;
        }
        set
        {
            if (_showDisplayPaneOnSelectionChange == value) { return; }

            _showDisplayPaneOnSelectionChange = value;
            _preferences.DisplayPaneSelectionPreference = value;
        }
    }

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

    public void Load() => Loaded?.Invoke();
}
