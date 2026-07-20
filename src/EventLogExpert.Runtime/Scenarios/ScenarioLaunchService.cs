// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Concurrency;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Scenarios.Catalog;
using Fluxor;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Scenarios;

internal sealed class ScenarioLaunchService(
    BuiltInScenarioRegistry registry,
    IMenuActionService menuActionService,
    IState<FilterPaneState> filterPaneState,
    IState<HistogramState> histogramState,
    IFolderPickerService folderPicker,
    IEvtxFolderEnumerator folderEnumerator,
    IEvtxChannelReader channelReader,
    IDispatcher dispatcher) : IScenarioLaunchService
{
    private readonly IEvtxChannelReader _channelReader = channelReader;
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly IState<FilterPaneState> _filterPaneState = filterPaneState;
    private readonly IEvtxFolderEnumerator _folderEnumerator = folderEnumerator;
    private readonly IFolderPickerService _folderPicker = folderPicker;
    private readonly IState<HistogramState> _histogramState = histogramState;
    private readonly IMenuActionService _menuActionService = menuActionService;
    private readonly BuiltInScenarioRegistry _registry = registry;

    public async Task<ScenarioLaunchResult> LaunchAsync(
        ScenarioDefinition scenario,
        DateFilter? dateWindow,
        bool combineLog = false)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var priorFilterState = _filterPaneState.Value;

        var filters = _registry.BuildFilterSet(scenario);

        _dispatcher.Dispatch(new ReplaceFiltersAction(filters));
        _dispatcher.Dispatch(new SetFilterDateRangeAction(dateWindow));

        var result = await _menuActionService.OpenLiveLogsAsync(scenario.Channels, combineLog);

        if (result.Opened == 0 && !combineLog)
        {
            _dispatcher.Dispatch(new CloseAllLogsAction());
            _dispatcher.Dispatch(new RestoreFilterPaneStateAction(priorFilterState));
        }
        else if (scenario.ActivatesTimeline)
        {
            if (scenario.TimelineDimension is { } timelineDimension)
            {
                _dispatcher.Dispatch(new RequestHistogramDimensionAction(MapTimelineDimension(timelineDimension)));
            }

            if (!_histogramState.Value.IsVisible)
            {
                _menuActionService.SetHistogramVisible(true);
            }
        }

        return new ScenarioLaunchResult(result.Opened, result.Empty, result.Failed);
    }

    public async Task<ScenarioFolderLaunchResult> LaunchFromFolderAsync(
        ScenarioDefinition scenario,
        DateFilter? dateWindow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        string? folder;

        try
        {
            folder = await _folderPicker.PickFolderAsync();
        }
        catch (InvalidOperationException ex)
        {
            return ScenarioFolderLaunchResult.Error(ex.Message);
        }

        if (folder is null) { return ScenarioFolderLaunchResult.Cancelled; }

        FolderMatch match;

        try
        {
            var scan = await Task.Run(
                () => _folderEnumerator.EnumerateTopLevel(folder, cancellationToken), cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            switch (scan)
            {
                case EvtxFolderScanResult.AccessDenied denied:
                    return ScenarioFolderLaunchResult.Error(denied.Message);
                case EvtxFolderScanResult.IoError ioError:
                    return ScenarioFolderLaunchResult.Error(ioError.Message);
                case EvtxFolderScanResult.Empty:
                    return new ScenarioFolderLaunchResult
                    {
                        Outcome = ScenarioFolderOutcome.NoMatchingLogs,
                        MissingChannels = AllTargetChannels(scenario)
                    };
                case EvtxFolderScanResult.Files files:
                    match = await ScanAndMatchAsync(files.Paths, scenario, cancellationToken);

                    // The parallel probe can finish before it observes a late cancellation; re-check here so a scan
                    // cancelled while probing does not fall through to opening logs or dispatching filters.
                    cancellationToken.ThrowIfCancellationRequested();

                    break;
                default:
                    return ScenarioFolderLaunchResult.Error("The folder could not be read.");
            }
        }
        catch (OperationCanceledException)
        {
            return ScenarioFolderLaunchResult.Cancelled;
        }

        // A late cancellation (for example, dashboard disposal) can land in the synchronous gap between the in-try
        // recheck and the commit. Gate it here WITHOUT throwing so a cancelled scan never dispatches filters or opens
        // logs, while a genuine fault surfacing from the open still propagates (the commit stays outside the catch).
        if (cancellationToken.IsCancellationRequested) { return ScenarioFolderLaunchResult.Cancelled; }

        return await OpenMatchedLogsAsync(scenario, dateWindow, match);
    }

    private static ImmutableArray<string> AllTargetChannels(ScenarioDefinition scenario) =>
        [.. TargetChannelSet(scenario).OrderBy(channel => channel, StringComparer.OrdinalIgnoreCase)];

    private static string FilesWord(int count) => count == 1 ? "1 file" : $"{count} files";

    private static HistogramDimension MapTimelineDimension(ScenarioTimelineDimension dimension) => dimension switch
    {
        ScenarioTimelineDimension.Severity => HistogramDimension.Severity,
        ScenarioTimelineDimension.Source => HistogramDimension.Source,
        ScenarioTimelineDimension.EventId => HistogramDimension.EventId,
        ScenarioTimelineDimension.TaskCategory => HistogramDimension.TaskCategory,
        ScenarioTimelineDimension.Opcode => HistogramDimension.Opcode,
        ScenarioTimelineDimension.Log => HistogramDimension.Log,
        ScenarioTimelineDimension.LogonType => HistogramDimension.LogonType,
        ScenarioTimelineDimension.TicketEncryptionType => HistogramDimension.TicketEncryptionType,
        ScenarioTimelineDimension.ErrorCode => HistogramDimension.ErrorCode,
        ScenarioTimelineDimension.ProcessImage => HistogramDimension.ProcessImage,
        ScenarioTimelineDimension.ParentProcessImage => HistogramDimension.ParentProcessImage,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null)
    };

    private static ImmutableArray<string> MissingChannels(ScenarioDefinition scenario, ImmutableArray<string> matchedChannels)
    {
        var matched = new HashSet<string>(matchedChannels, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. TargetChannelSet(scenario)
                .Where(channel => !matched.Contains(channel))
                .OrderBy(channel => channel, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static HashSet<string> TargetChannelSet(ScenarioDefinition scenario)
    {
        var targets = new HashSet<string>(scenario.Channels, StringComparer.OrdinalIgnoreCase);

        foreach (var channel in scenario.OptionalChannels) { targets.Add(channel); }

        return targets;
    }

    private async Task<ScenarioFolderLaunchResult> OpenMatchedLogsAsync(
        ScenarioDefinition scenario,
        DateFilter? dateWindow,
        FolderMatch match)
    {
        if (match.Paths.Count == 0)
        {
            return match.Unreadable > 0
                ? ScenarioFolderLaunchResult.Error(
                    $"No matching logs were found; {FilesWord(match.Unreadable)} could not be read.", match.Unreadable)
                : new ScenarioFolderLaunchResult
                {
                    Outcome = ScenarioFolderOutcome.NoMatchingLogs,
                    MissingChannels = AllTargetChannels(scenario)
                };
        }

        var priorFilterState = _filterPaneState.Value;

        _dispatcher.Dispatch(new ReplaceFiltersAction(_registry.BuildFilterSet(scenario)));
        _dispatcher.Dispatch(new SetFilterDateRangeAction(dateWindow));

        var result = await _menuActionService.OpenLogFilesAsync(match.Paths, combineLog: false);

        var missing = MissingChannels(scenario, match.MatchedChannels);

        if (result.Opened == 0)
        {
            _dispatcher.Dispatch(new CloseAllLogsAction());
            _dispatcher.Dispatch(new RestoreFilterPaneStateAction(priorFilterState));

            return new ScenarioFolderLaunchResult
            {
                Outcome = ScenarioFolderOutcome.NoLogsOpened,
                Matched = match.Paths.Count,
                Unreadable = match.Unreadable,
                Empty = result.Empty,
                Failed = result.Failed,
                MatchedChannels = match.MatchedChannels,
                MissingChannels = missing
            };
        }

        if (scenario.ActivatesTimeline)
        {
            if (scenario.TimelineDimension is { } timelineDimension)
            {
                _dispatcher.Dispatch(new RequestHistogramDimensionAction(MapTimelineDimension(timelineDimension)));
            }

            if (!_histogramState.Value.IsVisible)
            {
                _menuActionService.SetHistogramVisible(true);
            }
        }

        return new ScenarioFolderLaunchResult
        {
            Outcome = ScenarioFolderOutcome.Completed,
            Matched = match.Paths.Count,
            Unreadable = match.Unreadable,
            Opened = result.Opened,
            Empty = result.Empty,
            Failed = result.Failed,
            MatchedChannels = match.MatchedChannels,
            MissingChannels = missing
        };
    }

    private async Task<FolderMatch> ScanAndMatchAsync(
        ImmutableArray<string> files,
        ScenarioDefinition scenario,
        CancellationToken cancellationToken)
    {
        var probed = new ConcurrentBag<(string Path, EvtxChannelReadResult Result)>();

        await Parallel.ForEachAsync(
            files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = ConcurrencyLimits.MaxBackgroundIoParallelism,
                CancellationToken = cancellationToken
            },
            (file, token) =>
            {
                token.ThrowIfCancellationRequested();
                probed.Add((file, _channelReader.ReadChannel(file)));

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        var targets = TargetChannelSet(scenario);
        var matched = new List<(string Path, string Channel)>();
        var unreadable = 0;

        foreach (var (path, result) in probed)
        {
            if (result.Channel is { } channel)
            {
                if (targets.Contains(channel)) { matched.Add((path, channel)); }
            }
            else if (result.Failed)
            {
                unreadable++;
            }
        }

        var ordered = matched
            .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => Path.GetFileName(entry.Path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        ImmutableArray<string> matchedChannels =
            [.. ordered.Select(entry => entry.Channel).Distinct(StringComparer.OrdinalIgnoreCase)];

        return new FolderMatch([.. ordered.Select(entry => entry.Path)], matchedChannels, unreadable);
    }

    private sealed record FolderMatch(IReadOnlyList<string> Paths, ImmutableArray<string> MatchedChannels, int Unreadable);
}
