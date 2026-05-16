// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Modals;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Platforms.Windows;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Common.Clipboard;
using EventLogExpert.UI.Common.Lifecycle;
using EventLogExpert.UI.Common.Versioning;
using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.FilterPane;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Settings;
using EventLogExpert.UI.Update;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Application = Microsoft.Maui.Controls.Application;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Services;

/// <summary>
///     MAUI implementation of <see cref="IMenuActionService" />. Owns the cancellation token that gates background
///     log loads (replaces the legacy field on MainPage). Exposes <see cref="OpenLogsBatchAsync" /> publicly so the
///     page-level drag/drop and command-line handlers can batch multiple opens through the same cancellation lifecycle and
///     surface one banner alert per gesture for empty logs.
/// </summary>
public sealed class MauiMenuActionService(
    IDispatcher dispatcher,
    IEventLogCommands eventLogCommands,
    IFilterPaneCommands filterPaneCommands,
    IClipboardService clipboardService,
    IAlertDialogService dialogService,
    IModalService modalService,
    ISettingsService settings,
    IUpdateService updateService,
    ICurrentVersionProvider currentVersionProvider,
    ITraceLogger traceLogger,
    IState<EventLogState> eventLogState) : IMenuActionService, IDisposable
{
    private readonly IClipboardService _clipboardService = clipboardService;
    private readonly ICurrentVersionProvider _currentVersionProvider = currentVersionProvider;
    private readonly IAlertDialogService _dialogService = dialogService;
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly IEventLogCommands _eventLogCommands = eventLogCommands;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IFilterPaneCommands _filterPaneCommands = filterPaneCommands;
    private readonly SemaphoreSlim _logNamesLock = new(1, 1);
    private readonly IModalService _modalService = modalService;
    private readonly ISettingsService _settings = settings;
    private readonly ITraceLogger _traceLogger = traceLogger;
    private readonly IUpdateService _updateService = updateService;

    private IReadOnlyList<string>? _cachedLogNames;
    private CancellationTokenSource _cancellationTokenSource = new();
    private bool _disposed;

    public async Task CheckForUpdatesAsync()
    {
        if (!_currentVersionProvider.IsSupportedOS(DeviceInfo.Version))
        {
            _traceLogger.Warning($"Update API does not work on versions older than 10.0.19041.0");

            return;
        }

        await _updateService.CheckForUpdates(_settings.IsPreReleaseEnabled, true);
    }

    public async Task CloseAllLogsAsync()
    {
        await _cancellationTokenSource.CancelAsync();
        _dispatcher.Dispatch(new CloseAllLogsAction());
    }

    public async Task CopySelectedAsync(EventCopyFormat? format) => await _clipboardService.CopySelectedEvent(format);

    public void Dispose()
    {
        if (_disposed) { return; }

        _disposed = true;

        // Best-effort cancellation so any in-flight OpenLog effects stop linking new callbacks
        // to a token whose CTS is about to be disposed (which would surface ObjectDisposedException).
        try
        {
            _cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed — continue tearing down remaining resources.
        }

        _logNamesLock.Dispose();
        _cancellationTokenSource.Dispose();
    }

    public void Exit()
    {
        var current = Application.Current;
        var window = current?.Windows.Count > 0 ? current.Windows[0].Page?.Window : null;

        if (current is not null && window is not null)
        {
            current.CloseWindow(window);
        }
    }

    public async Task<IReadOnlyList<string>> GetOtherLogNamesAsync()
    {
        if (_cachedLogNames is not null) { return _cachedLogNames; }

        await _logNamesLock.WaitAsync();

        try
        {
            if (_cachedLogNames is not null) { return _cachedLogNames; }

            _cachedLogNames = await Task.Run<IReadOnlyList<string>>(() =>
                EventLogSession.GlobalSession.GetLogNames()
                    .Where(name => !LogChannelMethods.HardCodedLiveChannels.Contains(name))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            return _cachedLogNames;
        }
        finally
        {
            _logNamesLock.Release();
        }
    }

    public void LoadNewEvents() => _eventLogCommands.LoadNewEvents();

    public Task OpenDocsAsync() =>
        OpenBrowserAsync("https://github.com/microsoft/EventLogExpert/blob/main/docs/Home.md");

    public async Task OpenFileAsync(bool combineLog)
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>> { { DevicePlatform.WinUI, [".evtx"] } })
        };

        var files = await FilePicker.Default.PickMultipleAsync(options);

        if (!files.Any()) { return; }

        var paths = files
            .Where(file => file is not null && !string.IsNullOrEmpty(file.FullPath))
            .Select(file => (file!.FullPath, LogPathType.File))
            .ToList();

        await OpenLogsBatchAsync(paths, combineLog);
    }

    public async Task OpenFolderAsync(bool combineLog)
    {
        string? folderPath;

        try
        {
            folderPath = await FolderPickerHelper.PickFolderAsync();
        }
        catch (InvalidOperationException ex)
        {
            await _dialogService.ShowAlert("Open Folder Failed", ex.Message, "OK");

            return;
        }

        if (folderPath is null) { return; }

        List<(string, LogPathType)> files;

        try
        {
            files = Directory.EnumerateFiles(folderPath, "*.evtx", SearchOption.TopDirectoryOnly)
                .Select(file => (file, LogPathType.File))
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            await _dialogService.ShowAlert("Open Folder Failed", ex.Message, "OK");

            return;
        }

        if (files.Count == 0) { return; }

        await OpenLogsBatchAsync(files, combineLog);
    }

    public Task OpenIssueAsync() => OpenBrowserAsync("https://github.com/microsoft/EventLogExpert/issues/new");

    public Task OpenLiveLogAsync(string logName, bool combineLog) =>
        OpenLogsBatchAsync([(logName, LogPathType.Channel)], combineLog);

    public async Task<OpenLogStatus> OpenLogAsync(string logPath, LogPathType pathType, bool combineLog = false)
    {
        if (string.IsNullOrWhiteSpace(logPath) ||
            (combineLog && _eventLogState.Value.ActiveLogs.ContainsKey(logPath))) { return OpenLogStatus.Skipped; }

        EventLogInformation? eventLogInformation;

        try
        {
            eventLogInformation = EventLogSession.GlobalSession.GetLogInformation(logPath, pathType);
        }
        catch (UnauthorizedAccessException)
        {
            await _dialogService.ShowAlert(
                "Log requires elevation",
                "Please relaunch with \"Run as Administrator\" to open this log",
                "Ok");

            return OpenLogStatus.Failed;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowAlert("Failed to open Log", $"Exception: {ex.Message}", "Ok");

            return OpenLogStatus.Failed;
        }

        if (eventLogInformation.RecordCount is null or <= 0)
        {
            return OpenLogStatus.Empty;
        }

        if (!combineLog)
        {
            await _cancellationTokenSource.CancelAsync();
            _dispatcher.Dispatch(new CloseAllLogsAction());
        }

        if (_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        _dispatcher.Dispatch(new OpenLogAction(logPath, pathType, _cancellationTokenSource.Token));
        return OpenLogStatus.Opened;
    }

    /// <summary>
    ///     Opens each log in <paramref name="logs" /> sequentially and surfaces a single banner alert at the end naming
    ///     every log that contained zero events. <paramref name="combineLog" /> controls whether the first successful open
    ///     closes existing logs first; subsequent opens within the batch always coalesce with the new state. Use this from any
    ///     call site that may open multiple logs in one user gesture (multi-file picker, folder open, drag-drop, command line)
    ///     so the user sees one batched alert instead of one popup per empty file.
    /// </summary>
    public async Task OpenLogsBatchAsync(IEnumerable<(string Path, LogPathType Type)> logs, bool combineLog)
    {
        ArgumentNullException.ThrowIfNull(logs);

        List<string>? emptyDisplayNames = null;
        var combineForCall = combineLog;

        foreach (var (path, type) in logs)
        {
            // Only Opened consumed the close-existing semantics; Skipped/Failed/Empty did not,
            // so combineForCall must NOT flip until a real open happens.
            switch (await OpenLogAsync(path, type, combineForCall))
            {
                case OpenLogStatus.Opened:
                    combineForCall = true;
                    break;
                case OpenLogStatus.Empty:
                    (emptyDisplayNames ??= []).Add(GetEmptyLogDisplayName(path, type));
                    break;
            }
        }

        if (emptyDisplayNames is { Count: > 0 })
        {
            await _dialogService.ShowAlert(
                "Empty log",
                EmptyLogAlertFormatter.BuildMessage(emptyDisplayNames),
                "Ok",
                AlertPresentation.Banner);
        }
    }

    public Task<bool> OpenSettingsAsync() => ShowModalAsync<SettingsModal>("settings");

    public async Task SaveFiltersAsGroupAsync()
    {
        var groupName = await _dialogService.DisplayPrompt(
            "Group Name",
            "What would you like to name this group?",
            "New Filter Section\\New Filter Group");

        if (string.IsNullOrEmpty(groupName)) { return; }

        _dispatcher.Dispatch(new SaveFilterGroupAction(groupName));
    }

    public void SetContinuouslyUpdate(bool value) =>
        _dispatcher.Dispatch(new SetContinuouslyUpdateAction(value));

    public Task ShowDebugLogsAsync() => ShowModalAsync<DebugLogModal>("debug logs");

    public async Task ShowReleaseNotesAsync()
    {
        try
        {
            var content = await _updateService.GetReleaseNotes();

            if (content is null) { return; }

            await _modalService.Show<ReleaseNotesModal, bool>(
                new Dictionary<string, object?> { ["Content"] = content.Value });
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"Failed to display release notes: {ex}");
        }
    }

    public void ToggleShowAllEvents() => _filterPaneCommands.ToggleFilteringEnabled();

    private static string GetEmptyLogDisplayName(string path, LogPathType type)
    {
        if (type != LogPathType.File) { return path; }

        var fileName = Path.GetFileName(path);

        return string.IsNullOrEmpty(fileName) ? path : fileName;
    }

    private async Task OpenBrowserAsync(string url)
    {
        try
        {
            await Browser.Default.OpenAsync(new Uri(url), BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowAlert("Failed to launch browser", ex.Message, "Ok");
        }
    }

    private async Task<bool> ShowModalAsync<TModal>(string label)
        where TModal : IComponent
    {
        try
        {
            await _modalService.Show<TModal, bool>();

            return true;
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"Failed to open {label} modal: {ex}");

            return false;
        }
    }
}
