// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.Maui.Platform;
using System.Collections.Immutable;
using System.Diagnostics.Eventing.Reader;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using static EventLogExpert.UI.Store.EventLog.EventLogState;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class MainPage : ContentPage
{
    private readonly IDispatcher _fluxorDispatcher;
    private readonly IState<SettingsState> _settingsState;
    private readonly IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> _activeLogsState;
    private readonly IUpdateService _updateService;
    private readonly IAppTitleService _appTitleService;
    private readonly ITraceLogger _traceLogger;

    public MainPage(IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<SettingsState, bool> showLogNameState,
        IStateSelection<SettingsState, bool> showComputerNameState,
        IStateSelection<SettingsState, IEnumerable<string>> loadedProvidersState,
        IState<SettingsState> settingsState,
        IUpdateService updateService,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        ITraceLogger traceLogger)
    {
        InitializeComponent();

        _fluxorDispatcher = fluxorDispatcher;
        _settingsState = settingsState;
        _updateService = updateService;
        _appTitleService = appTitleService;
        _traceLogger = traceLogger;

        _activeLogsState = activeLogsState;

        activeLogsState.Select(e => e.ActiveLogs);

        activeLogsState.SelectedValueChanged += (sender, activeLogs) =>
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (activeLogs == ImmutableDictionary<string, EventLogData>.Empty)
                {
                    _appTitleService.SetLogName(null);
                }
                else
                {
                    _appTitleService.SetLogName(string.Join(" | ", activeLogs.Values.Select(l => l.Name)));
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
            databaseCollectionProvider.SetActiveDatabases(loadedProviders.Select(path => Path.Join(fileLocationOptions.DatabasePath, path)));

        fluxorDispatcher.Dispatch(new SettingsAction.LoadSettings());
        fluxorDispatcher.Dispatch(new SettingsAction.LoadDatabases());
        fluxorDispatcher.Dispatch(new FilterCacheAction.LoadFilters());

        PopulateOtherLogsMenu();

        var args = Environment.GetCommandLineArgs();

        if (args.Length > 1)
        {
            OpenEventLogFile(args[1]);
        }

        EnableAddLogToViewViaDragAndDrop();
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
            foreach (var file in result.Where(f => f is not null))
            {
                OpenEventLogFile(file!.FullPath);
            }
        }
    }

    public async void AddFile_Clicked(object sender, EventArgs e)
    {
        var result = await GetFilePickerResult();
        if (result is not null)
        {
            foreach (var file in result.Where(f => f is not null))
            {
                if (_activeLogsState.Value.Any(l => l.Key == file?.FullPath))
                {
                    return;
                }

                OpenEventLogFile(file!.FullPath);
            }
        }
    }

    private async Task<IEnumerable<FileResult?>> GetFilePickerResult()
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>> { { DevicePlatform.WinUI, new[] { ".evtx" } } }
            )
        };

        return await FilePicker.Default.PickMultipleAsync(options);
    }

    private async void CheckForUpdates_Clicked(object? sender, EventArgs e) =>
        await _updateService.CheckForUpdates(_settingsState.Value.Config.IsPrereleaseEnabled, manualScan: true);

    private void ContinuouslyUpdate_Clicked(object sender, EventArgs e)
    {
        if (((MenuFlyoutItem)sender).Text == "Continuously Update")
        {
            _fluxorDispatcher.Dispatch(new EventLogAction.SetContinouslyUpdate(true, _traceLogger));
        }
        else
        {
            _fluxorDispatcher.Dispatch(new EventLogAction.SetContinouslyUpdate(false, _traceLogger));
        }
    }

    private void LoadNewEvents_Clicked(object sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(new EventLogAction.LoadNewEvents(_traceLogger));
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

        if (_activeLogsState.Value.Any(l => l.Key == logName))
        {
            return;
        }

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

    private void EnableAddLogToViewViaDragAndDrop()
    {
        Loaded += (s, e) =>
        {
            if (Handler?.MauiContext == null) return;

            var platformElement = WebView.ToPlatform(Handler.MauiContext);
            platformElement.AllowDrop = true;
            platformElement.Drop += async (sender, eventArgs) =>
            {
                if (eventArgs.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await eventArgs.DataView.GetStorageItemsAsync();
                    foreach (var item in items)
                    {
                        if (item is StorageFile file)
                        {
                            if (_activeLogsState.Value.Any(l => l.Key == file.Path))
                            {
                                return;
                            }

                            OpenEventLogFile($"{file.Path}");
                        }
                    }
                }
            };

            platformElement.DragOver += async (sender, eventArgs) =>
            {
                if (eventArgs.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    var deferral = eventArgs.GetDeferral();
                    var extensions = new List<string> { ".evtx" };
                    var isAllowed = false;
                    var items = await eventArgs.DataView.GetStorageItemsAsync();
                    foreach (var item in items)
                    {
                        if (item is StorageFile file && extensions.Contains(file.FileType))
                        {
                            isAllowed = true;
                            break;
                        }
                    }

                    eventArgs.AcceptedOperation = isAllowed ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
                    deferral.Complete();
                }

                eventArgs.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            };
        };
    }

    private void ShowLogName_Clicked(object? sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(new SettingsAction.ToggleShowLogName());
    }

    private void ShowComputerName_Clicked(object? sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(new SettingsAction.ToggleShowComputerName());
    }

    private void ViewRecentFilters_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new FilterCacheAction.OpenMenu());
}
