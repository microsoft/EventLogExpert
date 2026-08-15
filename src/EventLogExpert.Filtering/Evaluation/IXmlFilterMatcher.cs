// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Filtering.Evaluation;

public interface IXmlFilterMatcher
{
    /// <summary>
    ///     Computes per-log match for <paramref name="filter" /> over the rows of <paramref name="reader" />, rendering
    ///     XML on demand for candidate rows via <paramref name="owningLog" /> / <paramref name="pathType" />.
    /// </summary>
    XmlFilterMatch ComputeMatch(
        IEventColumnReader reader,
        Filter filter,
        string owningLog,
        LogPathType pathType,
        CancellationToken cancellationToken);
}
