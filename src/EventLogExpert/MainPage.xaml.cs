// // Copyright (c) Microsoft Corporation.
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

    public MainPage(IDispatcher fluxorDispatcher,
        IEventResolver resolver,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<SettingsState, bool> showLogNameState,
        IStateSelection<SettingsState, bool> showComputerNameState,
        IStateSelection<SettingsState, IEnumerable<string>> loadedProvidersState)
    {
        InitializeComponent();

        _fluxorDispatcher = fluxorDispatcher;
        _resolver = resolver;

        activeLogsState.Select(e => e.ActiveLogs);

        activeLogsState.SelectedValueChanged += (sender, activeLogs) =>
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (activeLogs == ImmutableDictionary<string, EventLogData>.Empty)
                {
                    Utils.UpdateAppTitle();
                }
                else
                {
                    Utils.UpdateAppTitle(string.Join(" | ", activeLogs.Values.Select(l => l.Name)));
                }
            });

        continuouslyUpdateState.Select(e => e.ContinuouslyUpdate);

        continuouslyUpdateState.SelectedValueChanged += (sender, continuouslyUpdate) =>
            MainThread.InvokeOnMainThreadAsync(() =>
                ContinuouslyUpdateMenuItem.Text = $"Continuously Update{(continuouslyUpdate ? " ✓" : "")}");

        showLogNameState.Select(e => e.ShowLogName);

        showLogNameState.SelectedValueChanged += (sender, showLogName) =>
            MainThread.InvokeOnMainThreadAsync(() =>
                ShowLogNameMenuItem.Text = $"Show Log Name{(showLogName ? " ✓" : "")}");

        showComputerNameState.Select(e => e.ShowComputerName);

        showComputerNameState.SelectedValueChanged += (sender, showComputerName) =>
            MainThread.InvokeOnMainThreadAsync(() =>
                ShowComputerNameMenuItem.Text = $"Show Computer Name{(showComputerName ? " ✓" : "")}");

        loadedProvidersState.Select(s => s.LoadedDatabases);

        loadedProvidersState.SelectedValueChanged += (sender, loadedProviders) =>
        {
            if (_resolver is IDatabaseEventResolver dbResolver)
            {
                dbResolver.SetActiveDatabases(loadedProviders.Select(path => Path.Join(Utils.DatabasePath, path)));
            }
        };

        fluxorDispatcher.Dispatch(new SettingsAction.LoadSettings());
        fluxorDispatcher.Dispatch(new SettingsAction.LoadDatabases());

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
                var openItem = new MenuFlyoutItem { Text = name };
                openItem.Clicked += OpenLiveLog_Clicked;

                var addItem = new MenuFlyoutItem { Text = name };
                addItem.Clicked += AddLiveLog_Clicked;

                if (name == "Security")
                {
                    // If we are being run as admin, we can access the Security log.
                    // Make it a peer of Application and System instead of putting it
                    // under Other Logs.
                    OpenLiveLogFlyoutSubitem.Insert(1, openItem);
                    AddLiveLogFlyoutSubitem.Insert(1, addItem);
                }
                else
                {
                    OpenOtherLogsFlyoutSubitem.Add(openItem);
                    AddOtherLogsFlyoutSubitem.Add(addItem);
                }
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
