﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Store.EventLog;
using EventLogExpert.Store.Settings;
using EventLogExpert.Store.StatusBar;
using Fluxor;
using System.Collections.Immutable;
using System.Diagnostics.Eventing.Reader;
using static EventLogExpert.Store.EventLog.EventLogState;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class MainPage : ContentPage
{
    private readonly IDispatcher _fluxorDispatcher;

    private readonly IEventResolver _resolver;

    public MainPage(IDispatcher fluxorDispatcher, IEventResolver resolver,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<SettingsState, bool> showLogNameState,
        IStateSelection<SettingsState, bool> showComputerNameState)
    {
        InitializeComponent();

        _fluxorDispatcher = fluxorDispatcher;

        _resolver = resolver;

        activeLogsState.Select(e => e.ActiveLogs);
        activeLogsState.SelectedValueChanged += (sender, activeLogs) => Utils.UpdateAppTitle(string.Join(" ", activeLogs.Values.Select(l => l.Name)));

        continuouslyUpdateState.Select(e => e.ContinuouslyUpdate);
        continuouslyUpdateState.SelectedValueChanged += (sender, continuouslyUpdate) => ContinuouslyUpdateMenuItem.Text = $"Continuously Update{(continuouslyUpdate ? " ✓" : "")}";

        showLogNameState.Select(e => e.ShowLogName);
        showLogNameState.SelectedValueChanged += (sender, showLogName) => ShowLogNameMenuItem.Text = $"Show Log Name{(showLogName ? " ✓" : "")}";

        showComputerNameState.Select(e => e.ShowComputerName);
        showComputerNameState.SelectedValueChanged += (sender, showComputerName) => ShowComputerNameMenuItem.Text = $"Show Computer Name{(showComputerName ? " ✓" : "")}";

        PopulateOtherLogsMenu();

        ListenForResolverStatus();

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            OpenEventLogFile(args[1]);
        }
    }

    private void OpenEventLogFile(string fileName)
    {
        _fluxorDispatcher.Dispatch(
                new EventLogAction.OpenLog(
                    fileName,
                    LogType.File));
    }

    public async void OpenFile_Clicked(object sender, EventArgs e)
    {
        var result = await GetFilePickerResult();

        if (result != null)
        {
            _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());
            OpenEventLogFile(result.FullPath);
        }
    }

    public async void AddFile_Clicked(object sender, EventArgs e)
    {
        var result = await GetFilePickerResult();

        if (result != null)
        {
            OpenEventLogFile(result.FullPath);
        }
    }

    private async Task<FileResult?> GetFilePickerResult()
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>> { { DevicePlatform.WinUI, new[] { ".evtx" } } }
            )
        };

        return await FilePicker.Default.PickAsync(options);
    }

    private void CheckForUpdates_Clicked(object? sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new SettingsAction.CheckForUpdates());

    private void ContinuouslyUpdate_Clicked(object sender, EventArgs e)
    {
        if (((MenuFlyoutItem)sender).Text == "Continuously Update")
        {
            _fluxorDispatcher.Dispatch(new EventLogAction.SetContinouslyUpdate(true));
        }
        else
        {
            _fluxorDispatcher.Dispatch(new EventLogAction.SetContinouslyUpdate(false));
        }
    }

    private void LoadNewEvents_Clicked(object sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(new EventLogAction.LoadNewEvents());
    }

    private void OpenLiveLog_Clicked(object? sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());

        AddLiveLog_Clicked(sender, e);
    }

    private void AddLiveLog_Clicked(object? sender, EventArgs e)
    {
        if (sender == null) return;

        var logName = ((MenuFlyoutItem)sender).Text;

        _fluxorDispatcher.Dispatch(
            new EventLogAction.OpenLog(
                logName,
                LogType.Live));
    }

    private void CloseAll_Clicked(object? sender, EventArgs e)
    {
        if (sender == null) return;

        _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());
    }

    private void OpenSettingsModal_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new SettingsAction.OpenMenu());

    private async void PopulateOtherLogsMenu()
    {
        var logsThatAlreadyHaveMenuItems = new[] { "Application", "System" };
        var session = new EventLogSession();
        var names = session.GetLogNames()
            .Where(n => !logsThatAlreadyHaveMenuItems.Contains(n))
            .OrderBy(n => n);

        foreach (var name in names)
        {
            // Do this in the background to improve startup time.
            var hasLogInformation = await Task.Run(() =>
            {
                try
                {
                    return session.GetLogInformation(name, PathType.LogName).CreationTime.HasValue;
                }
                catch
                {
                    return false;
                }
            });

            if (hasLogInformation)
            {
                var m = new MenuFlyoutItem { Text = name };
                m.Clicked += OpenLiveLog_Clicked;
                OpenOtherLogsFlyoutSubitem.Add(m);

                var addm = new MenuFlyoutItem { Text = name };
                addm.Clicked += AddLiveLog_Clicked;
                AddOtherLogsFlyoutSubitem.Add(addm);
            }
        }
    }

    private void ListenForResolverStatus()
    {
        _resolver.StatusChanged += (sender, status) =>
        {
            _fluxorDispatcher.Dispatch(new StatusBarAction.SetResolverStatus(status));
        };

        if (!string.IsNullOrEmpty(_resolver.Status))
        {
            _fluxorDispatcher.Dispatch(new StatusBarAction.SetResolverStatus(_resolver.Status));
        }
    }

    private void ShowLogName_Clicked(object? sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(new SettingsAction.ToggleShowLogName());
    }

    private void ShowComputerName_Clicked(object? sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(new SettingsAction.ToggleShowComputerName());
    }
}
