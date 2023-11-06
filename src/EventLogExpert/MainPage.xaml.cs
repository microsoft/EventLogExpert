// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;
using System.Collections.Immutable;
using System.Diagnostics.Eventing.Reader;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using static EventLogExpert.UI.Store.EventLog.EventLogState;
using DataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class MainPage : ContentPage
{
    private readonly IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> _activeLogsState;
    private readonly IAppTitleService _appTitleService;
    private readonly IClipboardService _clipboardService;
    private readonly ICurrentVersionProvider _currentVersionProvider;
    private readonly IDispatcher _fluxorDispatcher;
    private readonly IState<SettingsState> _settingsState;
    private readonly ITraceLogger _traceLogger;
    private readonly IUpdateService _updateService;

    private DisplayEventModel? _selectedEvent;

    public MainPage(IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<EventLogState, DisplayEventModel> selectedEventState,
        IStateSelection<SettingsState, bool> showLogNameState,
        IStateSelection<SettingsState, bool> showComputerNameState,
        IStateSelection<SettingsState, IEnumerable<string>> loadedProvidersState,
        IState<SettingsState> settingsState,
        IClipboardService clipboardService,
        IUpdateService updateService,
        ICurrentVersionProvider currentVersionProvider,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        ITraceLogger traceLogger)
    {
        InitializeComponent();

        _activeLogsState = activeLogsState;
        _appTitleService = appTitleService;
        _clipboardService = clipboardService;
        _currentVersionProvider = currentVersionProvider;
        _fluxorDispatcher = fluxorDispatcher;
        _settingsState = settingsState;
        _traceLogger = traceLogger;
        _updateService = updateService;

        activeLogsState.Select(e => e.ActiveLogs);

        activeLogsState.SelectedValueChanged += (sender, activeLogs) =>
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                _appTitleService.SetLogName(
                    activeLogs == ImmutableDictionary<string, EventLogData>.Empty ?
                        null : string.Join(" | ", activeLogs.Values.Select(l => l.Name)));
            });

        continuouslyUpdateState.Select(e => e.ContinuouslyUpdate);

        continuouslyUpdateState.SelectedValueChanged += (sender, continuouslyUpdate) =>
            MainThread.InvokeOnMainThreadAsync(() =>
                ContinuouslyUpdateMenuItem.Text = $"Continuously Update{(continuouslyUpdate ? " ✓" : "")}");

        selectedEventState.Select(e => e.SelectedEvent!);

        selectedEventState.SelectedValueChanged += (sender, selectedEvent) => { _selectedEvent = selectedEvent; };

        loadedProvidersState.Select(s => s.LoadedDatabases);

        loadedProvidersState.SelectedValueChanged += (sender, loadedProviders) =>
            databaseCollectionProvider.SetActiveDatabases(loadedProviders.Select(path =>
                Path.Join(fileLocationOptions.DatabasePath, path)));

        fluxorDispatcher.Dispatch(new SettingsAction.LoadColumns());
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

    private static async Task<IEnumerable<FileResult?>> GetFilePickerResult()
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".evtx" } }
                }
            )
        };

        return await FilePicker.Default.PickMultipleAsync(options);
    }

    private async void AddFile_Clicked(object sender, EventArgs e)
    {
        var result = await GetFilePickerResult();

        foreach (var file in result.Where(f => f is not null))
        {
            if (_activeLogsState.Value.Any(l => l.Key == file?.FullPath))
            {
                return;
            }

            OpenEventLogFile(file!.FullPath);
        }
    }

    private void AddLiveLog_Clicked(object? sender, EventArgs e)
    {
        if (sender is null) { return; }

        var logName = ((MenuFlyoutItem)sender).Text;

        if (_activeLogsState.Value.Any(l => l.Key == logName)) { return; }

        _fluxorDispatcher.Dispatch(
            new EventLogAction.OpenLog(
                logName,
                LogType.Live));
    }

    private async void CheckForUpdates_Clicked(object? sender, EventArgs e)
    {
        if (!_currentVersionProvider.IsSupportedOS(DeviceInfo.Version))
        {
            _traceLogger.Trace("Update API does not work on versions older than 10.0.19041.0", LogLevel.Warning);
            return;
        }

        await _updateService.CheckForUpdates(_settingsState.Value.Config.IsPrereleaseEnabled, manualScan: true);
    }

    private void ClearAllFilters_Clicked(object? sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new FilterPaneAction.ClearAllFilters());

    private void CloseAll_Clicked(object? sender, EventArgs e)
    {
        if (sender is null) { return; }

        _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());
    }

    private void ContinuouslyUpdate_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(((MenuFlyoutItem)sender).Text == "Continuously Update" ?
            new EventLogAction.SetContinouslyUpdate(true, _traceLogger) :
            new EventLogAction.SetContinouslyUpdate(false, _traceLogger));

    private void CopySelected_Clicked(object sender, EventArgs e)
    {
        var item = sender as MenuFlyoutItem;
        var param = item?.CommandParameter as CopyType?;

        _clipboardService.CopySelectedEvent(_selectedEvent, param);
    }

    private void EnableAddLogToViewViaDragAndDrop()
    {
        Loaded += (s, e) =>
        {
            if (Handler?.MauiContext == null) { return; }

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

                    eventArgs.AcceptedOperation = isAllowed ?
                        DataPackageOperation.Copy :
                        DataPackageOperation.None;

                    deferral.Complete();
                }

                eventArgs.AcceptedOperation = DataPackageOperation.None;
            };
        };
    }

    private void Exit_Clicked(object sender, EventArgs e) =>
        Application.Current?.CloseWindow(Application.Current.MainPage!.Window);

    private void LoadNewEvents_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new EventLogAction.LoadNewEvents(_traceLogger));

    private void OpenEventLogFile(string fileName) =>
        _fluxorDispatcher.Dispatch(new EventLogAction.OpenLog(fileName, LogType.File));

    private async void OpenFile_Clicked(object sender, EventArgs e)
    {
        var result = await GetFilePickerResult();

        _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());

        foreach (var file in result.Where(f => f is not null))
        {
            OpenEventLogFile(file!.FullPath);
        }
    }

    private void OpenLiveLog_Clicked(object? sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());

        AddLiveLog_Clicked(sender, e);
    }

    private void OpenSettingsModal_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new SettingsAction.OpenMenu());

    private async void PopulateOtherLogsMenu()
    {
        var logsThatAlreadyHaveMenuItems = new[]
        {
            "Application",
            "System"
        };

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

    private void ViewRecentFilters_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new FilterCacheAction.OpenMenu());
}
