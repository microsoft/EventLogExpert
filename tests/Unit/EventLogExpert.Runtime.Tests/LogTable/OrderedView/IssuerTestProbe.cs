// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

internal static class IssuerTestProbe
{
    internal static IReadOnlyDictionary<EventLogId, IEventColumnReader> NoReaders { get; } =
        ImmutableDictionary<EventLogId, IEventColumnReader>.Empty;

    internal static IssueKind Issue(this ViewRequestIssuer issuer, ViewIdentity identity) =>
        issuer.TryIssue(identity, Matching(identity)).Kind;

    internal static Func<ViewProbe> Matching(ViewIdentity identity) =>
        () => new ViewProbe(identity, NoReaders, default);
}
