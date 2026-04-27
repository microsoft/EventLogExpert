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
using System.Collections.Immutable;
using Application = Microsoft.Maui.Controls.Application;
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
