// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Models;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.Operations;

/// <summary>
///     Shared base for the 5 concrete <see cref="IDatabaseToolsOperation" /> implementations. Provides static helpers
///     for loading local providers and formatting provider-detail log lines. The logger is passed per-call (not held as
///     state) because <see cref="IDatabaseToolsOperation.ExecuteAsync" /> receives a fresh logger per invocation (the
///     streaming sink used by the UI).
/// </summary>
public abstract class OperationBase
{
    private string _providerDetailFormat = "{0, -14} {1, 8} {2, 8} {3, 8} {4, 8} {5, 8} {6, 8}";

    /// <summary>
    ///     Returns the distinct local provider names installed on this machine, optionally filtered by a case-insensitive
    ///     regex.
    /// </summary>
    protected static List<string> GetLocalProviderNames(Regex? regex)
    {
        var providers = new List<string>(EventLogSession.GlobalSession.GetProviderNames().Distinct().OrderBy(name => name));

        return regex is null ? providers : providers.Where(p => regex.IsMatch(p)).ToList();
    }

    /// <summary>
    ///     Yields <see cref="ProviderDetails" /> for each local provider, applying the optional regex filter and skip set
    ///     before metadata is resolved.
    /// </summary>
    protected static IEnumerable<ProviderDetails> LoadLocalProviders(
        ITraceLogger logger,
        Regex? regex,
        IReadOnlySet<string>? skipProviderNames = null)
    {
        foreach (var providerName in GetLocalProviderNames(regex))
        {
            if (skipProviderNames is not null && skipProviderNames.Contains(providerName)) { continue; }

            yield return new EventMessageProvider(providerName, logger: logger).LoadProviderDetails();
        }
    }

    /// <summary>
    ///     Emits the provider-details column header sized to the longest provider name. Updates the instance format
    ///     string so subsequent <see cref="LogProviderDetails" /> calls align to the same width.
    /// </summary>
    protected void LogProviderDetailHeader(ITraceLogger logger, IEnumerable<string> providerNames)
    {
        var maxNameLength = providerNames.Any() ? providerNames.Max(p => p.Length) : 14;
        if (maxNameLength < 14) { maxNameLength = 14; }

        _providerDetailFormat = "{0, -" + maxNameLength + "} {1, 8} {2, 8} {3, 8} {4, 8} {5, 8} {6, 8}";

        var header = string.Format(
            _providerDetailFormat,
            "Provider Name", "Events", "Parameters", "Keywords", "Opcodes", "Tasks", "Messages");

        logger.Information($"{header}");
    }

    /// <summary>Emits one formatted line for the given <see cref="ProviderDetails" />.</summary>
    protected void LogProviderDetails(ITraceLogger logger, ProviderDetails details)
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

        logger.Information($"{line}");
    }
}
