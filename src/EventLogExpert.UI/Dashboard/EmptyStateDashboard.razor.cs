// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Scenarios.Favorites;
using EventLogExpert.Scenarios.Catalog;
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
    private List<(SplashCategory Category, string Label, IReadOnlyList<ScenarioDefinition> Scenarios)> _categories = [];
    private bool _isBusy;
    private LivePresence _livePresence = new(false, FrozenSet<string>.Empty);
    private bool _pendingTabFocus;
    private IReadOnlyDictionary<string, ChannelReadiness> _readinessByChannel =
        new Dictionary<string, ChannelReadiness>(StringComparer.OrdinalIgnoreCase);
    private SidebarTabs<SplashCategory>? _sidebarTabs;
    private IReadOnlyList<ScenarioDefinition>? _splashScenarios;
    private IReadOnlyList<(SplashCategory Tab, string Label)> _tabs = [];

    [Inject] private IMenuActionService Actions { get; init; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IAnnouncementService Announcer { get; init; } = null!;

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
            Favorites.SelectedValueChanged -= OnFavoritesChanged;
            await _lifetimeCts.CancelAsync();
            _lifetimeCts.Dispose();
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_pendingTabFocus && _sidebarTabs is not null)
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

        var readiness = await ChannelReadinessService.GetReadinessAsync(CatalogChannels(_splashScenarios));
        _readinessByChannel = readiness.ToDictionary(channel => channel.Channel, StringComparer.OrdinalIgnoreCase);
        _livePresence = LivePresence.FromReadiness(readiness);
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

    private void ClearFilter() => FilterCommands.ClearAllFilters();

    private IEnumerable<ScenarioDefinition> FavoriteScenarios() =>
        _splashScenarios is null
            ? []
            : _splashScenarios
                .Where(IsFavored)
                .OrderBy(scenario => scenario.Priority)
                .ThenBy(scenario => scenario.Order);

    private IReadOnlyList<ChannelReadiness> GetChannelReadiness(ScenarioDefinition scenario) =>
    [
        .. scenario.Channels.Select(channel =>
            _readinessByChannel.GetValueOrDefault(
                channel,
                new ChannelReadiness(channel, ChannelPresence.Unknown, ChannelEnablement.Unknown)))
    ];

    private bool IsFavored(ScenarioDefinition scenario) =>
        ScenarioFavorites.Value.FavoriteScenarioIds.Contains(scenario.Id);

    private bool IsLivePresent(ScenarioDefinition scenario) =>
        !_livePresence.Known || scenario.Channels.All(_livePresence.Present.Contains);

    private bool IsScenarioDisabled(ScenarioDefinition scenario) =>
        _livePresence.Known &&
        !scenario.Channels.All(channel => _livePresence.Present.Contains(channel) && AccessAllowsLaunch(channel));

    // Analytic/Debug channels never have their access evaluated (NotEvaluated), so they must not be
    // treated as blocked; only a genuine RequiresElevation or a read-failure Unknown disables launch.
    private bool AccessAllowsLaunch(string channel) =>
        _readinessByChannel.GetValueOrDefault(
            channel,
            new ChannelReadiness(channel, ChannelPresence.Unknown, ChannelEnablement.Unknown))
            .Access is ChannelAccess.Accessible or ChannelAccess.NotEvaluated;

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
                    () => LaunchScenarioFromFolderCoreAsync(scenario));
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
        var result = await ScenarioLaunch.LaunchFromFolderAsync(scenario, null, _lifetimeCts.Token);

        if (DescribeFolderLaunch(scenario, result) is not { } message) { return; }

        // A launch that opens logs is self-evident (the workspace changes) and only needs the screen-reader detail;
        // the outcomes that leave the dashboard unchanged need a visible dialog so a sighted user sees them.
        switch (result.Outcome)
        {
            case ScenarioFolderOutcome.Completed:
                Announcer.Announce(message);
                break;
            case ScenarioFolderOutcome.Error:
                await AlertDialogService.ShowErrorAlert("Open from folder", message);
                break;
            default:
                await AlertDialogService.ShowAlert("Open from folder", message, "OK");
                break;
        }
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

    private async Task RunGuardedAsync(Func<Task> action)
    {
        if (_isBusy) { return; }

        _isBusy = true;

        try { await action(); }
        finally { _isBusy = false; }
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
