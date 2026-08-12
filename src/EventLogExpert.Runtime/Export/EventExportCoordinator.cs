// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using Fluxor;
using System.Globalization;

namespace EventLogExpert.Runtime.Export;

public sealed class EventExportCoordinator(
    IState<LogTableState> logTableState,
    IEventTableExporter eventTableExporter,
    IFileSaveService fileSaveService,
    IExportProgressBannerService exportProgress,
    IAlertDialogService dialogService,
    ISettingsService settings,
    ILogTableColumnDefaultsProvider columnDefaults,
    ITraceLogger traceLogger)
{
    private readonly ILogTableColumnDefaultsProvider _columnDefaults = columnDefaults;
    private readonly IAlertDialogService _dialogService = dialogService;
    private readonly IEventTableExporter _eventTableExporter = eventTableExporter;
    private readonly IExportProgressBannerService _exportProgress = exportProgress;
    private readonly IFileSaveService _fileSaveService = fileSaveService;
    private readonly IState<LogTableState> _logTableState = logTableState;
    private readonly ISettingsService _settings = settings;
    private readonly ITraceLogger _traceLogger = traceLogger;

    private int _exportInFlight;

    public async Task ExportEventsAsync(ExportFormat format)
    {
        var state = _logTableState.Value;
        var events = state.GetActiveDisplayedEvents();

        if (events.Count == 0)
        {
            string reason = state.PresentationState switch
            {
                PresentationState.Faulted => "These events cannot be exported because the view could not be prepared.",
                PresentationState.Updating =>
                    "These events are still being prepared. Please try again once they have finished loading.",
                _ => "There are no events to export."
            };

            await _dialogService.ShowAlert("Export events", reason, "Ok", AlertPresentation.Banner);

            return;
        }

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

        if (Interlocked.CompareExchange(ref _exportInFlight, 1, 0) != 0)
        {
            await _dialogService.ShowAlert(
                "Export events", "An export is already in progress.", "Ok", AlertPresentation.Banner);

            return;
        }

        CancellationTokenSource cancellation = new();
        string? savedPath = null;
        Exception? failure = null;
        bool canceled = false;

        try
        {
            savedPath = await _fileSaveService.SaveStreamingAsync(
                suggestedFileName,
                fileTypes,
                async (stream, _) =>
                {
                    _exportProgress.Begin(
                        "Exporting events...",
                        () =>
                        {
                            try { cancellation.Cancel(); }
                            catch (ObjectDisposedException) { /* Teardown disposed the CTS; a late Cancel is a no-op. */ }
                        });

                    await _eventTableExporter.ExportAsync(
                        stream, format, events, columns, timeZone, includeDescription: true, cancellation.Token);
                },
                CancellationToken.None);
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

        if (savedPath is not null)
        {
            await _dialogService.ShowAlert(
                "Export complete",
                $"Exported {events.Count:N0} {(events.Count == 1 ? "event" : "events")} to {savedPath}.",
                "Ok",
                AlertPresentation.Banner);
        }
    }
}
