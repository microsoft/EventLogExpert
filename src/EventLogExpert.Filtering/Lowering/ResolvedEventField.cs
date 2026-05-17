// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common;

namespace EventLogExpert.Filtering.Lowering;

/// <summary>
///     Parser-side enumeration of every <see cref="ResolvedEvent" /> property the filter grammar may reference. Wider
///     than <see cref="EventProperty" /> (which only enumerates the BasicFilter authoring vocabulary) because the Advanced
///     free-text grammar may reach any public property on the event.
/// </summary>
internal enum ResolvedEventField
{
    ActivityId,
    ComputerName,
    Description,
    Id,
    Keywords,
    Level,
    LogName,
    ProcessId,
    RecordId,
    Source,
    TaskCategory,
    ThreadId,
    TimeCreated,
    UserId,
    Xml
}
