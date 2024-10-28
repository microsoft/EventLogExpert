// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Reader;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Store.FilterPane;
using EventLogExpert.UI.Store.Settings;
using Fluxor;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platform;
using System.Collections.Immutable;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using DataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public sealed partial class MainPage : ContentPage, IDisposable
{
    private static readonly KeyboardAccelerator s_copyShortcut = new() { Modifiers = KeyboardAcceleratorModifiers.Ctrl, Key = "C" };

    private readonly IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> _activeLogsState;
    private readonly IClipboardService _clipboardService;
    private readonly ICurrentVersionProvider _currentVersionProvider;
    private readonly IAlertDialogService _dialogService;
    private readonly IDispatcher _fluxorDispatcher;
    private readonly IState<SettingsState> _settingsState;
    private readonly ITraceLogger _traceLogger;
    private readonly IUpdateService _updateService;

    private CancellationTokenSource _cancellationTokenSource = new();

    public MainPage(
        IActionSubscriber actionSubscriber,
        IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<FilterPaneState, bool> filterPaneIsEnabledState,
        IStateSelection<SettingsState, IEnumerable<string>> loadedProvidersState,
        IState<SettingsState> settingsState,
        IAlertDialogService dialogService,
        IClipboardService clipboardService,
        IUpdateService updateService,
        ICurrentVersionProvider currentVersionProvider,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        ITraceLogger traceLogger)
    {
        InitializeComponent();
        PopulateOtherLogsMenu();

        _activeLogsState = activeLogsState;
        _clipboardService = clipboardService;
        _currentVersionProvider = currentVersionProvider;
        _fluxorDispatcher = fluxorDispatcher;
        _settingsState = settingsState;
        _dialogService = dialogService;
        _traceLogger = traceLogger;
        _updateService = updateService;

        activeLogsState.Select(e => e.ActiveLogs);

        activeLogsState.SelectedValueChanged += (sender, activeLogs) =>
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                appTitleService.SetLogName(
                    activeLogs == ImmutableDictionary<string, EventLogData>.Empty ?
                        null : string.Join(" | ", activeLogs.Values.Select(l => l.Name)));
            });

        continuouslyUpdateState.Select(e => e.ContinuouslyUpdate);

        continuouslyUpdateState.SelectedValueChanged += (sender, continuouslyUpdate) =>
            MainThread.InvokeOnMainThreadAsync(() =>
                ContinuouslyUpdateMenuItem.Text = $"Continuously Update{(continuouslyUpdate ? " ✓" : "")}");

        filterPaneIsEnabledState.Select(e => e.IsEnabled);

        filterPaneIsEnabledState.SelectedValueChanged += (sender, isEnabled) =>
            MainThread.InvokeOnMainThreadAsync(() =>
                ShowAllEventsMenuItem.Text = $"Show All Events{(isEnabled ? "" : " ✓")}");

        loadedProvidersState.Select(s => s.LoadedDatabases);

        loadedProvidersState.SelectedValueChanged += (sender, loadedProviders) =>
            databaseCollectionProvider.SetActiveDatabases(loadedProviders.Select(path =>
                Path.Join(fileLocationOptions.DatabasePath, path)));

        actionSubscriber.SubscribeToAction<SettingsAction.LoadSettingsCompleted>(this,
            action => { SetCopyKeyboardAccelerator(); });
        actionSubscriber.SubscribeToAction<SettingsAction.SaveCompleted>(this,
            action => { SetCopyKeyboardAccelerator(); });

        fluxorDispatcher.Dispatch(new EventTableAction.LoadColumns());
        fluxorDispatcher.Dispatch(new SettingsAction.LoadSettings());
        fluxorDispatcher.Dispatch(new SettingsAction.LoadDatabases());
        fluxorDispatcher.Dispatch(new FilterCacheAction.LoadFilters());
        fluxorDispatcher.Dispatch(new FilterGroupAction.LoadGroups());

        var args = Environment.GetCommandLineArgs();

        if (args.Length > 1)
        {
            OpenEventLogFile(args[1]);
        }

        EnableAddLogToViewViaDragAndDrop();
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

    private static async Task<IEnumerable<FileResult?>> GetFilePickerResult()
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, [".evtx"] }
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

    private async Task AddLiveLog(string logName)
    {
        if (string.IsNullOrWhiteSpace(logName)) { return; }

        if (_activeLogsState.Value.Any(l => l.Key == logName)) { return; }

        using EventLogSession session = new();

        EventLogInformation? eventLogInformation;

        try
        {
            eventLogInformation = session.GetLogInformation(logName, PathType.LogName);
        }
        catch (UnauthorizedAccessException)
        {
            await _dialogService.ShowAlert("Log requires elevation",
                "Please relaunch with \"Run as Administrator\" to open this log",
                "Ok");

            return;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowAlert("Failed to open Log", $"Exception: {ex.Message}", "Ok");

            return;
        }

        if (eventLogInformation.RecordCount is null or <= 0)
        {
            await _dialogService.ShowAlert("Empty log", "Log contains no events", "Ok");

            return;
        }

        if (_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        _fluxorDispatcher.Dispatch(new EventLogAction.OpenLog(logName, LogType.Live, _cancellationTokenSource.Token));
    }

    private async void AddLiveLog_Clicked(object? sender, EventArgs e)
    {
        if (sender is null) { return; }

        await AddLiveLog(((MenuFlyoutItem)sender).Text);
    }

    private async void CheckForUpdates_Clicked(object? sender, EventArgs e)
    {
        if (!_currentVersionProvider.IsSupportedOS(DeviceInfo.Version))
        {
            _traceLogger.Trace("Update API does not work on versions older than 10.0.19041.0", LogLevel.Warning);
            return;
        }

        await _updateService.CheckForUpdates(_settingsState.Value.Config.IsPreReleaseEnabled, manualScan: true);
    }

    private void ClearAllFilters_Clicked(object? sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new FilterPaneAction.ClearAllFilters());

    private void CloseAll_Clicked(object? sender, EventArgs e)
    {
        if (sender is null) { return; }

        _cancellationTokenSource.Cancel();

        _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());
    }

    private void ContinuouslyUpdate_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(((MenuFlyoutItem)sender).Text == "Continuously Update" ?
            new EventLogAction.SetContinuouslyUpdate(true) :
            new EventLogAction.SetContinuouslyUpdate(false));

    private void CopySelected_Clicked(object sender, EventArgs e)
    {
        var item = sender as MenuFlyoutItem;
        var param = item?.CommandParameter as CopyType?;

        _clipboardService.CopySelectedEvent(param);
    }

    private void CreateFlyoutMenu(MenuFlyoutSubItem rootMenu, IEnumerable<string> logNames, bool shouldAddLog = false)
    {
        foreach (var logName in logNames)
        {
            if (_cancellationTokenSource.IsCancellationRequested) { return; }

            var folders = logName.Split(['-', '/']);
            MenuFlyoutSubItem menu = rootMenu;

            if (folders.Length > 1)
            {
                for (int i = 0; i < folders.Length - 1; i++)
                {
                    if (menu.FirstOrDefault(x => x.Text.Equals(folders[i])) is MenuFlyoutSubItem newMenu)
                    {
                        menu = newMenu;

                        continue;
                    }

                    newMenu = new MenuFlyoutSubItem { Text = string.Intern(folders[i]) };
                    menu.Add(newMenu);
                    menu = newMenu;
                }
            }

            var log = new MenuFlyoutItem { Text = string.Intern(folders[^1]) };

            if (shouldAddLog)
            {
                log.Clicked += async (s, e) => { await AddLiveLog(logName); };
            }
            else
            {
                log.Clicked += async (s, e) => { await OpenLiveLog(logName); };
            }

            menu.Add(log);
        }
    }

    private async void Docs_Clicked(object sender, EventArgs e)
    {
        try
        {
            Uri uri = new("https://github.com/microsoft/EventLogExpert/blob/main/docs/Home.md");
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowAlert("Failed to launch browser", ex.Message, "Ok");
        }
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
        Application.Current?.CloseWindow(Application.Current.MainPage!.Window!);

    private void LoadNewEvents_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new EventLogAction.LoadNewEvents());

    private void OpenEventLogFile(string fileName)
    {
        if (_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        _fluxorDispatcher.Dispatch(new EventLogAction.OpenLog(fileName, LogType.File, _cancellationTokenSource.Token));
    }

    // TODO: Extract this so it can be called from the MainPage keyup handler
    private async void OpenFile_Clicked(object sender, EventArgs e)
    {
        var result = await GetFilePickerResult();

        var logs = result.Where(f => f is not null).ToList();

        if (logs.Count <= 0) { return; }

        _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());

        foreach (var file in logs)
        {
            OpenEventLogFile(file!.FullPath);
        }
    }

    private async Task OpenLiveLog(string logName)
    {
        _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());

        await AddLiveLog(logName);
    }

    private void OpenLiveLog_Clicked(object? sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());

        AddLiveLog_Clicked(sender, e);
    }

    private void OpenSettingsModal_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new SettingsAction.OpenMenu());

    private void PopulateOtherLogsMenu()
    {
        using EventLogSession session = new();

        var names = session.GetLogNames();

        CreateFlyoutMenu(AddOtherLogsFlyoutSubitem, names, true);
        CreateFlyoutMenu(OpenOtherLogsFlyoutSubitem, names);
    }

    private async void ReleaseNotes_Clicked(object sender, EventArgs e) => await _updateService.GetReleaseNotes();

    private async void SaveAllFilters_Clicked(object sender, EventArgs e)
    {
        var groupName = await _dialogService.DisplayPrompt(
            "Group Name",
            "What would you like to name this group?",
            "New Filter Section\\New Filter Group");

        if (string.IsNullOrEmpty(groupName)) { return; }

        _fluxorDispatcher.Dispatch(new FilterPaneAction.SaveFilterGroup(groupName));
    }

    private void SetCopyKeyboardAccelerator()
    {
        Application.Current!.Dispatcher.Dispatch(() =>
        {
            CopySelected.KeyboardAccelerators.Clear();
            CopySelectedSimple.KeyboardAccelerators.Clear();
            CopySelectedXml.KeyboardAccelerators.Clear();
            CopySelectedFull.KeyboardAccelerators.Clear();

            switch (_settingsState.Value.Config.CopyType)
            {
                case CopyType.Default:
                    CopySelected.KeyboardAccelerators.Add(s_copyShortcut);
                    break;
                case CopyType.Simple:
                    CopySelectedSimple.KeyboardAccelerators.Add(s_copyShortcut);
                    break;
                case CopyType.Xml:
                    CopySelectedXml.KeyboardAccelerators.Add(s_copyShortcut);
                    break;
                case CopyType.Full:
                    CopySelectedFull.KeyboardAccelerators.Add(s_copyShortcut);
                    break;
                default: throw new ArgumentOutOfRangeException();
            }
        });
    }

    private void ShowAllEvents_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new FilterPaneAction.ToggleIsEnabled());

    private async void SubmitAnIssue_Clicked(object sender, EventArgs e)
    {
        try
        {
            Uri uri = new("https://github.com/microsoft/EventLogExpert/issues/new");
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowAlert("Failed to launch browser", ex.Message, "Ok");
        }
    }

    private void ViewFilterGroups_Clicked(object? sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new FilterGroupAction.OpenMenu());

    private void ViewRecentFilters_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new FilterCacheAction.OpenMenu());
}
