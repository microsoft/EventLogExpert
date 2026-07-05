// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.EventLog;

public interface IEventLogQueries
{
    /// <summary>
    ///     Returns the distinct names of the currently open Channel logs (excludes File logs). Used by scenario authoring
    ///     export to record which channels the filter rows were captured against.
    /// </summary>
    IReadOnlyList<string> GetChannelNames();

    /// <summary>
    ///     Returns the distinct, sorted &lt;EventData&gt; field names present across all open raw events (used to
    ///     populate the Basic editor's EventData field-name picker).
    /// </summary>
    ImmutableArray<string> GetEventDataFieldNames();

    /// <summary>
    ///     Returns the distinct, sorted values of the named EventData <paramref name="fieldName" /> across all open raw
    ///     events (used to populate the value picker for an EventData filter row).
    /// </summary>
    ImmutableArray<string> GetEventDataFieldValues(string fieldName);

    /// <summary>
    ///     Returns the UTC date range covering all events across the active logs, with bounds rounded outward to the
    ///     hour, falling back to <paramref name="fallbackUtcNow" /> when no log has events.
    /// </summary>
    (DateTime After, DateTime Before) GetEventDateRange(DateTime fallbackUtcNow);

    /// <summary>
    ///     Returns the distinct, sorted set of values present across all open raw events for the given
    ///     <paramref name="property" /> (used to populate filter value pickers). Empty for properties that are not derived
    ///     from event data.
    /// </summary>
    ImmutableArray<string> GetPropertyValues(EventProperty property);
}
