// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Scenarios.Catalog;

public static class ScenarioGroupDisplay
{
    public static string DisplayName(this ScenarioGroup group) => group switch
    {
        ScenarioGroup.SystemHealth => "System Health",
        ScenarioGroup.Applications => "Applications",
        ScenarioGroup.Security => "Security",
        ScenarioGroup.ThreatsAndIncidentResponse => "Threats and Incident Response",
        ScenarioGroup.Network => "Network",
        ScenarioGroup.Storage => "Storage",
        ScenarioGroup.UpdatesAndPolicy => "Updates and Policy",
        ScenarioGroup.ActiveDirectory => "Active Directory",
        ScenarioGroup.DnsServer => "DNS Server",
        ScenarioGroup.DhcpServer => "DHCP Server",
        ScenarioGroup.NpsAndRras => "NPS and RRAS",
        ScenarioGroup.Wins => "WINS",
        ScenarioGroup.WebAndIis => "Web and IIS",
        ScenarioGroup.VirtualizationAndClustering => "Virtualization and Clustering",
        ScenarioGroup.FilePrintAndStorage => "File, Print, and Storage",
        ScenarioGroup.SqlServer => "SQL Server",
        ScenarioGroup.Exchange => "Exchange",
        ScenarioGroup.SharePoint => "SharePoint",
        ScenarioGroup.DefenderForEndpoint => "Defender for Endpoint",
        ScenarioGroup.Office => "Office",
        _ => group.ToString()
    };
}
