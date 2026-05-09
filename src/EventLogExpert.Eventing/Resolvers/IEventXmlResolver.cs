// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Eventing.Resolvers;

/// <summary>
///     Resolves the raw XML for a <see cref="ResolvedEvent" /> on demand and caches the result. Implementations
///     must be thread-safe and must coalesce concurrent requests for the same event into a single underlying
///     <c>EvtQuery</c> / <c>RenderEventXml</c> call.
/// </summary>
public interface IEventXmlResolver
{
    /// <summary>Removes all cached entries. Call when every active log is closed.</summary>
    void ClearAll();

    /// <summary>Removes cached entries for a specific log path. Call when an individual log is closed.</summary>
    void ClearXmlCacheForLog(string owningLog);

    /// <summary>
    ///     Returns the XML for <paramref name="evt" />. If <see cref="ResolvedEvent.Xml" /> is already populated
    ///     (because the log was opened with <c>renderXml: true</c>), the pre-rendered value is returned immediately; otherwise
    ///     the resolver re-opens the source log via <c>EvtQuery</c>, locates the record by
    ///     <see cref="ResolvedEvent.RecordId" />, and renders the XML.
    /// </summary>
    /// <param name="evt">The event to resolve XML for.</param>
    /// <param name="cancellationToken">
    ///     Cancellation for the caller's wait. Cancelling does not invalidate or evict the
    ///     in-flight resolution; concurrent callers waiting on the same cache entry continue to observe the result.
    /// </param>
    /// <returns>The XML string, or <see cref="string.Empty" /> if the record cannot be located or rendering fails.</returns>
    ValueTask<string> GetXmlAsync(ResolvedEvent evt, CancellationToken cancellationToken = default);
}
