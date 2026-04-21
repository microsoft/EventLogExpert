// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Platforms.Windows;
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
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
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
    private readonly IFileLogger _fileLogger;
    private readonly IDispatcher _fluxorDispatcher;
    private readonly ISettingsService _settings;
    private readonly ITraceLogger _traceLogger;
    private readonly IUpdateService _updateService;

    private CancellationTokenSource _cancellationTokenSource = new();
    private Microsoft.Web.WebView2.Core.CoreWebView2? _coreWebView;

    public MainPage(
        IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogsState,
        IStateSelection<EventLogState, bool> continuouslyUpdateState,
        IStateSelection<FilterPaneState, bool> filterPaneIsEnabledState,
        IDatabaseService databaseService,
        ISettingsService settings,
        IAlertDialogService dialogService,
        IClipboardService clipboardService,
        IUpdateService updateService,
        ICurrentVersionProvider currentVersionProvider,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        ITraceLogger traceLogger,
        IFileLogger fileLogger)
    {
        InitializeComponent();
        PopulateOtherLogsMenu();

        _activeLogsState = activeLogsState;
        _clipboardService = clipboardService;
        _currentVersionProvider = currentVersionProvider;
        _databaseService = databaseService;
        _fileLogger = fileLogger;
        _fluxorDispatcher = fluxorDispatcher;
        _settings = settings;
        _dialogService = dialogService;
        _traceLogger = traceLogger;
        _updateService = updateService;

        activeLogsState.Select(e => e.ActiveLogs);

        activeLogsState.SelectedValueChanged += (_, activeLogs) =>
            MainThread.InvokeOnMainThreadAsync(() =>
                appTitleService.SetLogName(activeLogs.Count <= 0 ? null : string.Join(" | ", activeLogs.Values.Select(l => l.Name))));

        continuouslyUpdateState.Select(e => e.ContinuouslyUpdate);

        continuouslyUpdateState.SelectedValueChanged += (_, continuouslyUpdate) =>
            MainThread.InvokeOnMainThreadAsync(() =>
                ContinuouslyUpdateMenuItem.Text = $"Continuously Update{(continuouslyUpdate ? " ✓" : "")}");

        filterPaneIsEnabledState.Select(e => e.IsEnabled);

        filterPaneIsEnabledState.SelectedValueChanged += (_, isEnabled) =>
            MainThread.InvokeOnMainThreadAsync(() =>
                ShowAllEventsMenuItem.Text = $"Show All Events{(isEnabled ? "" : " ✓")}");

        _databaseService.LoadedDatabasesChanged += (_, loadedProviders) =>
            databaseCollectionProvider.SetActiveDatabases(loadedProviders.Select(path =>
                Path.Join(fileLocationOptions.DatabasePath, path)));

        _settings.CopyTypeChanged += SetCopyKeyboardAccelerator;
        _settings.ThemeChanged += OnThemeChanged;

        fluxorDispatcher.Dispatch(new EventTableAction.LoadColumns());
        fluxorDispatcher.Dispatch(new FilterCacheAction.LoadFilters());
        fluxorDispatcher.Dispatch(new FilterGroupAction.LoadGroups());

        _ = ProcessCommandLine();
    }

    public void Dispose()
    {
        _settings.CopyTypeChanged -= SetCopyKeyboardAccelerator;
        _settings.ThemeChanged -= OnThemeChanged;

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

    private static async Task<IEnumerable<FileResult?>> GetFilePickerResultAsync()
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>> { { DevicePlatform.WinUI, [".evtx"] } })
        };

        return await FilePicker.Default.PickMultipleAsync(options);
    }

    private static async Task<IEnumerable<FileResult>> GetFolderPickerResultAsync()
    {
        string? folderPath = await FolderPickerHelper.PickFolderAsync();

        if (folderPath is null)
        {
            return [];
        }

        List<FileResult> fileResults = [];

        foreach (string file in Directory.EnumerateFiles(folderPath, "*.evtx", SearchOption.TopDirectoryOnly))
        {
            var fileResult = new FileResult(file);

            fileResults.Add(fileResult);
        }

        return fileResults;
    }

    private void ApplyWebViewTheme(Theme theme)
    {
        // Keep WebView2's prefers-color-scheme aligned with the user's choice
        // so the "System" path (data-theme attribute removed) actually follows
        // the OS, and so explicit Light/Dark stays consistent across the page.
        _coreWebView?.Profile.PreferredColorScheme = theme switch
        {
            Theme.Light => CoreWebView2PreferredColorScheme.Light,
            Theme.Dark => CoreWebView2PreferredColorScheme.Dark,
            _ => CoreWebView2PreferredColorScheme.Auto,
        };
    }

    private async void CheckForUpdates_Clicked(object? sender, EventArgs e)
    {
        if (!_currentVersionProvider.IsSupportedOS(DeviceInfo.Version))
        {
            _traceLogger.Warn($"Update API does not work on versions older than 10.0.19041.0");
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

    private async void CopySelected_Clicked(object sender, EventArgs e)
    {
        var item = sender as MenuFlyoutItem;
        var param = item?.CommandParameter as CopyType?;

        // ClipboardService.CopySelectedEvent is best-effort and swallows exceptions internally,
        // so no caller-side try/catch is needed even from this async void handler.
        await _clipboardService.CopySelectedEvent(param);
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
            if (item is not StorageFile file || !extensions.Contains(file.FileType, StringComparer.OrdinalIgnoreCase))
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

    private void Exit_Clicked(object sender, EventArgs e) =>
        Application.Current?.CloseWindow(Application.Current.Windows[0].Page!.Window!);

    private void LoadNewEvents_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new EventLogAction.LoadNewEvents());

    private void MainWebView_BlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        // Inject the saved theme synchronously into every document the WebView2
        // creates so the page renders with the correct palette on first paint
        // (avoids the dark→light flash while Blazor boots and calls setTheme).
        // Also align WebView2's prefers-color-scheme with the saved choice so
        // the System theme path lights up the right CSS branch immediately.
        _coreWebView = e.WebView.CoreWebView2;

        if (_coreWebView is null) { return; }

        ApplyWebViewTheme(_settings.Theme);

        var themeAttr = _settings.Theme switch
        {
            Theme.Light => "light",
            Theme.Dark => "dark",
            _ => null,
        };

        var script = themeAttr is null
            ? "document.documentElement.removeAttribute('data-theme');"
            : $"document.documentElement.setAttribute('data-theme','{themeAttr}');";

        _ = _coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }

    private void OnThemeChanged() =>
        // ThemeChanged may be raised from non-UI threads; marshal to the UI
        // thread before touching WebView2's profile.
        MainThread.BeginInvokeOnMainThread(() => ApplyWebViewTheme(_settings.Theme));

    // TODO: Extract this so it can be called from the MainPage keyup handler
    private async void OpenFile_Clicked(object sender, EventArgs e)
    {
        if (sender is not MenuFlyoutItem item) { return; }

        bool shouldAddLog = item.CommandParameter is true;

        // Returns null if cancelled
        var files = await GetFilePickerResultAsync();

        if (files is null || !files.Any()) { return; }

        if (!shouldAddLog)
        {
            await _cancellationTokenSource.CancelAsync();
            _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());
        }

        foreach (var file in files)
        {
            if (file?.FullPath is null) { continue; }

            await OpenLog(file.FullPath, PathType.FilePath, shouldAddLog: true);
        }
    }

    private async void OpenFolder_Clicked(object sender, EventArgs e)
    {
        if (sender is not MenuFlyoutItem item) { return; }

        bool shouldAddLog = item.CommandParameter is true;

        var files = await GetFolderPickerResultAsync();

        if (!files.Any()) { return; }

        if (!shouldAddLog)
        {
            await _cancellationTokenSource.CancelAsync();
            _fluxorDispatcher.Dispatch(new EventLogAction.CloseAll());
        }

        foreach (var file in files)
        {
            if (file?.FullPath is null) { continue; }

            await OpenLog(file.FullPath, PathType.FilePath, shouldAddLog: true);
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

    private async Task ProcessCommandLine()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();

            foreach (var arg in args)
            {
                switch (arg)
                {
                    case not null when arg.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase):
                        await OpenLog(arg, PathType.FilePath);
                        break;
                }
            }
        }
        catch (Exception e)
        {
            _traceLogger.Error($"Failed to process command line arguments: {e}");
        }
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

    private void ViewLogs_Clicked(object? sender, EventArgs e) => _fileLogger.LoadDebugLog();

    private void ViewRecentFilters_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new FilterCacheAction.OpenMenu());
}
