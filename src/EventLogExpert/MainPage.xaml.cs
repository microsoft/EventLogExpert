// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using System.Collections.Immutable;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Application = Microsoft.Maui.Controls.Application;
using DataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using DragEventArgs = Microsoft.Maui.Controls.DragEventArgs;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public sealed partial class MainPage : ContentPage, IDisposable
{
    private static readonly KeyboardAccelerator s_copyShortcut = new() { Modifiers = KeyboardAcceleratorModifiers.Ctrl, Key = "C" };

    private readonly IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> _activeLogsState;
    private readonly IClipboardService _clipboardService;
    private readonly ICurrentVersionProvider _currentVersionProvider;
    private readonly IDatabaseService _databaseService;
    private readonly IAlertDialogService _dialogService;
    private readonly IStateSelection<FilterPaneState, bool> _filterPaneIsXmlEnabledState;
    private readonly IDispatcher _fluxorDispatcher;
    private readonly ISettingsService _settings;
    private readonly IFileLogger _traceLogger;
    private readonly IUpdateService _updateService;

    private CancellationTokenSource _cancellationTokenSource = new();

    public MainPage(
        IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<FilterPaneState, bool> filterPaneIsEnabledState,
        IStateSelection<FilterPaneState, bool> filterPaneIsXmlEnabledState,
        IDatabaseService databaseService,
        ISettingsService settings,
        IAlertDialogService dialogService,
        IClipboardService clipboardService,
        IUpdateService updateService,
        ICurrentVersionProvider currentVersionProvider,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        IFileLogger traceLogger)
    {
        InitializeComponent();
        PopulateOtherLogsMenu();

        _activeLogsState = activeLogsState;
        _clipboardService = clipboardService;
        _currentVersionProvider = currentVersionProvider;
        _databaseService = databaseService;
        _filterPaneIsXmlEnabledState = filterPaneIsXmlEnabledState;
        _fluxorDispatcher = fluxorDispatcher;
        _settings = settings;
        _dialogService = dialogService;
        _traceLogger = traceLogger;
        _updateService = updateService;

        if (_currentVersionProvider.IsSupportedOS(DeviceInfo.Version))
        {
            _updateService.CheckForUpdates(_settings.IsPreReleaseEnabled, false).AndForget();
        }

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

        filterPaneIsXmlEnabledState.Select(e => e.IsXmlEnabled);

        filterPaneIsXmlEnabledState.SelectedValueChanged += async (sender, isEnabled) =>
            await MainThread.InvokeOnMainThreadAsync(() =>
                EnableXmlFilteringMenuItem.Text = $"Enable Xml Filtering{(isEnabled ? " ✓" : "")}");

        _databaseService.LoadedDatabasesChanged += (sender, loadedProviders) =>
            databaseCollectionProvider.SetActiveDatabases(loadedProviders.Select(path =>
                Path.Join(fileLocationOptions.DatabasePath, path)));

        _settings.CopyTypeChanged += SetCopyKeyboardAccelerator;

        fluxorDispatcher.Dispatch(new EventTableAction.LoadColumns());
        fluxorDispatcher.Dispatch(new FilterCacheAction.LoadFilters());
        fluxorDispatcher.Dispatch(new FilterGroupAction.LoadGroups());

        var args = Environment.GetCommandLineArgs();

        foreach (var arg in args)
        {
            if (arg.EndsWith(".evtx"))
            {
                OpenLog(arg, PathType.FilePath).AndForget();
            }
        }
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

    private async void CheckForUpdates_Clicked(object? sender, EventArgs e)
    {
        if (!_currentVersionProvider.IsSupportedOS(DeviceInfo.Version))
        {
            _traceLogger.Trace("Update API does not work on versions older than 10.0.19041.0", LogLevel.Warning);
            return;
        }

        await _updateService.CheckForUpdates(_settings.IsPreReleaseEnabled, manualScan: true);
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

            log.Clicked += async (s, e) => { await OpenLog(logName, PathType.LogName, shouldAddLog); };

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

    private async void DropGestureRecognizer_OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.PlatformArgs is null) { return; }

        if (!e.PlatformArgs.DragEventArgs.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.PlatformArgs.DragEventArgs.AcceptedOperation = DataPackageOperation.None;
        }

        DragOperationDeferral deferral = e.PlatformArgs.DragEventArgs.GetDeferral();
        List<string> extensions = [".evtx"];
        bool isAllowed = false;
        IReadOnlyList<IStorageItem> items = await e.PlatformArgs.DragEventArgs.DataView.GetStorageItemsAsync();

        foreach (var item in items)
        {
            if (item is not StorageFile file || !extensions.Contains(file.FileType))
            {
                continue;
            }

            isAllowed = true;

            break;
        }

        e.PlatformArgs.DragEventArgs.AcceptedOperation = isAllowed ?
            DataPackageOperation.Copy :
            DataPackageOperation.None;

        deferral.Complete();
    }

    private async void DropGestureRecognizer_OnDrop(object? sender, DropEventArgs e)
    {
        if (e.PlatformArgs is null || !e.PlatformArgs.DragEventArgs.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        IReadOnlyList<IStorageItem> items = await e.PlatformArgs.DragEventArgs.DataView.GetStorageItemsAsync();

        foreach (var item in items)
        {
            if (item is not StorageFile file || _activeLogsState.Value.Any(l => l.Key == file.Path))
            {
                return;
            }

            await OpenLog($"{file.Path}", PathType.FilePath, shouldAddLog: true);
        }
    }

    private async void EnableXmlFiltering_Clicked(object? sender, EventArgs e)
    {
        if (_filterPaneIsXmlEnabledState.Value || _activeLogsState.Value.IsEmpty)
        {
            _fluxorDispatcher.Dispatch(new FilterPaneAction.ToggleIsXmlEnabled());

            return;
        }

        var shouldReload = await _dialogService.ShowAlert("Reload Open Logs Now?",
            "In order for these changes to take effect, all currently open logs must be reloaded. Would you like to reload all open logs now?",
            "Yes", "No");

        if (shouldReload is false) { return; }

        var logsToReopen = _activeLogsState.Value;

        _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());
        _fluxorDispatcher.Dispatch(new FilterPaneAction.ToggleIsXmlEnabled());

        foreach ((_, EventLogData data) in logsToReopen)
        {
            _fluxorDispatcher.Dispatch(new EventLogAction.OpenLog(data.Name, data.Type));
        }
    }

    private void Exit_Clicked(object sender, EventArgs e) =>
        Application.Current?.CloseWindow(Application.Current.Windows[0].Page!.Window!);

    private void LoadNewEvents_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new EventLogAction.LoadNewEvents());

    // TODO: Extract this so it can be called from the MainPage keyup handler
    private async void OpenFile_Clicked(object sender, EventArgs e)
    {
        if (sender is not MenuFlyoutItem item) { return; }

        bool shouldAddLog = item.CommandParameter is true;

        var result = await GetFilePickerResult();

        var logs = result.Where(f => f is not null).ToList();

        if (logs.Count <= 0) { return; }

        if (!shouldAddLog)
        {
            await _cancellationTokenSource.CancelAsync();
            _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());
        }

        foreach (var file in logs)
        {
            await OpenLog(file!.FullPath, PathType.FilePath, shouldAddLog: true);
        }
    }

    private async void OpenLiveLog_Clicked(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyoutItem item) { return; }

        bool shouldAddLog = item.CommandParameter is true;

        await OpenLog(item.Text, PathType.LogName, shouldAddLog);
    }

    private async Task OpenLog(string logPath, PathType pathType, bool shouldAddLog = false)
    {
        if (string.IsNullOrWhiteSpace(logPath)) { return; }

        if (_activeLogsState.Value.Any(l => l.Key == logPath)) { return; }

        EventLogInformation? eventLogInformation;

        try
        {
            eventLogInformation = EventLogSession.GlobalSession.GetLogInformation(logPath, pathType);
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

        if (!shouldAddLog)
        {
            await _cancellationTokenSource.CancelAsync();
            _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());
        }

        if (_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        _fluxorDispatcher.Dispatch(new EventLogAction.OpenLog(logPath, pathType, _cancellationTokenSource.Token));
    }

    private void OpenSettingsModal_Clicked(object sender, EventArgs e) => _settings.Load();

    private void PopulateOtherLogsMenu()
    {
        var names = EventLogSession.GlobalSession.GetLogNames();

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

            switch (_settings.CopyType)
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

    private void ViewLogs_Clicked(object? sender, EventArgs e) => _traceLogger.LoadDebugLog();

    private void ViewRecentFilters_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new FilterCacheAction.OpenMenu());
}
