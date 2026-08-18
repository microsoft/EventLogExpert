// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.ActivityCorrelation;

/// <summary>The role a correlated activity plays relative to the selected event's activity.</summary>
public enum ActivityNodeRole
{
    /// <summary>The selected event's own activity.</summary>
    Focus,

    /// <summary>An activity the focus activity points at via <c>RelatedActivityId</c> (a causal parent).</summary>
    Parent,

    /// <summary>An activity that points at the focus activity via <c>RelatedActivityId</c> (a causal child).</summary>
    Child
}

/// <summary>
///     A single correlated event within an <see cref="ActivityNode" />. Carries only topology (the locator that
///     selects/reveals it plus its timestamp for sorting); display fields (level, source, event id) are resolved lazily by
///     the UI for the rows it actually renders, so the neighborhood never materializes strings for the whole log.
/// </summary>
public readonly record struct CorrelationEventLeaf(EventLocator Locator, long TimeTicks);

/// <summary>The snapshot identity a correlation view was built from, for freshness comparison against the live store.</summary>
public readonly record struct CorrelationContentToken(EventLogId LogId, int Generation, long ContentVersion, int Count);

/// <summary>One activity (a distinct <c>ActivityId</c>) in the selected event's within-log correlation neighborhood.</summary>
public sealed record ActivityNode
{
    /// <summary>The distinct <c>ActivityId</c> this node groups.</summary>
    public required Guid ActivityId { get; init; }

    /// <summary>Whether this is the focus activity, a parent, or a child.</summary>
    public required ActivityNodeRole Role { get; init; }

    /// <summary>The total number of member events in this log (may exceed <see cref="Leaves" />.Count when truncated).</summary>
    public required int EventCount { get; init; }

    /// <summary>The UTC-tick timestamp of the earliest built leaf (0 when the activity has no rows in this log).</summary>
    public required long MinTicks { get; init; }

    /// <summary>The UTC-tick timestamp of the latest built leaf (0 when the activity has no rows in this log).</summary>
    public required long MaxTicks { get; init; }

    /// <summary>
    ///     True when the member count exceeds the false-fusion threshold, marking a likely shared / sentinel
    ///     <c>ActivityId</c> that may span unrelated operations; such a node is not auto-expanded.
    /// </summary>
    public required bool IsSharedOversized { get; init; }

    /// <summary>The distinct parent activities referenced by this node's members; more than one is an ambiguous parentage.</summary>
    public IReadOnlyList<Guid> Parents { get; init; } = [];

    /// <summary>True when this activity both references and is referenced by the focus activity (a 2-cycle); shown once.</summary>
    public bool IsCycle { get; init; }

    /// <summary>The number of member events at Critical severity (part of the error headline).</summary>
    public int CriticalCount { get; init; }

    /// <summary>The number of member events at Error severity.</summary>
    public int ErrorCount { get; init; }

    /// <summary>The number of member events at Warning severity.</summary>
    public int WarningCount { get; init; }

    /// <summary>The headline "errors" count: Critical plus Error.</summary>
    public int ErrorTotal => CriticalCount + ErrorCount;

    /// <summary>The member events (capped for display), sorted by time.</summary>
    public IReadOnlyList<CorrelationEventLeaf> Leaves { get; init; } = [];

    /// <summary>True when <see cref="EventCount" /> exceeds the per-node display cap, so <see cref="Leaves" /> is a prefix.</summary>
    public bool LeavesTruncated { get; init; }

    /// <summary>True when this node has more than one distinct parent activity (parentage cannot be a single tree edge).</summary>
    public bool HasMultipleParents => Parents.Count > 1;
}

/// <summary>
///     The within-log activity correlation neighborhood of a selected event: its own activity plus, where
///     <c>RelatedActivityId</c> links exist, the immediate parent and child activities. When no <c>RelatedActivityId</c>
///     edges are present (the common case) this degrades to the focus activity's grouped events alone.
/// </summary>
public sealed record ActivityCorrelationView
{
    /// <summary>The log the neighborhood was built from (correlation is scoped to a single log).</summary>
    public required EventLogId LogId { get; init; }

    /// <summary>The selected event's <c>ActivityId</c>; <see cref="Guid.Empty" /> when the event carries no activity.</summary>
    public required Guid FocusActivityId { get; init; }

    /// <summary>The snapshot identity this view was built from, for staleness detection against live-tail updates.</summary>
    public required CorrelationContentToken Token { get; init; }

    /// <summary>The focus activity followed by its parent and child activities, ordered for display.</summary>
    public IReadOnlyList<ActivityNode> Activities { get; init; } = [];

    /// <summary>True when any <c>RelatedActivityId</c> parent/child edge was found in the neighborhood.</summary>
    public bool HasHierarchy { get; init; }

    /// <summary>True when the selected event carries no usable <c>ActivityId</c>, so there is nothing to correlate.</summary>
    public bool IsEmpty => FocusActivityId == Guid.Empty || Activities.Count == 0;
}
