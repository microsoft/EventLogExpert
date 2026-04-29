// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Readers;
using System.Text.RegularExpressions;

namespace EventLogExpert.EventDbTool;

public class DbToolCommand(ITraceLogger logger)
{
    private string _providerDetailFormat = "{0, -14} {1, 8} {2, 8} {3, 8} {4, 8} {5, 8}";

    protected ITraceLogger Logger => logger;

    protected static List<string> GetLocalProviderNames(string? filter, ITraceLogger logger) =>
        !RegexHelper.TryCreate(filter, logger, out var regex) ? [] : GetLocalProviderNames(regex);

    protected static List<string> GetLocalProviderNames(Regex? regex)
    {
        var providers = new List<string>(EventLogSession.GlobalSession.GetProviderNames().Distinct().OrderBy(name => name));

        return regex is null ? providers : providers.Where(p => regex.IsMatch(p)).ToList();
    }

    protected IEnumerable<ProviderDetails> LoadLocalProviders(string? filter, IReadOnlySet<string>? skipProviderNames = null)
    {
        if (!RegexHelper.TryCreate(filter, Logger, out var regex)) { yield break; }

        foreach (var details in LoadLocalProviders(regex, skipProviderNames))
        {
            yield return details;
        }
    }

    protected IEnumerable<ProviderDetails> LoadLocalProviders(Regex? regex, IReadOnlySet<string>? skipProviderNames = null)
    {
        foreach (var providerName in GetLocalProviderNames(regex))
        {
            // Skip BEFORE resolving so we don't pay the cost of loading metadata for providers we
            // are about to discard (e.g. when --skip-providers-in-file lists most local providers).
            if (skipProviderNames is not null && skipProviderNames.Contains(providerName)) { continue; }

            yield return new EventMessageProvider(providerName, logger: Logger).LoadProviderDetails();
        }
    }

    protected void LogProviderDetailHeader(IEnumerable<string> providerNames)
    {
        var maxNameLength = providerNames.Any() ? providerNames.Max(p => p.Length) : 14;
        if (maxNameLength < 14) { maxNameLength = 14; }

        _providerDetailFormat = "{0, -" + maxNameLength + "} {1, 8} {2, 8} {3, 8} {4, 8} {5, 8} {6, 8}";

        var header = string.Format(_providerDetailFormat, "Provider Name", "Events", "Parameters", "Keywords", "Opcodes", "Tasks", "Messages");
        Logger.Info($"{header}");
    }

    protected void LogProviderDetails(ProviderDetails details)
    {
        var line = string.Format(
            _providerDetailFormat,
            details.ProviderName,
            details.Events.Count,
            details.Parameters.Count(),
            details.Keywords.Count,
            details.Opcodes.Count,
            details.Tasks.Count,
            details.Messages.Count);

        Logger.Info($"{line}");
    }
}
