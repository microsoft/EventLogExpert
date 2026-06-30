// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.ProviderDatabase.Context;
using Microsoft.Data.Sqlite;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.Common.Operations;

internal abstract class OperationBase
{
    private static readonly string[] s_databaseFileSuffixes = ["", "-wal", "-shm"];

    private string _providerDetailFormat = "{0, -14} {1, 8} {2, 8} {3, 8} {4, 8} {5, 8} {6, 8}";

    public string? FailureSummary { get; private set; }

    // Clearing SQLite pools releases Windows file handles before deleting the partial database.
    protected static async Task CleanupPartialDatabaseAsync(
        ITraceLogger logger,
        ProviderDbContext? dbContext,
        string targetPath)
    {
        if (dbContext is not null) { await dbContext.DisposeAsync(); }

        if (!s_databaseFileSuffixes.Any(suffix => File.Exists(targetPath + suffix))) { return; }

        SqliteConnection.ClearAllPools();

        foreach (var suffix in s_databaseFileSuffixes)
        {
            var databasePath = targetPath + suffix;

            if (!File.Exists(databasePath)) { continue; }

            try
            {
                File.Delete(databasePath);
            }
            catch (IOException ex)
            {
                logger.Warning($"Could not delete partial database at {databasePath}: {ex.Message}. Delete manually before next run.");
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.Warning($"Could not delete partial database at {databasePath}: {ex.Message}. Delete manually before next run.");
            }
        }
    }

    // Recompile infinite-timeout regexes so hostile patterns cannot hang the operation.
    protected static Regex? EnsureBoundedTimeout(Regex? regex, TimeSpan defaultTimeout)
    {
        if (regex is null) { return null; }

        return regex.MatchTimeout == Regex.InfiniteMatchTimeout
            ? new Regex(regex.ToString(), regex.Options, defaultTimeout)
            : regex;
    }

    protected static List<string> GetLocalProviderNames(Regex? regex)
    {
        var providers = new List<string>(EventLogSession.GlobalSession.GetProviderNames().Distinct().OrderBy(name => name));

        return regex is null ? providers : providers.Where(p => regex.IsMatch(p)).ToList();
    }

    protected static async IAsyncEnumerable<ProviderDetails> LoadLocalProvidersAsync(
        ITraceLogger logger,
        Regex? regex,
        IReadOnlySet<string>? excludeProviderNames = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Async iterator bridge: synchronous provider reads share an IAsyncEnumerable consumer path.
        await Task.CompletedTask;

        foreach (var providerName in GetLocalProviderNames(regex))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (excludeProviderNames is not null && excludeProviderNames.Contains(providerName)) { continue; }

            yield return new EventMessageProvider(providerName, logger: logger).LoadProviderDetails();
        }
    }

    protected static async IAsyncEnumerable<ProviderDetails> LoadOfflineImageProvidersAsync(
        string offlineImagePath,
        ITraceLogger logger,
        Regex? regex,
        IReadOnlySet<string>? excludeProviderNames = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        foreach (var details in OfflineImageProviderSource.LoadProviders(offlineImagePath, logger, regex, excludeProviderNames))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return details;
        }
    }

    protected void LogProviderDetailHeader(ITraceLogger logger, IEnumerable<string> providerNames)
    {
        var names = providerNames as IReadOnlyList<string> ?? providerNames.ToList();
        var maxNameLength = names.Count > 0 ? names.Max(providerName => providerName.Length) : 14;

        if (maxNameLength < 14) { maxNameLength = 14; }

        _providerDetailFormat = "{0, -" + maxNameLength + "} {1, 8} {2, 8} {3, 8} {4, 8} {5, 8} {6, 8}";

        var header = string.Format(
            _providerDetailFormat,
            "Provider Name", "Events", "Parameters", "Keywords", "Opcodes", "Tasks", "Messages");

        logger.Information($"{header}");
    }

    protected void LogProviderDetails(ITraceLogger logger, ProviderDetails details)
    {
        var line = string.Format(
            _providerDetailFormat,
            details.ProviderName,
            details.Events.Count,
            details.Parameters.Count,
            details.Keywords.Count,
            details.Opcodes.Count,
            details.Tasks.Count,
            details.Messages.Count);

        logger.Information($"{line}");
    }

    protected void SetFailureSummary(string summary) => FailureSummary = summary;
}
