// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Settings;

public sealed partial class SettingsModal : ModalBase<bool>
{
    private EventCopyFormat _copyFormat;
    private bool _isPreReleaseEnabled;
    private LogLevel _logLevel;
    private bool _showDisplayPaneOnSelectionChange;
    private Theme _theme;
    private Dictionary<string, string> _timeZoneDisplay = new();
    private string _timeZoneId = string.Empty;
    private IReadOnlyList<TimeZoneInfo> _timeZones = [];
    private bool _verboseResolution;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    [Inject] private IDetailsPanePreferencesProvider DetailsPanePreferences { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    /// <summary>
    ///     Test-only seam. <c>SettingsModalTests</c> invoke this instead of routing through the ModalChrome footer
    ///     markup, which would couple tests to chrome button class names.
    /// </summary>
    internal Task InvokeOnSaveAsyncForTests() => OnSaveAsync();

    protected override void OnInitialized()
    {
        // One GetSystemTimeZones() snapshot drives both the ordered dropdown list and the id->DisplayName lookup;
        // per-instance (not static) because DisplayName is culture-sensitive.
        _timeZones = TimeZoneInfo.GetSystemTimeZones();
        _timeZoneDisplay = new Dictionary<string, string>(_timeZones.Count);

        foreach (var timeZone in _timeZones)
        {
            _timeZoneDisplay[timeZone.Id] = timeZone.DisplayName;
        }

        LoadFromSettings();
        base.OnInitialized();
    }

    protected override async Task OnSaveAsync()
    {
        SaveSettings();

        AnnouncementService.Announce(Localizer["Settings_SavedAnnouncement"]);

        await CompleteAsync(true);
    }

    private string GetCopyFormatDisplay(EventCopyFormat value) => Localizer[$"Settings_CopyFormat_{value}"];

    private string GetLogLevelDisplay(LogLevel value) => Localizer[$"Settings_LogLevel_{value}"];

    private string GetThemeDisplay(Theme value) => Localizer[$"Settings_Theme_{value}"];

    private string GetTimeZoneDisplay(string? id) =>
        string.IsNullOrEmpty(id) ? string.Empty : (_timeZoneDisplay.TryGetValue(id, out var name) ? name : id);

    private void LoadFromSettings()
    {
        _copyFormat = Settings.CopyFormat;
        _isPreReleaseEnabled = Settings.IsPreReleaseEnabled;
        _logLevel = Settings.LogLevel;
        _showDisplayPaneOnSelectionChange = DetailsPanePreferences.DisplayPaneSelectionPreference;
        _theme = Settings.Theme;
        _timeZoneId = Settings.TimeZoneId;
        _verboseResolution = Settings.VerboseResolution;
    }

    private void SaveSettings()
    {
        Settings.CopyFormat = _copyFormat;
        Settings.IsPreReleaseEnabled = _isPreReleaseEnabled;
        Settings.LogLevel = _logLevel;
        DetailsPanePreferences.DisplayPaneSelectionPreference = _showDisplayPaneOnSelectionChange;
        Settings.Theme = _theme;
        Settings.TimeZoneId = _timeZoneId;
        Settings.VerboseResolution = _verboseResolution;
    }
}
