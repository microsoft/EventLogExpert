// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.Settings;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using Fluxor;
using System.Collections.Immutable;
using static EventLogExpert.UI.Store.EventLog.EventLogState;
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
        ICurrentVersionProvider currentVersionProvider,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        ITraceLogger traceLogger)
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
                currentVersionProvider,
                appTitleService,
                fileLocationOptions,
                traceLogger));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        window.Title = "EventLogExpert";

        return window;
    }
}
