// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Eventing.Common.Events;

public enum EventDataKind : byte
{
    None,
    EventData
}

/// <summary>
///     Named/positional access to an event's structured &lt;EventData&gt; fields. A lightweight view materialized on
///     demand from the two references retained on <see cref="ResolvedEvent" />; it is not itself retained.
///     <see cref="Kind" /> is <see cref="EventDataKind.None" /> for legacy, template-less, or fail-closed events, whose
///     values remain available positionally through the event description and XML.
/// </summary>
public readonly struct EventDataView
{
    public static readonly EventDataView Empty = default;

    private readonly ImmutableArray<EventProperty> _values;
    private readonly TemplateFieldSchema? _schema;

    internal EventDataView(ImmutableArray<EventProperty> values, TemplateFieldSchema? schema)
    {
        _values = values;
        _schema = schema;
    }

    public EventDataKind Kind => _values.IsDefaultOrEmpty ? EventDataKind.None : EventDataKind.EventData;

    public int Count => _values.IsDefaultOrEmpty ? 0 : _values.Length;

    public bool TryGetValue(string? fieldName, out EventFieldValue value)
    {
        if (fieldName is not null
            && TryGetOrdering(out FieldNameOrdering ordering)
            && _schema.TryGetIndex(ordering, fieldName, out int index)
            && (uint)index < (uint)_values.Length)
        {
            value = EventFieldValue.FromProperty(_values[index]);

            return true;
        }

        value = default;

        return false;
    }

    internal bool TryGetRawValue(string? fieldName, out EventProperty property)
    {
        if (fieldName is not null
            && TryGetOrdering(out FieldNameOrdering ordering)
            && _schema.TryGetIndex(ordering, fieldName, out int index)
            && (uint)index < (uint)_values.Length)
        {
            property = _values[index];

            return true;
        }

        property = default;

        return false;
    }

    public bool TryGetName(int index, out string name)
    {
        if (TryGetOrdering(out FieldNameOrdering ordering))
        {
            ImmutableArray<string> names = OrderingNames(ordering);

            if ((uint)index < (uint)names.Length)
            {
                name = names[index];

                return true;
            }
        }

        name = string.Empty;

        return false;
    }

    public Enumerator GetEnumerator() => new(this);

    private ImmutableArray<string> OrderingNames(FieldNameOrdering ordering) =>
        ordering == FieldNameOrdering.Visible ? _schema!.VisibleNames : _schema!.AllNames;

    [MemberNotNullWhen(true, nameof(_schema))]
    private bool TryGetOrdering(out FieldNameOrdering ordering)
    {
        if (_schema is not null && !_values.IsDefaultOrEmpty)
        {
            if (_values.Length == _schema.VisibleNames.Length) { ordering = FieldNameOrdering.Visible; return true; }

            if (_values.Length == _schema.AllNames.Length) { ordering = FieldNameOrdering.All; return true; }
        }

        ordering = default;

        return false;
    }

    public readonly record struct Field(string Name, EventFieldValue Value);

    public struct Enumerator
    {
        private readonly ImmutableArray<EventProperty> _values;
        private readonly ImmutableArray<string> _names;
        private int _index;

        internal Enumerator(in EventDataView view)
        {
            if (view.TryGetOrdering(out FieldNameOrdering ordering))
            {
                _values = view._values;
                _names = view.OrderingNames(ordering);
            }
            else
            {
                _values = ImmutableArray<EventProperty>.Empty;
                _names = ImmutableArray<string>.Empty;
            }

            _index = -1;
        }

        public readonly Field Current => new(_names[_index], EventFieldValue.FromProperty(_values[_index]));

        public bool MoveNext()
        {
            int next = _index + 1;

            // Bounded to the aligned name/value pair (equal lengths by the schema lockstep invariant).
            if (next < _values.Length && next < _names.Length)
            {
                _index = next;

                return true;
            }

            return false;
        }
    }
}
