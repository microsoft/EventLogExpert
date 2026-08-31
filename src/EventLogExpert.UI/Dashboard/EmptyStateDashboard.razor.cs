// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Writers;
using EventLogExpert.Localization;
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
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using System.Collections.Frozen;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Dashboard;

public sealed partial class EmptyStateDashboard : AppStateComponentBase
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
    private bool _folderScanActive;
    private string? _folderScanLabel;
    private bool _includeSubfolders = true;
    private bool _isBusy;
    private LivePresence _livePresence = new(false, FrozenSet<string>.Empty);
    private bool _openingLogs;
    private bool _pendingCancelFocus;
    private bool _pendingScanEndFocus;
    private bool _pendingTabFocus;
    private IReadOnlyDictionary<string, ChannelReadiness> _readinessByChannel =
        new Dictionary<string, ChannelReadiness>(StringComparer.OrdinalIgnoreCase);
    private SidebarTabs<SplashCategory>? _sidebarTabs;
    private IReadOnlyList<ScenarioDefinition>? _splashScenarios;
    private IReadOnlyList<(SplashCategory Tab, string Label)> _tabs = [];

    [Inject] private IMenuActionService Actions { get; init; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IAnnouncementService Announcer { get; init; } = null!;

    [Inject] private IChannelEnableService ChannelEnable { get; init; } = null!;

    [Inject] private IChannelReadinessService ChannelReadinessService { get; init; } = null!;

    [Inject] private IScenarioFavoriteCommands FavoriteCommands { get; init; } = null!;

    [Inject] private IScenarioFavoritesSource FavoritesSource { get; init; } = null!;

    [Inject] private IFilterAppliedSource FilterAppliedSource { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterCommands { get; init; } = null!;

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

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
        if (disposing && !_disposed)
        {
            _disposed = true;
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
        ObserveSource(FilterAppliedSource);
        ObserveSource(
            handler => FavoritesSource.Changed += handler,
            handler => FavoritesSource.Changed -= handler,
            OnFavoritesChangedAsync);

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

    private static bool HasReactiveFolderFallback(ScenarioLaunchResult result) =>
        !result.ChannelOutcomes.IsDefaultOrEmpty &&
        result.ChannelOutcomes.Any(outcome => outcome.Outcome is ChannelLaunchOutcome.AccessDenied
            or ChannelLaunchOutcome.NotPresent
            or ChannelLaunchOutcome.Failed);

    private bool AccessAllowsLaunch(string channel) =>
        _readinessByChannel.GetValueOrDefault(
            channel,
            new ChannelReadiness(channel, ChannelPresence.Unknown, ChannelEnablement.Unknown))
            .Access is ChannelAccess.Accessible or ChannelAccess.NotEvaluated;

    private void CancelFolderScan()
    {
        if (_openingLogs || _cancelRequested || _folderLaunchCts is null) { return; }

        _cancelRequested = true;
        _pendingCancelFocus = false;
        _pendingTabFocus = false;
        _pendingScanEndFocus = true;
        _folderLaunchCts.Cancel();
        StateHasChanged();
    }

    private void ClearFilter() => FilterCommands.ClearAllFilters();

    private string DescribeEnableFailure(string channel, ChannelEnableResult result) => result.Outcome switch
    {
        ChannelEnableOutcome.AccessDenied => Localizer["Dashboard_EnableFail_AccessDenied", channel],
        ChannelEnableOutcome.NotFound => Localizer["Dashboard_EnableFail_NotFound", channel],
        ChannelEnableOutcome.NotElevated => Localizer["Dashboard_EnableFail_NotElevated", channel],
        _ => Localizer["Dashboard_EnableFail_Unknown", channel, result.Win32Error]
    };

    private string? DescribeFolderLaunch(ScenarioDefinition scenario, ScenarioFolderLaunchResult result)
    {
        var scanNote = result.Unreadable > 0
            ? Localizer["Dashboard_ScanNote", FolderFilesWord(result.Unreadable)].Value
            : string.Empty;

        return result.Outcome switch
        {
            ScenarioFolderOutcome.Cancelled => null,
            ScenarioFolderOutcome.Error => result.Message ?? Localizer["Dashboard_FolderError"].Value,
            ScenarioFolderOutcome.NoMatchingLogs => Localizer["Dashboard_FolderNoMatching", scenario.Name].Value,
            ScenarioFolderOutcome.NoLogsOpened =>
                Localizer["Dashboard_FolderNoneLoaded", FolderLogsWord(result.Matched), scenario.Name, result.Empty, result.Failed].Value +
                scanNote,
            ScenarioFolderOutcome.Completed =>
                Localizer["Dashboard_FolderCompleted", FolderLogsWord(result.Opened), scenario.Name].Value +
                FolderMissingNote(result.MissingChannels) + scanNote,
            _ => null
        };
    }

    private string DescribeLaunch(ScenarioDefinition scenario, ScenarioLaunchResult result)
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
                return Localizer["Dashboard_Launch_CouldNotOpenAll", scenario.Name, string.Join(" ", failedOutcomes)];
            }
        }

        if (result.Opened == 0)
        {
            return Localizer["Dashboard_Launch_NoneOpened", scenario.Name];
        }

        return result.Failed > 0 ?
            Localizer["Dashboard_Launch_OpenedWithUnavailable",
                scenario.Name,
                LocalizedCount.OneOrMany(Localizer, result.Failed, "Dashboard_Channel_One", "Dashboard_Channel_Many")] :
            Localizer["Dashboard_Launch_Opened", scenario.Name];
    }

    private string DescribeLaunchOutcome(ChannelOutcome outcome) => outcome.Outcome switch
    {
        ChannelLaunchOutcome.AccessDenied => Localizer["Dashboard_Outcome_AccessDenied", outcome.Channel],
        ChannelLaunchOutcome.NotPresent => Localizer["Dashboard_Outcome_NotPresent", outcome.Channel],
        ChannelLaunchOutcome.Failed => Localizer["Dashboard_Outcome_Failed", outcome.Channel],
        _ => Localizer["Dashboard_Outcome_Other", outcome.Channel, outcome.Outcome]
    };

    private Task EnableChannelAsync(string channel) =>
        RunGuardedAsync(async () =>
        {
            bool isAnalyticOrDebug = _readinessByChannel.TryGetValue(channel, out var current)
                && current.Access == ChannelAccess.NotEvaluated;

            if (!await EnableChannelConfirmation.ConfirmAsync(AlertDialogService, Localizer, channel, isAnalyticOrDebug))
            {
                return;
            }

            var result = await ChannelEnable.EnableAsync(channel);

            if (result.Outcome is not (ChannelEnableOutcome.Enabled or ChannelEnableOutcome.AlreadyEnabled))
            {
                await AlertDialogService.ShowErrorAlert(Localizer["Dashboard_Alert_EnableLog"], DescribeEnableFailure(channel, result));

                return;
            }

            await RefreshReadinessAsync();

            if (!_readinessByChannel.TryGetValue(channel, out var refreshed)
                || refreshed.Enablement != ChannelEnablement.Enabled)
            {
                await AlertDialogService.ShowAlert(
                    Localizer["Dashboard_Alert_EnableLog"],
                    Localizer["Dashboard_EnabledUnconfirmed", channel],
                    Localizer["Modal_Accept"]);
            }
        });

    private IEnumerable<ScenarioDefinition> FavoriteScenarios() =>
        _splashScenarios is null ? [] : _splashScenarios.Where(IsFavored)
            .OrderBy(scenario => scenario.Priority)
            .ThenBy(scenario => scenario.Order);

    private string FolderFilesWord(int count) => LocalizedCount.OneOrMany(Localizer, count, "Dashboard_File_One", "Dashboard_File_Many");

    private string FolderLogsWord(int count) => LocalizedCount.OneOrMany(Localizer, count, "Dashboard_Log_One", "Dashboard_Log_Many");

    private string FolderMissingNote(ImmutableArray<string> missing) =>
        missing.IsDefaultOrEmpty ? string.Empty : Localizer["Dashboard_MissingNote", string.Join(", ", missing)].Value;

    private string FolderScanCancelLabel() =>
        _folderScanLabel is { } label ?
            Localizer["Dashboard_ScanCancel_Labeled", label].Value :
            Localizer["Dashboard_ScanCancel"].Value;

    private string FolderScanStatusText()
    {
        if (_openingLogs)
        {
            return _folderScanLabel is { } openingLabel ?
                Localizer["Dashboard_ScanStatus_OpeningLabeled", openingLabel] :
                Localizer["Dashboard_ScanStatus_Opening"];
        }

        if (_cancelRequested)
        {
            return _folderScanLabel is { } cancellingLabel ?
                Localizer["Dashboard_ScanStatus_CancellingLabeled", cancellingLabel] :
                Localizer["Dashboard_ScanStatus_Cancelling"];
        }

        return _folderScanLabel is { } scanningLabel ?
            Localizer["Dashboard_ScanStatus_ScanningLabeled", scanningLabel] :
            Localizer["Dashboard_ScanStatus_Scanning"];
    }

    private IReadOnlyList<ChannelReadiness> GetChannelReadiness(ScenarioDefinition scenario) =>
        ReadinessFor(scenario.Channels);

    private IReadOnlyList<ChannelReadiness> GetOptionalChannelReadiness(ScenarioDefinition scenario) =>
        scenario.OptionalChannels.IsDefaultOrEmpty ? [] : ReadinessFor(scenario.OptionalChannels);

    private bool IsFavored(ScenarioDefinition scenario) =>
        FavoritesSource.FavoriteScenarioIds.Contains(scenario.Id);

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
                    Localizer["Dashboard_Alert_LaunchScenario"],
                    message,
                    Localizer["Dashboard_OpenFromFolder"],
                    () => LaunchScenarioFromFolderAsync(scenario, includeSubfolders: true));
            }
            else
            {
                Announcer.Announce(message);
            }
        });

    private Task LaunchScenarioFromFolderAsync(ScenarioDefinition scenario, bool includeSubfolders = false) =>
        RunGuardedAsync(() => LaunchScenarioFromFolderCoreAsync(scenario, includeSubfolders));

    private async Task LaunchScenarioFromFolderCoreAsync(ScenarioDefinition scenario, bool includeSubfolders)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _folderLaunchCts = cts;
        var scanStarted = false;
        ScenarioFolderLaunchResult result;

        try
        {
            result = await ScenarioLaunch.LaunchFromFolderAsync(scenario, null, includeSubfolders, cts.Token, OnFolderScanPhaseAsync);
        }
        finally
        {
            if (ReferenceEquals(_folderLaunchCts, cts)) { _folderLaunchCts = null; }

            cts.Dispose();

            if (_folderScanActive)
            {
                _folderScanActive = false;
                _folderScanLabel = null;
                _openingLogs = false;
                _cancelRequested = false;
                _pendingCancelFocus = false;
                _pendingScanEndFocus = false;

                await SafeInvokeAsync(StateHasChanged);
            }
        }

        if (DescribeFolderLaunch(scenario, result) is not { } message)
        {
            if (scanStarted) { RestoreFocusAfterScan(); }

            return;
        }

        switch (result.Outcome)
        {
            case ScenarioFolderOutcome.Completed:
                Announcer.Announce(message);
                break;
            case ScenarioFolderOutcome.Error:
                if (scanStarted) { RestoreFocusAfterScan(); }

                await AlertDialogService.ShowErrorAlert(Localizer["Dashboard_Alert_OpenFromFolder"], message);
                break;
            default:
                await AlertDialogService.ShowAlert(Localizer["Dashboard_Alert_OpenFromFolder"], message, Localizer["Modal_Accept"]);
                break;
        }

        async Task OnFolderScanPhaseAsync(ScenarioFolderPhase phase) =>
            await SafeInvokeAsync(() =>
            {
                switch (phase)
                {
                    case ScenarioFolderPhase.Scanning:
                        scanStarted = true;
                        _folderScanActive = true;
                        _folderScanLabel = scenario.Name;
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

    private Task OnFavoritesChangedAsync()
    {
        if (_disposed) { return Task.CompletedTask; }

        RebuildCategories();
        ReconcileActiveTab();

        if (_disposed) { return Task.CompletedTask; }

        StateHasChanged();

        return Task.CompletedTask;
    }

    private Task OpenApplicationAndSystemAsync() =>
        RunGuardedAsync(() => Actions.OpenLiveLogsAsync([LogChannelNames.ApplicationLog, LogChannelNames.SystemLog], false));

    private Task OpenApplicationAsync() => RunGuardedAsync(() => Actions.OpenLiveLogAsync(LogChannelNames.ApplicationLog, false));

    private Task OpenDatabaseToolsAsync() => RunGuardedAsync(() => Actions.OpenDatabaseToolsAsync());

    private Task OpenFileAsync() => RunGuardedAsync(() => Actions.OpenFileAsync(false));

    private Task OpenFolderAsync() => RunGuardedAsync(() => OpenFolderCoreAsync(_includeSubfolders));

    private async Task OpenFolderCoreAsync(bool includeSubfolders)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _folderLaunchCts = cts;
        var scanStarted = false;

        try
        {
            await Actions.OpenFolderAsync(false, includeSubfolders, cts.Token, OnFolderOpenPhaseAsync);
        }
        finally
        {
            if (ReferenceEquals(_folderLaunchCts, cts)) { _folderLaunchCts = null; }

            cts.Dispose();

            if (_folderScanActive)
            {
                _folderScanActive = false;
                _folderScanLabel = null;
                _openingLogs = false;
                _cancelRequested = false;
                _pendingCancelFocus = false;
                _pendingScanEndFocus = false;

                await SafeInvokeAsync(StateHasChanged);
            }
        }

        if (scanStarted) { RestoreFocusAfterScan(); }

        async Task OnFolderOpenPhaseAsync(FolderOpenPhase phase) =>
            await SafeInvokeAsync(() =>
            {
                switch (phase)
                {
                    case FolderOpenPhase.Scanning:
                        scanStarted = true;
                        _folderScanActive = true;
                        _folderScanLabel = null;
                        _openingLogs = false;
                        _cancelRequested = false;
                        _pendingTabFocus = false;
                        _pendingScanEndFocus = false;
                        _pendingCancelFocus = true;
                        break;
                    case FolderOpenPhase.Opening:
                        _openingLogs = true;
                        _pendingCancelFocus = false;
                        _pendingTabFocus = false;
                        _pendingScanEndFocus = true;
                        break;
                }

                StateHasChanged();
            });
    }

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

            if (favorites.Count > 0) { categories.Add((SplashCategory.Favorites, Localizer["Dashboard_Category_Favorites"].Value, favorites)); }

            List<ScenarioDefinition> recommended = [.. StarterScenarios()];

            if (recommended.Count > 0) { categories.Add((SplashCategory.Recommended, Localizer["Dashboard_Category_Recommended"].Value, recommended)); }

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

                categories.Add((category, ScenarioGroupLocalizer.GroupDisplay(Localizer, group), groupScenarios));
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
            await SafeInvokeAsync(StateHasChanged);
            await action();
        }
        finally
        {
            _isBusy = false;

            await SafeInvokeAsync(StateHasChanged);
        }
    }

    private async Task SafeInvokeAsync(Action render)
    {
        if (_disposed) { return; }

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
