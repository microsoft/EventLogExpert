// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Writers;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Scenarios.Favorites;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Focus;
using EventLogExpert.UI.Inputs;
using EventLogExpert.UI.Modal;
using Fluxor;
using Fluxor.Blazor.Web.Components;
using Microsoft.AspNetCore.Components;
using System.Collections.Frozen;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Dashboard;

public sealed partial class EmptyStateDashboard : FluxorComponent
{
    private const string ElevationReasonId = "empty-dashboard-elevation-reason";

    internal static readonly ImmutableArray<string> StarterScenarioIds =
    [
        "recent-critical-and-error-events",
        "unexpected-restart-power-loss-bsod",
        "failed-services-at-boot",
        "disk-io-errors-bad-blocks",
        "application-crashes",
        "application-hangs",
        "dotnet-unhandled-exceptions",
        "windows-update-diagnostics",
        "group-policy-processing-errors"
    ];

    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Dictionary<SplashCategory, ScenarioDefinition?> _selectedByCategory = new();

    private SplashCategory _activeCategory;
    private bool _cancelRequested;
    private Button? _cancelScanButton;
    private List<(SplashCategory Category, string Label, IReadOnlyList<ScenarioDefinition> Scenarios)> _categories = [];
    private ElementReference _dashboardRoot;
    private bool _disposed;
    private CancellationTokenSource? _folderLaunchCts;
    private bool _isBusy;
    private LivePresence _livePresence = new(false, FrozenSet<string>.Empty);
    private bool _openingLogs;
    private bool _pendingCancelFocus;
    private bool _pendingScanEndFocus;
    private bool _pendingTabFocus;
    private IReadOnlyDictionary<string, ChannelReadiness> _readinessByChannel =
        new Dictionary<string, ChannelReadiness>(StringComparer.OrdinalIgnoreCase);
    private ScenarioDefinition? _scanningScenario;
    private SidebarTabs<SplashCategory>? _sidebarTabs;
    private IReadOnlyList<ScenarioDefinition>? _splashScenarios;
    private IReadOnlyList<(SplashCategory Tab, string Label)> _tabs = [];

