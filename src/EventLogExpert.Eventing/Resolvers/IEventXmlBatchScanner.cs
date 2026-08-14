// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;

namespace EventLogExpert.Eventing.Resolvers;

public interface IEventXmlBatchScanner
{
    /// <summary>
    ///     Streams each event's record id and freshly rendered XML from a single sequential scan of
    ///     <paramref name="owningLog" />, rendering (and yielding) XML only for records that
    ///     <paramref name="shouldRenderXml" /> accepts.
    /// </summary>
    IEnumerable<ScannedEventXml> Scan(
        string owningLog,
        LogPathType pathType,
        Func<long, bool> shouldRenderXml,
        CancellationToken cancellationToken);
}
