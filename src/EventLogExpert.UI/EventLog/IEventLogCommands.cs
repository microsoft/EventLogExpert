// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.EventLog;

/// <summary>
///     Host-facing intent API for the EventLog slice. Hides slice-internal action records from
///     consumers outside the UI assembly so the IVT grant to <c>EventLogExpert</c> can be dropped.
/// </summary>
public interface IEventLogCommands
{
    /// <summary>Reads any buffered new events into the active table and clears the buffer counter.</summary>
    void LoadNewEvents();
}
