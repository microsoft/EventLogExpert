// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Reader;
using System.Text.RegularExpressions;

namespace EventLogExpert.EventDbTool;

public class DbToolCommand
{
    private static string s_providerDetailFormat = "{0, -14} {1, 8} {2, 8} {3, 8} {4, 8} {5, 8}";

    public static List<string> GetLocalProviderNames(string filter)
    {
        var providers = new List<string>(EventLogSession.GlobalSession.GetProviderNames().Distinct().OrderBy(name => name));

        if (!string.IsNullOrEmpty(filter))
        {
            var regex = new Regex(filter, RegexOptions.IgnoreCase);
            providers = providers.Where(p => regex.IsMatch(p)).ToList();
        }

        return providers;
    }

    public static void LogProviderDetailHeader(IEnumerable<string> providerNames)
    {
        var maxNameLength = providerNames.Any() ? providerNames.Max(p => p.Length) : 14;
        if (maxNameLength < 14) maxNameLength = 14;
        s_providerDetailFormat = "{0, -" + maxNameLength + "} {1, 8} {2, 8} {3, 8} {4, 8} {5, 8} {6, 8}";
        Console.WriteLine(s_providerDetailFormat, "Provider Name", "Events", "Parameters", "Keywords", "Opcodes", "Tasks", "Messages");
    }

    public static void LogProviderDetails(ProviderDetails details)
    {
        Console.WriteLine(
            s_providerDetailFormat,
            details.ProviderName,
            details.Events.Count,
            details.Parameters.Count,
            details.Keywords.Count,
            details.Opcodes.Count,
            details.Tasks.Count,
            details.Messages.Count);
    }
}
