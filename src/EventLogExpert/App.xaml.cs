// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Store.EventLog;
using Fluxor;
using static EventLogExpert.Store.EventLog.EventLogState;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class App : Application
{
    public App(IDispatcher fluxorDispatcher, IEventResolver resolver, IStateSelection<EventLogState, IEnumerable<LogSpecifier>> activeLogsState)
    {
        InitializeComponent();

        MainPage = new NavigationPage(new MainPage(fluxorDispatcher, resolver, activeLogsState));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        window.Title = "EventLogExpert";

        return window;
    }
}
