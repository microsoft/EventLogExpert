// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Store.EventLog;
using EventLogExpert.Store.Settings;
using EventLogExpert.Store.StatusBar;
using Fluxor;
using System.Diagnostics.Eventing.Reader;
using static EventLogExpert.Store.EventLog.EventLogState;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class MainPage : ContentPage
{
    private readonly IDispatcher _fluxorDispatcher;

    private readonly IEventResolver _resolver;

    public MainPage(IDispatcher fluxorDispatcher, IEventResolver resolver, IStateSelection<EventLogState, IEnumerable<LogSpecifier>> activeLogsState)
    {
        InitializeComponent();

        _fluxorDispatcher = fluxorDispatcher;

        _resolver = resolver;

        activeLogsState.Select(e => e.ActiveLogs);
        activeLogsState.SelectedValueChanged += (sender, activeLogs) => Utils.UpdateAppTitle(string.Join(" ", activeLogs.Select(l => l.Name)));

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
                    new EventLogState.LogSpecifier(
                        fileName,
                        EventLogState.LogType.File)));
    }

    public async void OpenFile_Clicked(object sender, EventArgs e)
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>> { { DevicePlatform.WinUI, new[] { ".evtx" } } }
            )
        };

        FileResult? result = await FilePicker.Default.PickAsync(options);

        if (result != null)
        {
            OpenEventLogFile(result.FullPath);
        }
    }
    private void CheckForUpdates_Clicked(object? sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new SettingsAction.CheckForUpdates());

    private void ContinuouslyUpdate_Clicked(object sender, EventArgs e)
    {
        if (((MenuFlyoutItem)sender).Text == "Continuously Update")
        {
            _fluxorDispatcher.Dispatch(new EventLogAction.SetContinouslyUpdate(true));
            ((MenuFlyoutItem)sender).Text = "Continuously Update ✓";
        }
        else
        {
            _fluxorDispatcher.Dispatch(new EventLogAction.SetContinouslyUpdate(false));
            ((MenuFlyoutItem)sender).Text = "Continuously Update";
        }
    }

    private void LoadNewEvents_Clicked(object sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(new EventLogAction.LoadNewEvents());
    }

    private void OpenLiveLog_Clicked(object? sender, EventArgs e)
    {
        if (sender == null) return;

        var logName = ((MenuFlyoutItem)sender).Text;

        _fluxorDispatcher.Dispatch(
            new EventLogAction.OpenLog(
                new EventLogState.LogSpecifier(
                    logName,
                    EventLogState.LogType.Live)));
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
                OtherLogsFlyoutSubitem.Add(m);
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
