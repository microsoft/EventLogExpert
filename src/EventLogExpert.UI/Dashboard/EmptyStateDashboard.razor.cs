// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Versioning;
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

    private readonly Dictionary<SplashCategory, ScenarioDefinition?> _selectedByCategory = new();

    private SplashCategory _activeCategory;
    private List<(SplashCategory Category, string Label, IReadOnlyList<ScenarioDefinition> Scenarios)> _categories = [];
    private bool _isBusy;
    private bool _pendingTabFocus;
    private SidebarTabs<SplashCategory>? _sidebarTabs;
    private IReadOnlyList<ScenarioDefinition>? _splashScenarios;
    private IReadOnlyList<(SplashCategory Tab, string Label)> _tabs = [];

    [Inject] private IMenuActionService Actions { get; init; } = null!;

    [Inject] private IAnnouncementService Announcer { get; init; } = null!;

    [Inject] private IScenarioFavoriteCommands FavoriteCommands { get; init; } = null!;

    [Inject] private IStateSelection<ScenarioFavoritesState, ImmutableHashSet<string>> Favorites { get; init; } = null!;

    [Inject] private IStateSelection<EventLogState, bool> FilterApplied { get; init; } = null!;

    [Inject] private IFilterPaneCommands FilterCommands { get; init; } = null!;

    [Inject] private IState<ScenarioFavoritesState> ScenarioFavorites { get; init; } = null!;

    [Inject] private IScenarioLaunchService ScenarioLaunch { get; init; } = null!;

    [Inject] private IScenarioQueryService ScenarioQuery { get; init; } = null!;

    [Inject] private ICurrentVersionProvider Version { get; init; } = null!;

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
        RebuildCategories();

        if (_categories.Count > 0 && !_categories.Any(category => category.Category.Equals(_activeCategory)))
        {
            _activeCategory = _categories[0].Category;
        }

        await base.OnInitializedAsync();
    }

    private static string DescribeLaunch(ScenarioDefinition scenario, ScenarioLaunchResult result)
    {
        if (result.Opened == 0)
        {
            return $"No channels could be opened for {scenario.Name}.";
        }

        return result.Failed > 0
            ? $"Opened {scenario.Name}; {result.Failed} {(result.Failed == 1 ? "channel" : "channels")} unavailable."
            : $"Opened {scenario.Name}.";
    }

    private void ClearFilter() => FilterCommands.ClearAllFilters();

    private IEnumerable<ScenarioDefinition> FavoriteScenarios() =>
        _splashScenarios is null
            ? []
            : _splashScenarios
                .Where(IsFavored)
                .OrderBy(scenario => scenario.Priority)
                .ThenBy(scenario => scenario.Order);

    private bool IsFavored(ScenarioDefinition scenario) =>
        ScenarioFavorites.Value.FavoriteScenarioIds.Contains(scenario.Id);

    private bool IsScenarioDisabled(ScenarioDefinition scenario) => scenario.RequiresAdmin && !Version.IsAdmin;

    private Task LaunchScenarioAsync(ScenarioDefinition scenario) =>
        RunGuardedAsync(async () =>
        {
            var result = await ScenarioLaunch.LaunchAsync(scenario, null);
            Announcer.Announce(DescribeLaunch(scenario, result));
        });

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
