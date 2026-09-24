// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging.Extraction;
using EventLogExpert.Eventing.ProviderMetadata;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Database.Context;
using EventLogExpert.Provider.Resolution;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.Common.Operations;

internal abstract class OperationBase
{
    protected static readonly TimeSpan DefaultFilterRegexTimeout = TimeSpan.FromSeconds(5);
    private static readonly string[] s_databaseFileSuffixes = ["", "-wal", "-shm"];

    private string _providerDetailFormat = "{0, -14} {1, 8} {2, 8} {3, 8} {4, 8} {5, 8} {6, 8}";

    public LocalizableText? FailureSummary { get; private set; }

    protected static async Task CleanupPartialDatabaseAsync(
        IOperationLog logger,
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
                logger.User(LogLevel.Warning,
                    new LocalizableText(DatabaseToolsLogKeys.OperationDeletePartialFailed, [databasePath]),
                    ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.User(LogLevel.Warning,
                    new LocalizableText(DatabaseToolsLogKeys.OperationDeletePartialFailed, [databasePath]),
                    ex);
            }
        }
    }

    protected static Regex? EnsureBoundedTimeout(Regex? regex) =>
        EnsureBoundedTimeout(regex, DefaultFilterRegexTimeout);

    protected static Regex? EnsureBoundedTimeout(Regex? regex, TimeSpan defaultTimeout)
    {
        if (regex is null) { return null; }

        return regex.MatchTimeout == Regex.InfiniteMatchTimeout ?
            new Regex(regex.ToString(), regex.Options, defaultTimeout) :
            regex;
    }

    protected static List<string> GetLocalProviderNames(Regex? regex)
    {
        var providers = new List<string>(EventLogSession.GlobalSession.GetProviderNames()
            .Distinct()
            .OrderBy(name => name));

        return regex is null ? providers : [.. providers.Where(p => regex.IsMatch(p))];
    }

    protected static async IAsyncEnumerable<ProviderDetails> LoadLocalProvidersAsync(
        IOperationLog logger,
        Regex? regex,
        IReadOnlySet<string>? excludeProviderNames = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        foreach (var providerName in GetLocalProviderNames(regex))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (excludeProviderNames is not null && excludeProviderNames.Contains(providerName)) { continue; }

            yield return new EventMessageProvider(providerName, logger: logger.Trace).LoadProviderDetails();
        }
    }

    protected static async IAsyncEnumerable<ProviderDetails> LoadOfflineImageProvidersAsync(
        string offlineImagePath,
        IOperationLog logger,
        Regex? regex,
        IReadOnlySet<string>? excludeProviderNames = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        foreach (var details in OfflineImageProviderSource.LoadProviders(offlineImagePath,
            logger.Trace,
            regex,
            excludeProviderNames))
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return details;
        }
    }

    protected void LogProviderDetailHeader(IOperationLog logger, IEnumerable<string> providerNames)
    {
        var names = providerNames as IReadOnlyList<string> ?? [.. providerNames];
        var maxNameLength = names.Count > 0 ? names.Max(providerName => providerName.Length) : 14;

        if (maxNameLength < 14) { maxNameLength = 14; }

        _providerDetailFormat = "{0, -" + maxNameLength + "} {1, 8} {2, 8} {3, 8} {4, 8} {5, 8} {6, 8}";

        // Intentional English residual (not a localization miss): this is a fixed-width provider-detail data table
        // whose column layout is computed at runtime from the provider names, emitted verbatim through Data() rather
        // than the keyed User() path. Localizing variable-length column labels through the deferred key-based pipeline
        // would break the alignment and mix a localized header with English data rows, so the table chrome stays
        // English by design.
        var header = string.Format(
            _providerDetailFormat,
            "Provider Name",
            "Events",
            "Parameters",
            "Keywords",
            "Opcodes",
            "Tasks",
            "Messages");

        logger.Data(LogLevel.Information, header);
    }

    protected void LogProviderDetails(IOperationLog logger, ProviderDetails details)
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

        logger.Data(LogLevel.Information, line);
    }

    protected void SetFailureSummary(LocalizableText summary) => FailureSummary = summary;
}
