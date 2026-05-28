// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Settings;

public sealed partial class SettingsModal : ModalBase<bool>
{
    private EventCopyFormat _copyFormat;
    private bool _isPreReleaseEnabled;
    private LogLevel _logLevel;
    private bool _showDisplayPaneOnSelectionChange;
    private Theme _theme;
    private string _timeZoneId = string.Empty;

    [Inject] private IAnnouncementService AnnouncementService { get; init; } = null!;

    [Inject] private IDetailsPanePreferencesProvider DetailsPanePreferences { get; init; } = null!;

    [Inject] private ISettingsService Settings { get; init; } = null!;

    /// <summary>
    ///     Test-only seam. <c>SettingsModalTests</c> invoke this instead of routing through the ModalChrome footer
    ///     markup, which would couple tests to chrome button class names.
    /// </summary>
    internal Task InvokeOnSaveAsyncForTests() => OnSaveAsync();

    protected override void OnInitialized()
    {
        LoadFromSettings();
        base.OnInitialized();
    }

    protected override async Task OnSaveAsync()
    {
        SaveSettings();

        AnnouncementService.Announce("Settings saved");

        await CompleteAsync(true);
    }

    private void LoadFromSettings()
    {
        _copyFormat = Settings.CopyFormat;
        _isPreReleaseEnabled = Settings.IsPreReleaseEnabled;
        _logLevel = Settings.LogLevel;
        _showDisplayPaneOnSelectionChange = DetailsPanePreferences.DisplayPaneSelectionPreference;
        _theme = Settings.Theme;
        _timeZoneId = Settings.TimeZoneId;
    }

    private void SaveSettings()
    {
        Settings.CopyFormat = _copyFormat;
        Settings.IsPreReleaseEnabled = _isPreReleaseEnabled;
        Settings.LogLevel = _logLevel;
        DetailsPanePreferences.DisplayPaneSelectionPreference = _showDisplayPaneOnSelectionChange;
        Settings.Theme = _theme;
        Settings.TimeZoneId = _timeZoneId;
    }
}
