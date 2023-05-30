// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Store.EventLog;
using EventLogExpert.Store.Settings;
using Fluxor;
using static EventLogExpert.Store.EventLog.EventLogState;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class App : Application
{
    public App(IDispatcher fluxorDispatcher,
        IEventResolver resolver,
        IStateSelection<EventLogState, IEnumerable<LogSpecifier>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<SettingsState, bool> showLogState,
        IStateSelection<SettingsState, bool> showComputerState,
        IStateSelection<SettingsState, IEnumerable<string>> loadedProvidersState)
    {
        InitializeComponent();

        MainPage = new NavigationPage(
            new MainPage(fluxorDispatcher,
                resolver,
                activeLogsState,
                continuouslyUpdateState,
                showLogState,
                showComputerState,
                loadedProvidersState));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        window.Title = "EventLogExpert";

        return window;
    }
}
