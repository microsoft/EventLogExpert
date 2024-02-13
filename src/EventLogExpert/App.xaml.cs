// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Services;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using System.Collections.Immutable;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public sealed partial class App : Application
{
    public App(
        IActionSubscriber actionSubscriber,
        IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<FilterPaneState, bool> filterPaneIsEnabledState,
        IStateSelection<SettingsState, IEnumerable<string>> loadedDatabasesState,
        IState<SettingsState> settingsState,
        IAlertDialogService dialogService,
        IClipboardService clipboardService,
        IUpdateService updateService,
        ICurrentVersionProvider currentVersionProvider,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        ITraceLogger traceLogger)
    {
        InitializeComponent();

        MainPage = new NavigationPage(
            new MainPage(
                actionSubscriber,
                fluxorDispatcher,
                databaseCollectionProvider,
                activeLogsState,
                continuouslyUpdateState,
                filterPaneIsEnabledState,
                loadedDatabasesState,
                settingsState,
                dialogService,
                clipboardService,
                updateService,
                currentVersionProvider,
                appTitleService,
                fileLocationOptions,
                traceLogger));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        window.Title = "EventLogExpert";

        // Ultrawide monitors create a window that is way too wide
        if (DeviceDisplay.Current.MainDisplayInfo.Width >= 2048)
        {
            window.Width = 2000;
        }

        return window;
    }
}
