// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Platforms.Windows;
using EventLogExpert.Shared.Components;
using EventLogExpert.Shared.Components.Filters;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Application = Microsoft.Maui.Controls.Application;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Services;

/// <summary>
///     MAUI implementation of <see cref="IMenuActionService" />. Owns the cancellation token that gates background
///     log loads (replaces the legacy field on MainPage). Exposes <see cref="OpenLogAsync" /> publicly so the page-level
///     drag/drop handler can reuse the same cancellation lifecycle.
/// </summary>
public sealed class MauiMenuActionService(
    IDispatcher dispatcher,
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
    private readonly IState<EventLogState> _eventLogState = eventLogState;
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
            _traceLogger.Warn($"Update API does not work on versions older than 10.0.19041.0");
            return;
        }

        await _updateService.CheckForUpdates(_settings.IsPreReleaseEnabled, userInitiated: true);
    }

    public void ClearAllFilters() => _dispatcher.Dispatch(new FilterPaneAction.ClearAllFilters());

    public async Task CloseAllLogsAsync()
    {
        await _cancellationTokenSource.CancelAsync();
        _dispatcher.Dispatch(new EventLogAction.CloseAll());
    }

    public async Task CopySelectedAsync(CopyType? copyType) => await _clipboardService.CopySelectedEvent(copyType);

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
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            return _cachedLogNames;
        }
        finally
        {
            _logNamesLock.Release();
        }
    }

    public void LoadNewEvents() => _dispatcher.Dispatch(new EventLogAction.LoadNewEvents());

    public Task OpenDocsAsync() =>
        OpenBrowserAsync("https://github.com/microsoft/EventLogExpert/blob/main/docs/Home.md");

    public async Task OpenFileAsync(bool addLog)
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>> { { DevicePlatform.WinUI, [".evtx"] } })
        };

        var files = await FilePicker.Default.PickMultipleAsync(options);

        if (!files.Any()) { return; }

        if (!addLog)
        {
            await CloseAllLogsAsync();
        }

        foreach (var file in files)
        {
            if (file?.FullPath is null) { continue; }

            await OpenLogAsync(file.FullPath, PathType.FilePath, true);
        }
    }

    public async Task OpenFolderAsync(bool addLog)
    {
        string? folderPath = await FolderPickerHelper.PickFolderAsync();

        if (folderPath is null) { return; }

        var files = Directory.EnumerateFiles(folderPath, "*.evtx", SearchOption.TopDirectoryOnly).ToList();

        if (files.Count == 0) { return; }

        if (!addLog)
        {
            await CloseAllLogsAsync();
        }

        foreach (var file in files)
        {
            await OpenLogAsync(file, PathType.FilePath, true);
        }
    }

    public Task OpenIssueAsync() => OpenBrowserAsync("https://github.com/microsoft/EventLogExpert/issues/new");

    public Task OpenLiveLogAsync(string logName, bool addLog) => OpenLogAsync(logName, PathType.LogName, addLog);

    public async Task OpenLogAsync(string logPath, PathType pathType, bool shouldAddLog = false)
    {
        if (string.IsNullOrWhiteSpace(logPath)) { return; }

        if (shouldAddLog && _eventLogState.Value.ActiveLogs.ContainsKey(logPath)) { return; }

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
            _dispatcher.Dispatch(new EventLogAction.CloseAll());
        }

        if (_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        _dispatcher.Dispatch(new EventLogAction.OpenLog(logPath, pathType, _cancellationTokenSource.Token));
    }

    public Task OpenSettingsAsync() => ShowModalAsync<SettingsModal>("settings");

    public async Task SaveAllFiltersAsync()
    {
        var groupName = await _dialogService.DisplayPrompt(
            "Group Name",
            "What would you like to name this group?",
            "New Filter Section\\New Filter Group");

        if (string.IsNullOrEmpty(groupName)) { return; }

        _dispatcher.Dispatch(new FilterPaneAction.SaveFilterGroup(groupName));
    }

    public void SetContinuouslyUpdate(bool value) =>
        _dispatcher.Dispatch(new EventLogAction.SetContinuouslyUpdate(value));

    public Task ShowDebugLogsAsync() => ShowModalAsync<DebugLogModal>("debug logs");

    public Task ShowFilterCacheAsync() => ShowModalAsync<FilterCacheModal>("filter cache");

    public Task ShowFilterGroupsAsync() => ShowModalAsync<FilterGroupModal>("filter groups");

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

    public void ToggleShowAllEvents() => _dispatcher.Dispatch(new FilterPaneAction.ToggleIsEnabled());

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

    private async Task ShowModalAsync<TModal>(string label)
        where TModal : IComponent
    {
        try
        {
            await _modalService.Show<TModal, bool>();
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"Failed to open {label} modal: {ex}");
        }
    }
}
