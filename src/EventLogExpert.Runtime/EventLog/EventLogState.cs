// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Evaluation;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.EventLog;

[FeatureState]
public sealed record EventLogState
{
    /// <summary>The maximum number of new events we will hold in the state before we turn off the watcher.</summary>
    public static int MaxNewEvents => 1000;

    internal ImmutableDictionary<string, OpenLogInfo> OpenLogs { get; init; } =
        ImmutableDictionary<string, OpenLogInfo>.Empty;

    public int OpenLogCount => OpenLogs.Count;

    internal ImmutableDictionary<string, ImmutableHashSet<string>> NamesByLog { get; init; } =
        ImmutableDictionary<string, ImmutableHashSet<string>>.Empty;

    public ImmutableHashSet<string> LoadedLogNames { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    public bool IsLogOpen(string logPath) => OpenLogs.ContainsKey(logPath);

    public Filter AppliedFilter { get; init; } = new(null, []);

    public bool ContinuouslyUpdate { get; init; } = false;

    public IReadOnlyList<ResolvedEvent> NewEventBuffer { get; init; } = [];

    public bool NewEventBufferIsFull { get; init; }

    public ResolvedEvent? SelectedEvent { get; init; }

    public ImmutableList<ResolvedEvent> SelectedEvents { get; init; } = [];
}
