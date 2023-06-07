// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Services;
using EventLogExpert.Store.EventLog;
using EventLogExpert.Store.Settings;
using Fluxor;
using System.Collections.Immutable;
using static EventLogExpert.Store.EventLog.EventLogState;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class App : Application
{

    public App(IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<SettingsState, bool> showLogState,
        IStateSelection<SettingsState, bool> showComputerState,
        IStateSelection<SettingsState, IEnumerable<string>> loadedDatabasesState,
        IState<SettingsState> settingsState,
        IUpdateService updateService,
        IAppTitleService appTitleService)
    {
        InitializeComponent();

        MainPage = new NavigationPage(
            new MainPage(fluxorDispatcher,
                databaseCollectionProvider,
                activeLogsState,
                continuouslyUpdateState,
                showLogState,
                showComputerState,
                loadedDatabasesState,
                settingsState,
                updateService,
                appTitleService));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        window.Title = "EventLogExpert";

        return window;
    }
}
