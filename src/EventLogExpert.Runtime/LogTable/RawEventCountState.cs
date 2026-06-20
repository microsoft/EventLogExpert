// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.LogTable;

/// <summary>
///     Public, render-isolated mirror of the raw event count per log. Carries only integer counts (never the raw
///     events) so the status bar can show a live "Events Loaded" total without coupling the UI to the internal raw store.
///     Kept in sync with <c>RawEventStoreState</c> by reducing the same lifecycle actions.
/// </summary>
[FeatureState]
public sealed record RawEventCountState
{
    public ImmutableDictionary<EventLogId, int> ByLog { get; init; } = ImmutableDictionary<EventLogId, int>.Empty;

    public int Total => ByLog.Values.Sum();
}
