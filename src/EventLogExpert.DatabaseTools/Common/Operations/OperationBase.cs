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

/// <summary>
///     Shared base for the 5 concrete <see cref="IDatabaseToolsOperation" /> implementations. Provides static helpers
///     for loading local providers and formatting provider-detail log lines. The logger is passed per-call (not held as
///     state) because <see cref="IDatabaseToolsOperation.ExecuteAsync" /> receives a fresh logger per invocation (the
///     streaming sink used by the UI).
/// </summary>
internal abstract class OperationBase
{
    private string _providerDetailFormat = "{0, -14} {1, 8} {2, 8} {3, 8} {4, 8} {5, 8} {6, 8}";

    /// <summary>
    ///     A user-actionable failure reason worth surfacing in the result chip (see
    ///     <see cref="IDatabaseToolsOperation.FailureSummary" />). Operations set this via <see cref="SetFailureSummary" />
    ///     when they fail-fast on a destination/scratch write denial so the actionable remedy is not buried in the log.
    /// </summary>
    public string? FailureSummary { get; private set; }

    /// <summary>
    ///     Cleans up a partially-created .db file after an operation aborts (cancellation, fatal exception). EF Core's
    ///     SqliteConnection pool keeps the file handle alive across <c>DbContext.Dispose</c>, so a naive <c>File.Delete</c>
    ///     hits a sharing violation on Windows. Mirrors the codebase pattern at
    ///     <c>ProviderDatabaseMaintenance.PrepareForFileDeletion()</c>: dispose context, clear pool, best-effort delete.
    ///     Callers pass the context by value and null their own local afterward so the surrounding <c>finally</c> does not
    ///     dispose twice. Best-effort: catches <see cref="IOException" /> and <see cref="UnauthorizedAccessException" /> with
    ///     a logger.Warning so the user has signal if the partial file persists.
    /// </summary>
    protected static async Task CleanupPartialDatabaseAsync(
        ITraceLogger logger,
        ProviderDbContext? dbContext,
        string targetPath)
    {
        if (dbContext is not null) { await dbContext.DisposeAsync(); }

        if (!File.Exists(targetPath)) { return; }

        SqliteConnection.ClearAllPools();

        try { File.Delete(targetPath); }
        catch (IOException ex)
        {
            logger.Warning($"Could not delete partial database at {targetPath}: {ex.Message}. Delete manually before next run.");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.Warning($"Could not delete partial database at {targetPath}: {ex.Message}. Delete manually before next run.");
        }
    }

    /// <summary>
    ///     Returns a <see cref="Regex" /> with a bounded <see cref="Regex.MatchTimeout" />, so a pathological pattern
    ///     (e.g., catastrophic backtracking) cannot hang the operation. If <paramref name="regex" /> already has a finite
    ///     timeout, it is returned as-is. If it has the default <c>Regex.InfiniteMatchTimeout</c>, a fresh
    ///     <see cref="Regex" /> is compiled from the same pattern/options with <paramref name="defaultTimeout" />. Returns
    ///     <c>null</c> when input is <c>null</c>. Operations should call this at entry to guarantee any
    ///     <see cref="RegexMatchTimeoutException" /> catch block is actually reachable, regardless of how the caller
    ///     constructed the regex.
    /// </summary>
    protected static Regex? EnsureBoundedTimeout(Regex? regex, TimeSpan defaultTimeout)
    {
        if (regex is null) { return null; }

        return regex.MatchTimeout == Regex.InfiniteMatchTimeout
            ? new Regex(regex.ToString(), regex.Options, defaultTimeout)
            : regex;
    }

    /// <summary>
    ///     Returns the distinct local provider names installed on this machine, optionally filtered by a
    ///     <paramref name="regex" /> whose case sensitivity follows the caller's <see cref="RegexOptions" />.
    /// </summary>
    protected static List<string> GetLocalProviderNames(Regex? regex)
    {
        var providers = new List<string>(EventLogSession.GlobalSession.GetProviderNames().Distinct().OrderBy(name => name));

        return regex is null ? providers : providers.Where(p => regex.IsMatch(p)).ToList();
    }

    /// <summary>
    ///     Yields <see cref="ProviderDetails" /> for each local provider, applying the optional regex filter and
    ///     name-level <paramref name="excludeProviderNames" /> exclude set before metadata is resolved. Local providers are
    ///     live, so their identity is always <c>(name, "")</c>; a name-level exclude is therefore exact here.
    /// </summary>
    protected static async IAsyncEnumerable<ProviderDetails> LoadLocalProvidersAsync(
        ITraceLogger logger,
        Regex? regex,
        IReadOnlySet<string>? excludeProviderNames = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Local-provider resolution is synchronous (registry/metadata reads). This wrapper exposes it as an
        // IAsyncEnumerable so callers can stream local and file-source providers through one await foreach; the
        // await keeps it a valid async iterator without scheduling an extra continuation.
        await Task.CompletedTask;

        foreach (var providerName in GetLocalProviderNames(regex))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (excludeProviderNames is not null && excludeProviderNames.Contains(providerName)) { continue; }

            yield return new EventMessageProvider(providerName, logger: logger).LoadProviderDetails();
        }
    }

    /// <summary>
    ///     Streams providers extracted from a mounted or extracted foreign Windows image, fully offline. Mirrors
    ///     <see cref="LoadLocalProvidersAsync" />: offline extraction is synchronous (registry-hive + DLL reads), so this
    ///     wrapper exposes it as an <see cref="IAsyncEnumerable{T}" /> for the shared <c>await foreach</c> consume loop; the
    ///     await keeps it a valid async iterator without scheduling an extra continuation. The facade applies the name filter
    ///     and exclude set itself (so this does not re-filter) and stamps each provider with the IMAGE's OS provenance.
    /// </summary>
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
            details.Parameters.Count,
            details.Keywords.Count,
            details.Opcodes.Count,
            details.Tasks.Count,
            details.Messages.Count);

        logger.Information($"{line}");
    }

    /// <summary>Records a user-actionable <see cref="FailureSummary" /> for a fail-fast outcome.</summary>
    protected void SetFailureSummary(string summary) => FailureSummary = summary;
}
