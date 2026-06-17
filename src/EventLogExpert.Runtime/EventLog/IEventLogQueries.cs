// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.EventLog;

public interface IEventLogQueries
{
    /// <summary>
    ///     Returns the UTC date range covering all events across the active logs, with bounds rounded outward to the
    ///     hour, falling back to <paramref name="fallbackUtcNow" /> when no log has events.
    /// </summary>
    (DateTime After, DateTime Before) GetEventDateRange(DateTime fallbackUtcNow);
}
