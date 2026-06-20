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

    private void ApplyNativeTheme(Theme theme) =>
        UserAppTheme = theme switch
        {
            Theme.Light => AppTheme.Light,
            Theme.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };

    private void OnThemeChanged() =>
        // ThemeChanged may be raised from non-UI threads (Blazor JSInterop /
        // Fluxor effects). UserAppTheme must be set on the MAUI UI thread.
        MainThread.BeginInvokeOnMainThread(() => ApplyNativeTheme(_settings.Theme));
}
