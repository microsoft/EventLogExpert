﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Services;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using System.Collections.Immutable;
using static EventLogExpert.UI.Store.EventLog.EventLogState;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public sealed partial class App : Application
{
    public App(IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<EventLogState, DisplayEventModel> selectedEventState,
        IStateSelection<SettingsState, IEnumerable<string>> loadedDatabasesState,
        IState<SettingsState> settingsState,
        IClipboardService clipboardService,
        IUpdateService updateService,
        ICurrentVersionProvider currentVersionProvider,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        IUrlOpenService urlOpenService,
        ITraceLogger traceLogger)
    {
        InitializeComponent();

        MainPage = new NavigationPage(
            new MainPage(fluxorDispatcher,
                databaseCollectionProvider,
                activeLogsState,
                continuouslyUpdateState,
                selectedEventState,
                loadedDatabasesState,
                settingsState,
                clipboardService,
                updateService,
                currentVersionProvider,
                appTitleService,
                fileLocationOptions,
                urlOpenService,
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
