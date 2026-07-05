// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using System.Collections.Immutable;
using System.Security;
using System.Security.Principal;

namespace EventLogExpert.Eventing.TestUtils;

/// <summary>
///     Builds <see cref="ResolvedEvent" /> instances carrying structured &lt;EventData&gt; named fields for filter
///     and view tests. The field value's CLR type determines the resulting <see cref="EventFieldValueKind" /> (e.g.
///     <see cref="string" /> to String, <see cref="long" /> to Int64, <see cref="Guid" /> to Guid). Field name order is
///     preserved; duplicate names exercise the schema's first-wins lookup.
/// </summary>
public static class EventDataTestFactory
{
    public static ResolvedEvent CreateEventWithData(params (string Name, object? Value)[] fields) =>
        new ResolvedEvent("TestLog", LogPathType.Channel).WithEventData(fields);

    public static ResolvedEvent WithEventData(this ResolvedEvent source, params (string Name, object? Value)[] fields)
    {
        var template = "<template>"
            + string.Concat(fields.Select(field => $"<data name=\"{SecurityElement.Escape(field.Name)}\"/>"))
            + "</template>";

        var schema = new TemplateAnalyzer().GetTemplateInfo(template).Schema;

        var builder = ImmutableArray.CreateBuilder<EventProperty>(fields.Length);

        foreach (var (_, value) in fields)
        {
            builder.Add(ToEventProperty(value));
        }

        return source with { EventDataValues = builder.MoveToImmutable(), EventDataSchema = schema };
    }

    private static EventProperty ToEventProperty(object? value) =>
        value switch
        {
            null => EventProperty.FromReference(null),
            string text => text,
            bool boolean => boolean,
            long int64 => int64,
            int int32 => int32,
            ulong uint64 => uint64,
            uint uint32 => uint32,
            double doubleValue => doubleValue,
            float single => single,
            DateTime dateTime => dateTime,
            Guid guid => guid,
            SecurityIdentifier sid => sid,
            byte[] bytes => bytes,
            string[] strings => strings,
            _ => value.ToString() ?? string.Empty
        };
}
