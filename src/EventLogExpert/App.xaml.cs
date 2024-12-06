// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Services;
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

    public App(
        IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<FilterPaneState, bool> filterPaneIsEnabledState,
        IStateSelection<FilterPaneState, bool> filterPaneIsXmlEnabledState,
        IDatabaseService databaseService,
        ISettingsService settings,
        IAlertDialogService dialogService,
        IClipboardService clipboardService,
        IUpdateService updateService,
        ICurrentVersionProvider currentVersionProvider,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        IFileLogger traceLogger)
    {
        InitializeComponent();

        _mainPage = new MainPage(
            fluxorDispatcher,
            databaseCollectionProvider,
            activeLogsState,
            continuouslyUpdateState,
            filterPaneIsEnabledState,
            filterPaneIsXmlEnabledState,
            databaseService,
            settings,
            dialogService,
            clipboardService,
            updateService,
            currentVersionProvider,
            appTitleService,
            fileLocationOptions,
            traceLogger);
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

        return window;
    }
}
