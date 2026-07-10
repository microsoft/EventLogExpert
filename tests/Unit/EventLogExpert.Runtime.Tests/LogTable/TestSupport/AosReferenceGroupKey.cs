// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.LogTable;
using System.Globalization;

namespace EventLogExpert.Runtime.Tests.LogTable.TestSupport;

/// <summary>
///     Test-only array-of-structs grouping-key oracle, relocated verbatim from the deleted production
///     <c>ResolvedEventGroupKey.For(ColumnName, ResolvedEvent)</c> overload. The live reader-based
///     <see cref="ResolvedEventGroupKey.For(IEventColumnReader, EventLocator, ColumnName)" /> is validated against this
///     reference; "" is the no-value bucket.
/// </summary>
internal static class AosReferenceGroupKey
{
    internal static string For(ColumnName column, ResolvedEvent @event) =>
        column switch
        {
            ColumnName.RecordId => @event.RecordId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ColumnName.Level => @event.Level ?? string.Empty,
            ColumnName.DateAndTime => @event.TimeCreated.Ticks.ToString(CultureInfo.InvariantCulture),
            ColumnName.ActivityId => @event.ActivityId?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty,
            ColumnName.Log => @event.LogName ?? string.Empty,
            ColumnName.ComputerName => @event.ComputerName ?? string.Empty,
            ColumnName.Source => @event.Source ?? string.Empty,
            ColumnName.EventId => @event.Id.ToString(CultureInfo.InvariantCulture),
            ColumnName.TaskCategory => @event.TaskCategory ?? string.Empty,
            ColumnName.Keywords => @event.KeywordsDisplayName ?? string.Empty,
            ColumnName.ProcessId => @event.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ColumnName.ThreadId => @event.ThreadId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ColumnName.User => @event.UserId?.Value ?? string.Empty,
            _ => string.Empty
        };
}
