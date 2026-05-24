// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.DatabaseTools.Sources;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Models;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.Operations;

/// <summary>
///     Lists provider details from either local providers (<see cref="ShowProvidersRequest.SourcePath" /> = null) or
///     a specified source (.db / .evtx / folder). Streams output as each provider is resolved.
/// </summary>
public sealed class ShowProvidersOperation(ShowProvidersRequest request) : OperationBase, IDatabaseToolsOperation
{
    public Task<DatabaseToolsOutcome> ExecuteAsync(
        ITraceLogger logger,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Defensive: defaults Regex.InfiniteMatchTimeout means RegexMatchTimeoutException can never fire — recompile
        // with a 5-second bound so catastrophic backtracking is caught regardless of how the caller built the regex.
        var filterRegex = EnsureBoundedTimeout(request.FilterRegex, TimeSpan.FromSeconds(5));

        try
        {
            IReadOnlyList<string> providerNames;
            IEnumerable<ProviderDetails> providers;

            if (string.IsNullOrEmpty(request.SourcePath))
            {
                providerNames = GetLocalProviderNames(filterRegex);
                providers = LoadLocalProviders(logger, filterRegex);
            }
            else
            {
                if (!ProviderSource.TryValidate(request.SourcePath, logger))
                {
                    return Task.FromResult(DatabaseToolsOutcome.Failed);
                }

                providerNames = ProviderSource.LoadProviderNames(request.SourcePath, logger, filterRegex);
                providers = ProviderSource.LoadProviders(request.SourcePath, logger, filterRegex);
            }

            if (providerNames.Count == 0)
            {
                logger.Warning($"No providers found.");

                return Task.FromResult(DatabaseToolsOutcome.Succeeded);
            }

            LogProviderDetailHeader(logger, providerNames);

            var processed = 0;

            foreach (var details in providers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                LogProviderDetails(logger, details);

                processed++;
                progress?.Report(new DatabaseToolsProgress(processed, providerNames.Count, details.ProviderName));
            }

            return Task.FromResult(DatabaseToolsOutcome.Succeeded);
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(DatabaseToolsOutcome.Cancelled);
        }
        catch (RegexMatchTimeoutException)
        {
            logger.Error($"The provider-name regex timed out. The pattern may cause catastrophic backtracking.");

            return Task.FromResult(DatabaseToolsOutcome.Failed);
        }
    }
}
