// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;

namespace EventLogExpert.UI.LogTable.Find;

public interface IFindMarkerSource
{
    event EventHandler? MarksChanged;

    EventLogId? Owner { get; }

    /// <summary>The current match timestamps in ascending UTC-tick order; empty when Find is closed or has no matches.</summary>
    IReadOnlyList<long> Ticks { get; }

    void Clear();

    /// <summary>
    ///     Publishes owner-tagged, ascending match timestamps; the list is snapshotted so later caller mutations don't
    ///     change the published marks.
    /// </summary>
    void Publish(EventLogId owner, IReadOnlyList<long> sortedTicks);
}
