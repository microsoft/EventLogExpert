// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public sealed partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly ISettingsService _settings;

    public App(
        IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogs,
        IStateSelection<EventLogState, bool> continuouslyUpdate,
        IStateSelection<FilterPaneState, bool> filterPaneIsEnabled,
        IDatabaseService databaseService,
        ISettingsService settings,
        IAlertDialogService dialogService,
        IClipboardService clipboardService,
        IUpdateService updateService,
        ICurrentVersionProvider currentVersionProvider,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        ITraceLogger traceLogger,
        IFileLogger fileLogger,
        IModalService modalService)
    {
        InitializeComponent();

        _settings = settings;

        // Apply native (XAML) theme before constructing MainPage so the initial
        // MenuFlyout / native control tree is created under the correct theme.
        ApplyNativeTheme(_settings.Theme);
        _settings.ThemeChanged += OnThemeChanged;

        _mainPage = new MainPage(
            fluxorDispatcher,
            databaseCollectionProvider,
            activeLogs,
            continuouslyUpdate,
            filterPaneIsEnabled,
            databaseService,
            settings,
            dialogService,
            clipboardService,
            updateService,
            currentVersionProvider,
            appTitleService,
            fileLocationOptions,
            traceLogger,
            fileLogger,
            modalService);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window
        {
            Title = "EventLogExpert",
            Page = new NavigationPage(_mainPage)
        };

        // Ultrawide monitors create a window that is way too wide
        if (DeviceDisplay.Current.MainDisplayInfo.Width >= 2048)
        {
            window.Width = 2000;
        }

        // The WinUI MenuBar (rendered for ContentPage.MenuBarItems) is a
        // native control whose theme is driven by the WinUI window root's
        // FrameworkElement.RequestedTheme - MAUI's UserAppTheme alone does
        // not propagate to it. Apply once the platform handler is attached.
        window.HandlerChanged += (_, _) => ApplyPlatformWindowTheme();

        return window;
    }

    private void ApplyNativeTheme(Theme theme)
    {
        UserAppTheme = theme switch
        {
            Theme.Light => AppTheme.Light,
            Theme.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };

        ApplyPlatformWindowTheme();
    }

    private void OnThemeChanged() =>
        // ThemeChanged may be raised from non-UI threads (Blazor JSInterop /
        // Fluxor effects). UserAppTheme must be set on the MAUI UI thread.
        MainThread.BeginInvokeOnMainThread(() => ApplyNativeTheme(_settings.Theme));

    private void ApplyPlatformWindowTheme()
    {
        // The WinUI window root hosts the native title bar AND the MAUI
        // AppTitleBar (which contains the MenuBar). Both follow the root's
        // RequestedTheme. Force Dark so the entire top chrome stays dark
        // regardless of the user's selected app theme. The Blazor page is in
        // a WebView and doesn't inherit RequestedTheme - it follows the user
        // theme independently via CSS data-theme.
        foreach (var window in Windows)
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window winUiWindow)
            {
                continue;
            }

            if (winUiWindow.Content is Microsoft.UI.Xaml.FrameworkElement root)
            {
                root.RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark;
            }

            ForceDarkTitleBar(winUiWindow);
        }
    }

    private static void ForceDarkTitleBar(Microsoft.UI.Xaml.Window winUiWindow)
    {
        try
        {
            var titleBar = winUiWindow.AppWindow?.TitleBar;

            if (titleBar is null) { return; }

            if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) { return; }

            var background = global::Windows.UI.Color.FromArgb(0xFF, 0x22, 0x22, 0x22);
            var foreground = global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            var inactiveBackground = global::Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x2D, 0x2D);
            var inactiveForeground = global::Windows.UI.Color.FromArgb(0xFF, 0x99, 0x99, 0x99);
            var hoverBackground = global::Windows.UI.Color.FromArgb(0xFF, 0x35, 0x35, 0x35);
            var pressedBackground = global::Windows.UI.Color.FromArgb(0xFF, 0x44, 0x44, 0x44);

            titleBar.BackgroundColor = background;
            titleBar.ForegroundColor = foreground;
            titleBar.InactiveBackgroundColor = inactiveBackground;
            titleBar.InactiveForegroundColor = inactiveForeground;

            titleBar.ButtonBackgroundColor = background;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = hoverBackground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = pressedBackground;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonInactiveBackgroundColor = inactiveBackground;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Window has been closed/disposed between the event firing and
            // here. Safe to ignore.
        }
    }
}