    [Inject] private IMenuActionService Actions { get; init; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IAnnouncementService Announcer { get; init; } = null!;

    [Inject] private IChannelEnableService ChannelEnable { get; init; } = null!;

    [Inject] private IChannelReadinessService ChannelReadinessService { get; init; } = null!;

    [Inject] private IScenarioFavoriteCommands FavoriteCommands { get; init; } = null!;

    [Inject] private IStateSelection<ScenarioFavoritesState, ImmutableHashSet<string>> Favorites { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, bool> FilterApplied { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterCommands { get; init; } = null!;

    [Inject] private IState<ScenarioFavoritesState> ScenarioFavorites { get; init; } = null!;

    [Inject] private IScenarioLaunchService ScenarioLaunch { get; init; } = null!;

    [Inject] private IScenarioQueryService ScenarioQuery { get; init; } = null!;

    private bool SecurityRequiresElevation =>
        _readinessByChannel.GetValueOrDefault(
            LogChannelNames.SecurityLog,
            new ChannelReadiness(LogChannelNames.SecurityLog, ChannelPresence.Unknown, ChannelEnablement.Unknown))
            .Access == ChannelAccess.RequiresElevation;

    internal static string ScenarioIcon(ScenarioGroup group) => group switch
    {
        ScenarioGroup.SystemHealth => "bi-heart-pulse",
        ScenarioGroup.Applications => "bi-window-stack",
        ScenarioGroup.Security => "bi-shield-lock",
        ScenarioGroup.ThreatsAndIncidentResponse => "bi-shield-exclamation",
        ScenarioGroup.Network => "bi-diagram-3",
        ScenarioGroup.Storage => "bi-hdd-stack",
        ScenarioGroup.UpdatesAndPolicy => "bi-arrow-repeat",
        ScenarioGroup.ActiveDirectory => "bi-diagram-2",
        ScenarioGroup.DnsServer => "bi-signpost-split",
        ScenarioGroup.DhcpServer => "bi-ethernet",
        ScenarioGroup.NpsAndRras => "bi-shield-check",
        ScenarioGroup.Wins => "bi-hdd-network",
        ScenarioGroup.WebAndIis => "bi-globe2",
        ScenarioGroup.VirtualizationAndClustering => "bi-hdd-rack",
        ScenarioGroup.FilePrintAndStorage => "bi-printer",
        ScenarioGroup.SqlServer => "bi-database",
        ScenarioGroup.Exchange => "bi-envelope",
        ScenarioGroup.SharePoint => "bi-folder-symlink",
        ScenarioGroup.DefenderForEndpoint => "bi-shield-shaded",
        ScenarioGroup.Office => "bi-file-earmark-richtext",
        _ => "bi-search"
    };

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            Favorites.SelectedValueChanged -= OnFavoritesChanged;
            await _lifetimeCts.CancelAsync();
            _lifetimeCts.Dispose();
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingCancelFocus)
        {
            _pendingCancelFocus = false;

            if (_cancelScanButton is not null) { await ElementFocus.SafelyAsync(_cancelScanButton.Element); }
        }
        else if (_pendingScanEndFocus)
        {
            _pendingScanEndFocus = false;

            var focused = _sidebarTabs is not null && await _sidebarTabs.FocusActiveTabAsync();

            if (!focused) { await ElementFocus.SafelyAsync(_dashboardRoot); }
        }
        else if (_pendingTabFocus && _sidebarTabs is not null)
        {
            _pendingTabFocus = false;

            await _sidebarTabs.FocusActiveTabAsync();
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    protected override void OnInitialized()
    {
        FilterApplied.Select(state => state.AppliedFilter.IsFilteringEnabled);
        Favorites.Select(state => state.FavoriteScenarioIds);
        Favorites.SelectedValueChanged += OnFavoritesChanged;
        FavoriteCommands.Load();
        base.OnInitialized();
    }

    protected override async Task OnInitializedAsync()
    {
        _splashScenarios = await Task.Run(ScenarioQuery.GetSplashScenarios);

        await RefreshReadinessAsync();
        RebuildCategories();

        if (_categories.Count > 0 && !_categories.Any(category => category.Category.Equals(_activeCategory)))
        {
            _activeCategory = _categories[0].Category;
        }

        await base.OnInitializedAsync();
    }

    private static IEnumerable<string> CatalogChannels(IReadOnlyList<ScenarioDefinition>? scenarios) =>
        scenarios is null
            ? []
            : scenarios
                .SelectMany(static scenario => scenario.Channels.Concat(scenario.OptionalChannels))
                .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string DescribeEnableFailure(string channel, ChannelEnableResult result) => result.Outcome switch
    {
        ChannelEnableOutcome.AccessDenied =>
            $"Enabling \"{channel}\" was denied. Administrator rights are required, and the log's security settings must allow the change.",
        ChannelEnableOutcome.NotFound => $"The \"{channel}\" log is not registered on this computer.",
        ChannelEnableOutcome.NotElevated => $"Run EventLogExpert as administrator to enable \"{channel}\".",
        _ => $"The \"{channel}\" log could not be enabled (error {result.Win32Error})."
    };

    private static string? DescribeFolderLaunch(ScenarioDefinition scenario, ScenarioFolderLaunchResult result)
    {
        var scanNote = result.Unreadable > 0 ? $" {FolderFilesWord(result.Unreadable)} could not be inspected." : string.Empty;

        return result.Outcome switch
        {
            ScenarioFolderOutcome.Cancelled => null,
            ScenarioFolderOutcome.Error => result.Message ?? "The selected folder could not be opened.",
            ScenarioFolderOutcome.NoMatchingLogs => $"No {scenario.Name} logs were found in the selected folder.",
            ScenarioFolderOutcome.NoLogsOpened =>
                $"Matched {FolderLogsWord(result.Matched)} for {scenario.Name} but none could be loaded " +
                $"({result.Empty} empty, {result.Failed} failed).{scanNote}",
            ScenarioFolderOutcome.Completed =>
                $"Opened {FolderLogsWord(result.Opened)} for {scenario.Name}.{FolderMissingNote(result.MissingChannels)}{scanNote}",
            _ => null
        };
    }

    private static string DescribeLaunch(ScenarioDefinition scenario, ScenarioLaunchResult result)
    {
        if (!result.ChannelOutcomes.IsDefaultOrEmpty)
        {
            var failedOutcomes = result.ChannelOutcomes
                .Where(outcome => outcome.Outcome is ChannelLaunchOutcome.AccessDenied
                    or ChannelLaunchOutcome.NotPresent
                    or ChannelLaunchOutcome.Failed)
                .Select(DescribeLaunchOutcome)
                .ToList();

            if (failedOutcomes.Count > 0)
            {
                return $"{scenario.Name} could not open every required channel. {string.Join(" ", failedOutcomes)}";
            }
        }

        if (result.Opened == 0)
        {
            return $"No channels could be opened for {scenario.Name}.";
        }

        return result.Failed > 0
            ? $"Opened {scenario.Name}; {result.Failed} {(result.Failed == 1 ? "channel" : "channels")} unavailable."
            : $"Opened {scenario.Name}.";
    }

    private static string DescribeLaunchOutcome(ChannelOutcome outcome) => outcome.Outcome switch
    {
        ChannelLaunchOutcome.AccessDenied =>
            $"{outcome.Channel}: access denied - needs elevation, or open from a saved .evtx.",
        ChannelLaunchOutcome.NotPresent =>
            $"{outcome.Channel}: not present on this computer - open from a saved .evtx.",
        ChannelLaunchOutcome.Failed =>
            $"{outcome.Channel}: failed to open - open from a saved .evtx.",
        _ => $"{outcome.Channel}: {outcome.Outcome}."
    };

    private static string FolderFilesWord(int count) => count == 1 ? "1 file" : $"{count} files";

    private static string FolderLogsWord(int count) => count == 1 ? "1 log" : $"{count} logs";

    private static string FolderMissingNote(ImmutableArray<string> missing) =>
        missing.IsDefaultOrEmpty ? string.Empty : $" Not found: {string.Join(", ", missing)}.";

    private static bool HasReactiveFolderFallback(ScenarioLaunchResult result) =>
        !result.ChannelOutcomes.IsDefaultOrEmpty &&
        result.ChannelOutcomes.Any(outcome => outcome.Outcome is ChannelLaunchOutcome.AccessDenied
            or ChannelLaunchOutcome.NotPresent
            or ChannelLaunchOutcome.Failed);

    // Analytic/Debug channels never have their access evaluated (NotEvaluated), so they must not be
    // treated as blocked; only a genuine RequiresElevation or a read-failure Unknown disables launch.
    private bool AccessAllowsLaunch(string channel) =>
        _readinessByChannel.GetValueOrDefault(
            channel,
            new ChannelReadiness(channel, ChannelPresence.Unknown, ChannelEnablement.Unknown))
            .Access is ChannelAccess.Accessible or ChannelAccess.NotEvaluated;

    private void CancelFolderScan()
    {
        // No-op once the scan has committed to opening or a cancel is already in flight. The null-field guard plus the
        // null-before-Dispose ordering in the finally means Cancel() is never called on a disposed source.
        if (_openingLogs || _cancelRequested || _folderLaunchCts is null) { return; }

        _cancelRequested = true;
        _pendingCancelFocus = false;
        _pendingTabFocus = false;
        _pendingScanEndFocus = true;
        _folderLaunchCts.Cancel();
        StateHasChanged();
    }

    private void ClearFilter() => FilterCommands.ClearAllFilters();

    private Task EnableChannelAsync(string channel) =>
        RunGuardedAsync(async () =>
        {
            bool isAnalyticOrDebug = _readinessByChannel.TryGetValue(channel, out var current)
                && current.Access == ChannelAccess.NotEvaluated;

            if (!await EnableChannelConfirmation.ConfirmAsync(AlertDialogService, channel, isAnalyticOrDebug))
            {
                return;
            }

            var result = await ChannelEnable.EnableAsync(channel);

            if (result.Outcome is not (ChannelEnableOutcome.Enabled or ChannelEnableOutcome.AlreadyEnabled))
            {
                await AlertDialogService.ShowErrorAlert("Enable log", DescribeEnableFailure(channel, result));

                return;
            }

            // The committed change is authoritative; re-run the full readiness fetch (never a single-channel probe,
            // which would make LivePresence treat one channel as the complete set) so the pill reflects real state.
            await RefreshReadinessAsync();

            if (!_readinessByChannel.TryGetValue(channel, out var refreshed)
                || refreshed.Enablement != ChannelEnablement.Enabled)
            {
                await AlertDialogService.ShowAlert(
                    "Enable log",
                    $"\"{channel}\" was enabled, but its status could not be confirmed. Refresh to re-check.",
                    "OK");
            }
        });

    private IEnumerable<ScenarioDefinition> FavoriteScenarios() =>
        _splashScenarios is null
            ? []
            : _splashScenarios
                .Where(IsFavored)
                .OrderBy(scenario => scenario.Priority)
                .ThenBy(scenario => scenario.Order);

    private IReadOnlyList<ChannelReadiness> GetChannelReadiness(ScenarioDefinition scenario) =>
        ReadinessFor(scenario.Channels);

    private IReadOnlyList<ChannelReadiness> GetOptionalChannelReadiness(ScenarioDefinition scenario) =>
        scenario.OptionalChannels.IsDefaultOrEmpty ? [] : ReadinessFor(scenario.OptionalChannels);

    private bool IsFavored(ScenarioDefinition scenario) =>
        ScenarioFavorites.Value.FavoriteScenarioIds.Contains(scenario.Id);

    private bool IsLivePresent(ScenarioDefinition scenario) =>
        !_livePresence.Known || scenario.Channels.All(_livePresence.Present.Contains);

    private bool IsScenarioDisabled(ScenarioDefinition scenario) =>
        _livePresence.Known &&
        !scenario.Channels.All(channel => _livePresence.Present.Contains(channel) && AccessAllowsLaunch(channel));

    private Task LaunchScenarioAsync(ScenarioDefinition scenario) =>
        RunGuardedAsync(async () =>
        {
            var result = await ScenarioLaunch.LaunchAsync(scenario, null);
            var message = DescribeLaunch(scenario, result);

            if (HasReactiveFolderFallback(result))
            {
                await AlertDialogService.ShowErrorAlert(
                    "Launch scenario",
                    message,
                    "Open from folder",
                    () => LaunchScenarioFromFolderAsync(scenario));
            }
            else
            {
                Announcer.Announce(message);
            }
        });

    private Task LaunchScenarioFromFolderAsync(ScenarioDefinition scenario) =>
        RunGuardedAsync(() => LaunchScenarioFromFolderCoreAsync(scenario));

    private async Task LaunchScenarioFromFolderCoreAsync(ScenarioDefinition scenario)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _folderLaunchCts = cts;
        var scanStarted = false;
        ScenarioFolderLaunchResult result;

        try
        {
            result = await ScenarioLaunch.LaunchFromFolderAsync(scenario, null, cts.Token, OnFolderScanPhaseAsync);
        }
        finally
        {
            if (ReferenceEquals(_folderLaunchCts, cts)) { _folderLaunchCts = null; }

            cts.Dispose();

            if (_scanningScenario is not null)
            {
                // Hide the scan chip before any result dialog. The banner-retry path does not run in the dashboard's
                // event loop, so it will not auto-render; the explicit render is required there. Clear the scan-end
                // focus intent too, so an Opening-set request cannot steal focus from a result modal opened below.
                _scanningScenario = null;
                _openingLogs = false;
                _cancelRequested = false;
                _pendingCancelFocus = false;
                _pendingScanEndFocus = false;

                await SafeInvokeAsync(StateHasChanged);
            }
        }

        if (DescribeFolderLaunch(scenario, result) is not { } message)
        {
            // Cancelled: no dialog takes focus and the Cancel chip that had focus is gone, so restore it.
            if (scanStarted) { RestoreFocusAfterScan(); }

            return;
        }

        // A launch that opens logs is self-evident (the workspace changes) and only needs the screen-reader detail;
        // the outcomes that leave the dashboard unchanged need a visible dialog so a sighted user sees them.
        switch (result.Outcome)
        {
            case ScenarioFolderOutcome.Completed:
                Announcer.Announce(message);
                break;
            case ScenarioFolderOutcome.Error:
                // ShowErrorAlert surfaces a banner, which does not capture focus; restore it so a scan that showed the
                // Cancel chip does not strand keyboard focus on <body>.
                if (scanStarted) { RestoreFocusAfterScan(); }

                await AlertDialogService.ShowErrorAlert("Open from folder", message);
                break;
            default:
                // ShowAlert opens a focus-capturing modal, so it owns focus; issuing a restore here would fight it.
                await AlertDialogService.ShowAlert("Open from folder", message, "OK");
                break;
        }

        // Runs on the UI dispatcher via SafeInvokeAsync and must never throw out of onPhase: it executes outside the
        // service's cancellation catch, so an escaping teardown exception would surface a normal launch as a fault.
        async Task OnFolderScanPhaseAsync(ScenarioFolderPhase phase) =>
            await SafeInvokeAsync(() =>
            {
                switch (phase)
                {
                    case ScenarioFolderPhase.Scanning:
                        scanStarted = true;
                        _scanningScenario = scenario;
                        _openingLogs = false;
                        _cancelRequested = false;
                        _pendingTabFocus = false;
                        _pendingScanEndFocus = false;
                        _pendingCancelFocus = true;
                        break;
                    case ScenarioFolderPhase.Opening:
                        _openingLogs = true;
                        _pendingCancelFocus = false;
                        _pendingTabFocus = false;
                        _pendingScanEndFocus = true;
                        break;
                }

                StateHasChanged();
            });
    }

    private async void OnFavoritesChanged(object? _, ImmutableHashSet<string> __)
    {
        try
        {
            await InvokeAsync(() =>
            {
                RebuildCategories();
                ReconcileActiveTab();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
    }

    private Task OpenApplicationAndSystemAsync() =>
        RunGuardedAsync(() => Actions.OpenLiveLogsAsync([LogChannelNames.ApplicationLog, LogChannelNames.SystemLog], false));

    private Task OpenApplicationAsync() => RunGuardedAsync(() => Actions.OpenLiveLogAsync(LogChannelNames.ApplicationLog, false));

    private Task OpenDatabaseToolsAsync() => RunGuardedAsync(() => Actions.OpenDatabaseToolsAsync());

    private Task OpenFileAsync() => RunGuardedAsync(() => Actions.OpenFileAsync(false));

    private Task OpenFolderAsync() => RunGuardedAsync(() => Actions.OpenFolderAsync(false));

    private Task OpenSecurityAsync() => RunGuardedAsync(() => Actions.OpenLiveLogAsync(LogChannelNames.SecurityLog, false));

    private Task OpenSystemAsync() => RunGuardedAsync(() => Actions.OpenLiveLogAsync(LogChannelNames.SystemLog, false));

    private IReadOnlyList<ChannelReadiness> ReadinessFor(IEnumerable<string> channels) =>
    [
        .. channels.Select(channel =>
            _readinessByChannel.GetValueOrDefault(
                channel,
                new ChannelReadiness(channel, ChannelPresence.Unknown, ChannelEnablement.Unknown)))
    ];

    private void RebuildCategories()
    {
        List<(SplashCategory Category, string Label, IReadOnlyList<ScenarioDefinition> Scenarios)> categories = [];

        if (_splashScenarios is not null)
        {
            List<ScenarioDefinition> favorites = [.. FavoriteScenarios()];

            if (favorites.Count > 0) { categories.Add((SplashCategory.Favorites, "Favorites", favorites)); }

            List<ScenarioDefinition> recommended = [.. StarterScenarios()];

            if (recommended.Count > 0) { categories.Add((SplashCategory.Recommended, "Recommended", recommended)); }

            foreach (ScenarioGroup group in Enum.GetValues<ScenarioGroup>())
            {
                if (SplashCategoryMapping.ToSplashCategory(group) is not { } category) { continue; }

                List<ScenarioDefinition> groupScenarios =
                [
                    .. _splashScenarios
                        .Where(scenario => scenario.Group == group)
                        .OrderBy(scenario => scenario.Priority)
                        .ThenBy(scenario => scenario.Order)
                ];

                if (groupScenarios.Count == 0) { continue; }

                categories.Add((category, group.DisplayName(), groupScenarios));
            }
        }

        _categories = categories;
        _tabs = [.. categories.Select(category => (category.Category, category.Label))];

        HashSet<SplashCategory> present = [.. categories.Select(category => category.Category)];

        foreach (SplashCategory stale in _selectedByCategory.Keys.Where(key => !present.Contains(key)).ToList())
        {
            _selectedByCategory.Remove(stale);
        }

        foreach ((SplashCategory category, _, IReadOnlyList<ScenarioDefinition> scenarios) in categories)
        {
            ScenarioDefinition? current = _selectedByCategory.GetValueOrDefault(category);

            bool stillPresent = current is not null &&
                scenarios.Any(scenario => string.Equals(scenario.Id, current.Id, StringComparison.Ordinal));

            if (!stillPresent)
            {
                _selectedByCategory[category] = scenarios.Count > 0 ? scenarios[0] : null;
            }
        }
    }

    private void ReconcileActiveTab()
    {
        if (_categories.Count == 0) { return; }

        if (_categories.Any(category => category.Category.Equals(_activeCategory))) { return; }

        _activeCategory = _categories[0].Category;
        _pendingTabFocus = true;
    }

    private async Task RefreshReadinessAsync()
    {
        var readiness = await ChannelReadinessService.GetReadinessAsync(CatalogChannels(_splashScenarios));
        _readinessByChannel = readiness.ToDictionary(channel => channel.Channel, StringComparer.OrdinalIgnoreCase);
        _livePresence = LivePresence.FromReadiness(readiness);
    }

    private void RestoreFocusAfterScan()
    {
        if (_disposed) { return; }

        _pendingCancelFocus = false;
        _pendingTabFocus = false;
        _pendingScanEndFocus = true;
        StateHasChanged();
    }

    private async Task RunGuardedAsync(Func<Task> action)
    {
        if (_isBusy) { return; }

        _isBusy = true;

        try
        {
            // Render immediately (inside the try, so the finally still clears _isBusy if this ever threw) so the
            // busy-gated controls disable right away. On a normal button click Blazor auto-renders at the first await
            // inside the action, but the banner-retry path runs outside the dashboard's event loop and gets no
            // automatic render, so without this the controls would stay visibly enabled during the folder picker.
            await SafeInvokeAsync(StateHasChanged);
            await action();
        }
        finally
        {
            _isBusy = false;

            // Re-render after clearing the busy flag for the same non-event-loop reason, so the controls re-enable.
            await SafeInvokeAsync(StateHasChanged);
        }
    }

    private async Task SafeInvokeAsync(Action render)
    {
        if (_disposed) { return; }

        // Matches the OnFavoritesChanged teardown pattern: a render can race the dashboard unmounting when a folder
        // launch opens logs; treat disposal and cancellation as expected.
        try { await InvokeAsync(render); }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
    }

    private IReadOnlyList<ScenarioDefinition> ScenariosFor(SplashCategory category)
    {
        foreach ((SplashCategory current, _, IReadOnlyList<ScenarioDefinition> scenarios) in _categories)
        {
            if (current.Equals(category)) { return scenarios; }
        }

        return [];
    }

    private void Select(SplashCategory category, ScenarioDefinition scenario) =>
        _selectedByCategory[category] = scenario;

    private IEnumerable<ScenarioDefinition> StarterScenarios()
    {
        if (_splashScenarios is null) { yield break; }

        foreach (var id in StarterScenarioIds)
        {
            var match = _splashScenarios.FirstOrDefault(scenario => scenario.Id == id);

            if (match is not null) { yield return match; }
        }
    }

    private void ToggleFavorite(ScenarioDefinition scenario) =>
        FavoriteCommands.SetFavorite(scenario.Id, scenario.Name, !IsFavored(scenario));
}
