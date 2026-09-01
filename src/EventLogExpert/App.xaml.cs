// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Adapters.Menu;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Activation;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using Fluxor;
using System.Collections.Immutable;
using Application = Microsoft.Maui.Controls.Application;
using Window = Microsoft.Maui.Controls.Window;

namespace EventLogExpert;

public sealed partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly ISettingsService _settings;

    public App(
        IFilterLibraryCommands filterLibraryCommands,
        ILogTableCommands logTableCommands,
        IStateSelection<LogTableState, ImmutableList<LogView>> logTables,
        ISettingsService settings,
        IAppTitleService appTitleService,
        ITraceLogger traceLogger,
        MauiMenuActionService menuActionService,
        IActivationDispatcher activationDispatcher)
    {
        InitializeComponent();

        _settings = settings;

        ApplyNativeTheme(_settings.Theme);
        _settings.ThemeChanged += OnThemeChanged;
        RequestedThemeChanged += OnRequestedThemeChanged;

        _mainPage = new MainPage(
            filterLibraryCommands,
            logTableCommands,
            logTables,
            settings,
            appTitleService,
            traceLogger,
            menuActionService,
            activationDispatcher);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window
        {
            Title = "EventLogExpert",
            Page = _mainPage
        };

        // Ultrawide monitors create a window that is way too wide
        if (DeviceDisplay.Current.MainDisplayInfo.Width >= 2048)
        {
            window.Width = 2000;
        }

        return window;
    }

    // Keep the native page background (briefly visible before the WebView paints, and as the window
    // backdrop) matched to the active theme so it never flashes an off-theme color. Split from
    // ApplyNativeTheme so the OS-theme-change handler can refresh it WITHOUT re-setting UserAppTheme.
    private void ApplyNativeBackground(Theme theme)
    {
        var backgroundHex = theme switch
        {
            Theme.ModernLight => "#E0E0E0",
            Theme.ModernDark => "#222222",
            Theme.Light => "#F0F0F0",
            Theme.Dark => "#222222",
            _ => RequestedTheme == AppTheme.Dark ? "#222222" : "#F0F0F0",
        };

        Resources["PageBackgroundColor"] = Color.FromArgb(backgroundHex);
    }

    private void ApplyNativeTheme(Theme theme)
    {
        UserAppTheme = theme switch
        {
            Theme.Light or Theme.ModernLight => AppTheme.Light,
            Theme.Dark or Theme.ModernDark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };

        ApplyNativeBackground(theme);
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) =>
        // Following the OS (Theme.System): a Windows light/dark switch changes RequestedTheme without
        // raising ISettingsService.ThemeChanged, so refresh only the native backdrop (not UserAppTheme,
        // which would re-enter this event) so it keeps tracking the OS. The Theme re-check runs on the UI
        // thread so an explicit-theme change made while this callback is queued is not overwritten.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_settings.Theme == Theme.System) { ApplyNativeBackground(Theme.System); }
        });

    private void OnThemeChanged() =>
        // ThemeChanged may be raised from non-UI threads (Blazor JSInterop /
        // Fluxor effects). UserAppTheme must be set on the MAUI UI thread.
        MainThread.BeginInvokeOnMainThread(() => ApplyNativeTheme(_settings.Theme));
}
