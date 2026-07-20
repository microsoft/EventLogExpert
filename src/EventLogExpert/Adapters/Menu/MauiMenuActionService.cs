// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Export;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Update;
using EventLogExpert.UI.DatabaseTools;
using EventLogExpert.UI.DebugLog;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Settings;
using EventLogExpert.WindowsPlatform.Activation;
using Fluxor;
using System.Collections.Immutable;
using System.Globalization;
using Application = Microsoft.Maui.Controls.Application;
using IDispatcher = Fluxor.IDispatcher;
using MauiFilePicker = Microsoft.Maui.Storage.FilePicker;

namespace EventLogExpert.Adapters.Menu;

/// <summary>
///     MAUI implementation of <see cref="IMenuActionService" />. Owns the cancellation token that gates background
///     log loads (replaces the legacy field on MainPage). Exposes <see cref="OpenLogsBatchAsync" /> publicly so the
///     page-level drag/drop and command-line handlers can batch multiple opens through the same cancellation lifecycle and
///     surface one banner alert per gesture for empty logs.
/// </summary>
public sealed class MauiMenuActionService(
    IDispatcher dispatcher,
    IEventLogCommands eventLogCommands,
    IFilterLibraryCommands filterLibraryCommands,
    IFilterPaneCommands filterPaneCommands,
    IHistogramCommands histogramCommands,
    ILogTableCommands logTableCommands,
    IClipboardService clipboardService,
    IAlertDialogService dialogService,
    IModalCoordinator modalCoordinator,
    ISettingsService settings,
    IUpdateService updateService,
    ICurrentVersionProvider currentVersionProvider,
    ITraceLogger traceLogger,
    IFolderPickerService folderPickerService,
    IState<EventLogState> eventLogState,
    IState<LogTableState> logTableState,
    ILogTableColumnDefaultsProvider columnDefaults,
    IEventTableExporter eventTableExporter,
    IExportProgressBannerService exportProgressBannerService,
    IFileSaveService fileSaveService) : IMenuActionService, IDisposable
{
    private readonly IClipboardService _clipboardService = clipboardService;
    private readonly ILogTableColumnDefaultsProvider _columnDefaults = columnDefaults;
    private readonly ICurrentVersionProvider _currentVersionProvider = currentVersionProvider;
    private readonly IAlertDialogService _dialogService = dialogService;
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly IEventLogCommands _eventLogCommands = eventLogCommands;
    private readonly IState<EventLogState> _eventLogState = eventLogState;
    private readonly IEventTableExporter _eventTableExporter = eventTableExporter;
    private readonly IExportProgressBannerService _exportProgress = exportProgressBannerService;
    private readonly IFileSaveService _fileSaveService = fileSaveService;
    private readonly IFilterLibraryCommands _filterLibraryCommands = filterLibraryCommands;
    private readonly IFilterPaneCommands _filterPaneCommands = filterPaneCommands;
    private readonly IFolderPickerService _folderPickerService = folderPickerService;
    private readonly IHistogramCommands _histogramCommands = histogramCommands;
    private readonly ILogTableCommands _logTableCommands = logTableCommands;
    private readonly IState<LogTableState> _logTableState = logTableState;
    private readonly IModalCoordinator _modalCoordinator = modalCoordinator;
    private readonly ISettingsService _settings = settings;
    private readonly ITraceLogger _traceLogger = traceLogger;
    private readonly IUpdateService _updateService = updateService;

    private CancellationTokenSource _cancellationTokenSource = new();
    private bool _disposed;
    private int _exportInFlight;

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
            // Already disposed - continue tearing down remaining resources.
        }

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

    public async Task ExportEventsAsync(ExportFormat format)
    {
        var state = _logTableState.Value;
        var events = state.GetActiveDisplayedEvents();

        if (events.Count == 0)
        {
            await _dialogService.ShowAlert(
                "Export events", "There are no events to export.", "Ok", AlertPresentation.Banner);

            return;
        }

        // Snapshot everything the export needs before opening the picker, so a state change while the dialog is open
        // cannot mutate the rows/columns/timezone being written.
        var columns = state.GetOrderedEnabledColumns(_columnDefaults);

        if (columns.Count == 0)
        {
            await _dialogService.ShowAlert(
                "Export events", "There are no visible columns to export.", "Ok", AlertPresentation.Banner);

            return;
        }

        var timeZone = _settings.TimeZoneInfo;
        bool isCsv = format == ExportFormat.Csv;
        var fileTypes = isCsv ? FileSaveFileTypes.Csv : FileSaveFileTypes.Json;
        string extension = isCsv ? ".csv" : ".json";
        string suggestedFileName =
            $"events-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}{extension}";

        // Only one export may run at a time: the progress banner holds a single entry and the cancel
        // affordance owns a single CTS, so a concurrent export would orphan the first cancellation.
        if (Interlocked.CompareExchange(ref _exportInFlight, 1, 0) != 0)
        {
            await _dialogService.ShowAlert(
                "Export events", "An export is already in progress.", "Ok", AlertPresentation.Banner);

            return;
        }

        CancellationTokenSource cancellation = new();
        bool finished = false;
        string? savedPath = null;
        Exception? failure = null;
        bool canceled = false;

        try
        {
            savedPath = await _fileSaveService.SaveStreamingAsync(
                suggestedFileName,
                fileTypes,
                async (stream, token) =>
                {
                    // Begin only once the picker has returned a destination and the streaming write is
                    // starting; dismissing the picker never reaches here, so it stays silent. The finished
                    // flag turns a late Cancel click into a clean no-op.
                    _exportProgress.Begin(
                        "Exporting events...", () => { if (!Volatile.Read(ref finished)) { cancellation.Cancel(); } });

                    await _eventTableExporter.ExportAsync(
                        stream, format, events, columns, timeZone, includeDescription: true, token);

                    // The streaming write is done; stop honoring Cancel before SaveStreamingAsync runs its
                    // post-write cancel gates, so a late Cancel can't abort an already-written export's commit.
                    Volatile.Write(ref finished, true);
                },
                cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            Volatile.Write(ref finished, true);

            // End() raises StateChanged; isolate it so a throwing subscriber can never skip the CTS
            // disposal or the in-flight reset, which would otherwise wedge every later export.
            try
            {
                _exportProgress.End();
            }
            finally
            {
                cancellation.Dispose();
                Interlocked.Exchange(ref _exportInFlight, 0);
            }
        }

        if (canceled)
        {
            await _dialogService.ShowAlert(
                "Export canceled", "The export was canceled.", "Ok", AlertPresentation.Banner);

            return;
        }

        if (failure is not null)
        {
            _traceLogger.Error($"Failed to export events: {failure}");

            await _dialogService.ShowAlert("Export failed", failure.Message, "Ok", AlertPresentation.Banner);

            return;
        }

        // A null path means the user dismissed the save picker; only confirm an actual write.
        if (savedPath is not null)
        {
            await _dialogService.ShowAlert(
                "Export complete",
                $"Exported {events.Count:N0} {(events.Count == 1 ? "event" : "events")} to {savedPath}.",
                "Ok",
                AlertPresentation.Banner);
        }
    }

    public void LoadNewEvents() => _eventLogCommands.LoadNewEvents();

    public Task<bool> OpenDatabaseToolsAsync() =>
        TryOpenModalAsync(_modalCoordinator.OpenDatabaseToolsAsync, nameof(DatabaseToolsModal));

    public Task OpenDocsAsync() =>
        OpenBrowserAsync("https://github.com/microsoft/EventLogExpert/blob/main/docs/Home.md");

    public async Task OpenFileAsync(bool combineLog)
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>> { { DevicePlatform.WinUI, [".evtx"] } })
        };

        var files = await MauiFilePicker.Default.PickMultipleAsync(options);

        if (!files.Any()) { return; }

        var paths = files
            .OfType<FileResult>()
            .Where(file => !string.IsNullOrEmpty(file.FullPath))
            .Select(file => (file.FullPath, LogPathType.File))
            .ToList();

        await OpenLogsBatchAsync(paths, combineLog);
    }

    public async Task OpenFolderAsync(bool combineLog)
    {
        string? folderPath;

        try
        {
            folderPath = await _folderPickerService.PickFolderAsync();
        }
        catch (InvalidOperationException ex)
        {
            await _dialogService.ShowAlert("Open Folder Failed", ex.Message, "Ok");

            return;
        }

        if (folderPath is null) { return; }

        var result = EvtxFolderEnumerator.EnumerateEvtxTopLevel(folderPath);

        var alertCopy = EvtxFolderEnumerator.ToAlertCopy(result);

        if (alertCopy is { } copy)
        {
            await _dialogService.ShowAlert(copy.Title, copy.Message, "Ok");

            return;
        }

        if (result is not EvtxEnumerationResult.Success success) { return; }

        var files = success.Files
            .Select(file => (file, LogPathType.File))
            .ToList();

        await OpenLogsBatchAsync(files, combineLog);
    }

    public Task OpenIssueAsync() => OpenBrowserAsync("https://github.com/microsoft/EventLogExpert/issues/new");

    public Task OpenLiveLogAsync(string logName, bool combineLog) =>
        OpenLogsBatchCoreAsync([(logName, LogPathType.Channel)], combineLog, showInlineAlerts: true);

    public Task<OpenLogsBatchResult> OpenLiveLogsAsync(
        IEnumerable<string> logNames,
        bool combineLog,
        bool showInlineAlerts = true)
    {
        ArgumentNullException.ThrowIfNull(logNames);

        return OpenLogsBatchCoreAsync(logNames.Select(name => (name, LogPathType.Channel)), combineLog, showInlineAlerts);
    }

    public async Task<OpenLogStatus> OpenLogAsync(string logPath, LogPathType pathType, bool combineLog = false)
    {
        var outcome = await OpenLogWithOutcomeAsync(logPath, pathType, combineLog, showInlineAlerts: true);

        return outcome.Outcome switch
        {
            ChannelLaunchOutcome.Opened => OpenLogStatus.Opened,
            ChannelLaunchOutcome.Empty => OpenLogStatus.Empty,
            ChannelLaunchOutcome.Skipped => OpenLogStatus.Skipped,
            _ => OpenLogStatus.Failed
        };
    }

    public Task<OpenLogsBatchResult> OpenLogFilesAsync(
        IEnumerable<string> filePaths,
        bool combineLog,
        bool showInlineAlerts = true)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        return OpenLogsBatchCoreAsync(filePaths.Select(path => (path, LogPathType.File)), combineLog, showInlineAlerts);
    }

    public Task OpenLogsBatchAsync(IEnumerable<(string Path, LogPathType Type)> logs, bool combineLog) =>
        OpenLogsBatchCoreAsync(logs, combineLog, showInlineAlerts: true);

    public Task<bool> OpenSettingsAsync() =>
        TryOpenModalAsync(_modalCoordinator.OpenSettingsAsync, nameof(SettingsModal));

    public async Task SaveFiltersAsFilterSetAsync()
    {
        var filterSetName = await _dialogService.DisplayPrompt(
            "Filter Set Name",
            "What would you like to name this filter set?",
            "New Filter Set");

        if (string.IsNullOrWhiteSpace(filterSetName)) { return; }

        _filterLibraryCommands.SavePaneAsFilterSet(filterSetName);
    }

    public void SetAllGroupsCollapsed(bool collapsed) => _logTableCommands.SetAllGroupsCollapsed(collapsed);

    public void SetContinuouslyUpdate(bool value) =>
        _eventLogCommands.SetContinuouslyUpdate(value);

    public void SetHistogramVisible(bool value) => _histogramCommands.SetVisible(value);

    public Task ShowDebugLogsAsync() =>
        TryOpenModalAsync(_modalCoordinator.OpenDebugLogsAsync, nameof(DebugLogModal));

    public async Task ShowReleaseNotesAsync()
    {
        try
        {
            var content = await _updateService.GetReleaseNotes();

            if (content is null) { return; }

            await _modalCoordinator.OpenReleaseNotesAsync(content.Value);
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"Failed to display release notes: {ex}");
        }
    }

    public void ToggleGroupSortDirection() => _logTableCommands.ToggleGroupSortDirection();

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

    private async Task<OpenLogsBatchResult> OpenLogsBatchCoreAsync(
        IEnumerable<(string Path, LogPathType Type)> logs,
        bool combineLog,
        bool showInlineAlerts)
    {
        ArgumentNullException.ThrowIfNull(logs);

        List<string>? emptyDisplayNames = null;
        List<ChannelOutcome>? channelOutcomes = null;
        var combineForCall = combineLog;
        var opened = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var (path, type) in logs)
        {
            var outcome = await OpenLogWithOutcomeAsync(path, type, combineForCall, showInlineAlerts);

            if (type == LogPathType.Channel)
            {
                (channelOutcomes ??= []).Add(outcome);
            }

            switch (outcome.Outcome)
            {
                case ChannelLaunchOutcome.Opened:
                    opened++;
                    combineForCall = true;
                    break;
                case ChannelLaunchOutcome.Empty:
                    (emptyDisplayNames ??= []).Add(GetEmptyLogDisplayName(path, type));
                    break;
                case ChannelLaunchOutcome.AccessDenied:
                case ChannelLaunchOutcome.NotPresent:
                case ChannelLaunchOutcome.Failed:
                    failed++;
                    break;
                case ChannelLaunchOutcome.Skipped:
                    skipped++;
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

        return new OpenLogsBatchResult(
            opened,
            emptyDisplayNames?.Count ?? 0,
            failed,
            skipped,
            emptyDisplayNames?.ToImmutableArray() ?? [])
        {
            ChannelOutcomes = channelOutcomes?.ToImmutableArray() ?? []
        };
    }

    private async Task<ChannelOutcome> OpenLogWithOutcomeAsync(
        string logPath,
        LogPathType pathType,
        bool combineLog,
        bool showInlineAlerts)
    {
        if (string.IsNullOrWhiteSpace(logPath) ||
            (combineLog && _eventLogState.Value.IsLogOpen(logPath)))
        {
            return new ChannelOutcome(logPath, ChannelLaunchOutcome.Skipped);
        }

        EventLogInformation? eventLogInformation;

        try
        {
            eventLogInformation = EventLogSession.GlobalSession.GetLogInformation(logPath, pathType);
        }
        catch (UnauthorizedAccessException)
        {
            if (showInlineAlerts)
            {
                await _dialogService.ShowAlert(
                    "Log requires elevation",
                    "Please relaunch with \"Run as Administrator\" to open this log",
                    "Ok");
            }

            return new ChannelOutcome(logPath, ChannelLaunchOutcome.AccessDenied);
        }
        catch (FileNotFoundException)
        {
            if (showInlineAlerts)
            {
                await _dialogService.ShowAlert(
                    "Failed to open Log",
                    $"The log {logPath} was not found.",
                    "Ok");
            }

            return new ChannelOutcome(logPath, ChannelLaunchOutcome.NotPresent);
        }
        catch (Exception ex)
        {
            if (showInlineAlerts)
            {
                await _dialogService.ShowAlert("Failed to open Log", $"Exception: {ex.Message}", "Ok");
            }

            return new ChannelOutcome(logPath, ChannelLaunchOutcome.Failed);
        }

        if (eventLogInformation.RecordCount is null or <= 0)
        {
            return new ChannelOutcome(logPath, ChannelLaunchOutcome.Empty);
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

        _eventLogCommands.OpenLog(logPath, pathType, _cancellationTokenSource.Token);

        return new ChannelOutcome(logPath, ChannelLaunchOutcome.Opened);
    }

    private async Task<bool> TryOpenModalAsync(Func<Task<ModalOpenResult<bool>>> open, string modalName)
    {
        try
        {
            ModalOpenResult<bool> result = await open();

            if (!result.WasOpened) { _traceLogger.Trace($"{modalName} open preempted by an active modal."); }

            // Propagate WasOpened so callers like AttentionBanner can surface a fallback error when the
            // coordinator vetoes preemption (returns ModalOpenResult.NotOpened) rather than misreading
            // a no-op as a successful open.
            return result.WasOpened;
        }
        catch (Exception ex)
        {
            _traceLogger.Error($"Failed to open {modalName}: {ex}");

            return false;
        }
    }
}
