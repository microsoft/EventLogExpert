// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.ShowProviders;

/// <summary>
///     Lists provider details from either local providers (<see cref="ShowProvidersRequest.SourcePath" /> = null) or
///     a specified source (.db / .evtx / folder). Streams output as each provider is resolved.
/// </summary>
internal sealed class ShowProvidersOperation(ShowProvidersRequest request) : OperationBase, IDatabaseToolsOperation
{
    private const int HeaderBatchSize = 100;

    public async Task<DatabaseToolsOutcome> ExecuteAsync(
        ITraceLogger logger,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Defensive recompile if input has Regex.InfiniteMatchTimeout (otherwise catch below is dead).
        var filterRegex = EnsureBoundedTimeout(request.FilterRegex, TimeSpan.FromSeconds(5));

        // Buffer first batch to size the column widths (mirrors CreateDatabaseOperation). Lives outside
        // the try so the cancellation arm can flush partial output the user has been waiting on.
        var headerLogged = false;
        var pendingForHeader = new List<ProviderDetails>(HeaderBatchSize);
        var processed = 0;

        try
        {
            IAsyncEnumerable<ProviderDetails> providers;

            if (string.IsNullOrEmpty(request.SourcePath))
            {
                providers = LoadLocalProvidersAsync(logger, filterRegex, cancellationToken: cancellationToken);
            }
            else
            {
                if (!ProviderSource.TryValidate(request.SourcePath, logger))
                {
                    return DatabaseToolsOutcome.Failed;
                }

                providers = ProviderSource.LoadProvidersAsync(request.SourcePath, logger, filterRegex, cancellationToken: cancellationToken);
            }

            await foreach (var details in providers.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!headerLogged)
                {
                    pendingForHeader.Add(details);
                    processed++;
                    progress?.Report(new DatabaseToolsProgress(processed, null, details.ProviderName));

                    if (pendingForHeader.Count < HeaderBatchSize) { continue; }

                    FlushHeaderAndBuffer(logger, pendingForHeader);
                    headerLogged = true;
                    pendingForHeader.Clear();

                    continue;
                }

                LogProviderDetails(logger, details);
                processed++;
                progress?.Report(new DatabaseToolsProgress(processed, null, details.ProviderName));
            }

            if (!headerLogged && pendingForHeader.Count > 0)
            {
                FlushHeaderAndBuffer(logger, pendingForHeader);
            }

            if (processed == 0)
            {
                logger.Warning($"No providers found.");
            }

            return DatabaseToolsOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            // Flush buffered providers so the user sees partial output before "[Cancelled]".
            if (!headerLogged && pendingForHeader.Count > 0)
            {
                FlushHeaderAndBuffer(logger, pendingForHeader);
            }

            return DatabaseToolsOutcome.Cancelled;
        }
        catch (RegexMatchTimeoutException)
        {
            logger.Error($"The provider-name regex timed out. The pattern may cause catastrophic backtracking.");

            return DatabaseToolsOutcome.Failed;
        }
    }

    private void FlushHeaderAndBuffer(ITraceLogger logger, List<ProviderDetails> buffer)
    {
        LogProviderDetailHeader(logger, buffer.Select(p => p.ProviderName));

        foreach (var details in buffer)
        {
            LogProviderDetails(logger, details);
        }
    }
}
