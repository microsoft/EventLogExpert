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
using Fluxor;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Application = Microsoft.Maui.Controls.Application;
using Color = Windows.UI.Color;
using IDispatcher = Fluxor.IDispatcher;
using Window = Microsoft.Maui.Controls.Window;

namespace EventLogExpert;

public sealed partial class App : Application
{
    private readonly MainPage _mainPage;
    private readonly ISettingsService _settings;

    public App(
        IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogs,
        IDatabaseService databaseService,
        ISettingsService settings,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        ITraceLogger traceLogger,
        MauiMenuActionService menuActionService)
    {
        InitializeComponent();

        _settings = settings;

        ApplyNativeTheme(_settings.Theme);
        _settings.ThemeChanged += OnThemeChanged;

        _mainPage = new MainPage(
            fluxorDispatcher,
            databaseCollectionProvider,
            activeLogs,
            databaseService,
            settings,
            appTitleService,
            fileLocationOptions,
            traceLogger,
            menuActionService);
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

        // The custom Blazor menu bar (MenuBar/MenuHost) replaces the native WinUI MenuBar that
        // ContentPage.MenuBarItems used to render, so we no longer need to wire FrameworkElement
        // RequestedTheme through the platform handler. The forced-dark title bar styling below
        // is still applied here once the WinUI window handler is attached.
        window.HandlerChanged += (_, _) => ApplyPlatformWindowTheme();

        return window;
    }

    private static void ForceDarkTitleBar(Microsoft.UI.Xaml.Window winUiWindow)
    {
        try
        {
            var titleBar = winUiWindow.AppWindow?.TitleBar;

            if (titleBar is null) { return; }

            if (!AppWindowTitleBar.IsCustomizationSupported()) { return; }

            var background = Color.FromArgb(0xFF, 0x22, 0x22, 0x22);
            var foreground = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            var inactiveBackground = Color.FromArgb(0xFF, 0x2D, 0x2D, 0x2D);
            var inactiveForeground = Color.FromArgb(0xFF, 0x99, 0x99, 0x99);
            var hoverBackground = Color.FromArgb(0xFF, 0x35, 0x35, 0x35);
            var pressedBackground = Color.FromArgb(0xFF, 0x44, 0x44, 0x44);

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
        catch (COMException)
        {
            // Window has been closed/disposed between the event firing and
            // here. Safe to ignore.
        }
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

            if (winUiWindow.Content is FrameworkElement root)
            {
                root.RequestedTheme = ElementTheme.Dark;
            }

            ForceDarkTitleBar(winUiWindow);
        }
    }

    private void OnThemeChanged() =>
        // ThemeChanged may be raised from non-UI threads (Blazor JSInterop /
        // Fluxor effects). UserAppTheme must be set on the MAUI UI thread.
        MainThread.BeginInvokeOnMainThread(() => ApplyNativeTheme(_settings.Theme));
}
