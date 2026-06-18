// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.UI.Dashboard;

internal enum SplashCategory
{
    Favorites,
    Recommended,
    SystemHealth,
    Applications,
    Security,
    ThreatsAndIncidentResponse,
    Network,
    Storage,
    UpdatesAndPolicy,
    ActiveDirectory,
    DnsServer,
    DhcpServer,
    NpsAndRras,
    Wins,
    WebAndIis,
    VirtualizationAndClustering,
    FilePrintAndStorage,
    SqlServer,
    Exchange,
    SharePoint,
    DefenderForEndpoint,
    Office,
}

internal static class SplashCategoryMapping
{
    public static SplashCategory? ToSplashCategory(ScenarioGroup group) => group switch
    {
        ScenarioGroup.SystemHealth => SplashCategory.SystemHealth,
        ScenarioGroup.Applications => SplashCategory.Applications,
        ScenarioGroup.Security => SplashCategory.Security,
        ScenarioGroup.ThreatsAndIncidentResponse => SplashCategory.ThreatsAndIncidentResponse,
        ScenarioGroup.Network => SplashCategory.Network,
        ScenarioGroup.Storage => SplashCategory.Storage,
        ScenarioGroup.UpdatesAndPolicy => SplashCategory.UpdatesAndPolicy,
        ScenarioGroup.ActiveDirectory => SplashCategory.ActiveDirectory,
        ScenarioGroup.DnsServer => SplashCategory.DnsServer,
        ScenarioGroup.DhcpServer => SplashCategory.DhcpServer,
        ScenarioGroup.NpsAndRras => SplashCategory.NpsAndRras,
        ScenarioGroup.Wins => SplashCategory.Wins,
        ScenarioGroup.WebAndIis => SplashCategory.WebAndIis,
        ScenarioGroup.VirtualizationAndClustering => SplashCategory.VirtualizationAndClustering,
        ScenarioGroup.FilePrintAndStorage => SplashCategory.FilePrintAndStorage,
        ScenarioGroup.SqlServer => SplashCategory.SqlServer,
        ScenarioGroup.Exchange => SplashCategory.Exchange,
        ScenarioGroup.SharePoint => SplashCategory.SharePoint,
        ScenarioGroup.DefenderForEndpoint => SplashCategory.DefenderForEndpoint,
        ScenarioGroup.Office => SplashCategory.Office,
        _ => null,
    };
}
