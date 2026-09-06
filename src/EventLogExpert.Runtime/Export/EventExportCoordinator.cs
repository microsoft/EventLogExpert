// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
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
    IInfoBannerService infoBanners,
    ISettingsService settings,
    ILogTableColumnDefaultsProvider columnDefaults,
    ITraceLogger traceLogger)
{
    private readonly ILogTableColumnDefaultsProvider _columnDefaults = columnDefaults;
    private readonly IEventTableExporter _eventTableExporter = eventTableExporter;
    private readonly IExportProgressBannerService _exportProgress = exportProgress;
    private readonly IFileSaveService _fileSaveService = fileSaveService;
    private readonly IInfoBannerService _infoBanners = infoBanners;
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
            ExportBlockReason reason = state.PresentationState switch
            {
                PresentationState.Faulted => ExportBlockReason.Faulted,
                PresentationState.Updating => ExportBlockReason.Updating,
                _ => ExportBlockReason.NoEvents
            };

            _infoBanners.ReportInfoBanner(new ExportBlocked(reason), BannerSeverity.Warning);

            return;
        }

        var columns = state.GetOrderedEnabledColumns(_columnDefaults);

        if (columns.Count == 0)
        {
            _infoBanners.ReportInfoBanner(new ExportBlocked(ExportBlockReason.NoColumns), BannerSeverity.Warning);

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
            _infoBanners.ReportInfoBanner(new ExportBlocked(ExportBlockReason.AlreadyInProgress), BannerSeverity.Warning);

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
                    _exportProgress.Begin(() =>
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
            _infoBanners.ReportInfoBanner(new ExportCanceled(), BannerSeverity.Warning);

            return;
        }

        if (failure is not null)
        {
            _traceLogger.Error($"Failed to export events: {failure}");

            _infoBanners.ReportInfoBanner(new ExportFailed(failure.Message), BannerSeverity.Warning);

            return;
        }

        if (savedPath is not null)
        {
            _infoBanners.ReportInfoBanner(new ExportComplete(events.Count, savedPath), BannerSeverity.Warning);
        }
    }
}
