// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Structured;
using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Common.Events;

/// <summary>
///     A ref-struct enumerator that yields an event's nested-UserData fields as <see cref="UserDataFieldEntry" /> pairs,
///     folding the event-level <c>UserDataIncomplete</c> flag into each result's truncation bit exactly as the row
///     oracle's <c>field.ToFieldResult(@event.UserDataIncomplete)</c> does. Like <see cref="EventDataFieldEnumerator" />
///     it supports two source modes behind one type: <em>view mode</em> reads a whole <see cref="ResolvedEvent" />'s
///     UserData (legacy adapter, or the store's pending tail) and <em>columns mode</em> reconstructs each field from the
///     sealed store columns, matching the store's existing <see cref="EventColumnStoreReader.GetUserData" /> point-lookup
///     construction.
/// </summary>
public ref struct UserDataFieldEnumerator
{
    private readonly bool _isView;
    private readonly ImmutableArray<UserDataField> _fields;
    private readonly bool _incomplete;
    private readonly EventColumnStore? _store;
    private readonly int _index;
    private readonly int _count;
    private int _position;

    internal UserDataFieldEnumerator(ImmutableArray<UserDataField> fields, bool incomplete)
    {
        _isView = true;
        _fields = fields;
        _incomplete = incomplete;
        _store = null;
        _index = 0;
        _count = fields.IsDefault ? 0 : fields.Length;
        _position = -1;
    }

    internal UserDataFieldEnumerator(EventColumnStore store, int index)
    {
        _isView = false;
        _fields = default;
        _incomplete = false;
        _store = store;
        _index = index;
        _count = store.RawUserDataCount(index);
        _position = -1;
    }

    public readonly UserDataFieldEntry Current => _isView
        ? new UserDataFieldEntry(_fields[_position].Path, _fields[_position].ToFieldResult(_incomplete))
        : new UserDataFieldEntry(
            _store!.PoolGet(_store.RawUserDataPathIndex(_index, _position))!,
            new StructuredFieldResult(
                EventFieldValue.FromProperty(
                    EventProperty.FromReference(_store.ReconstructStringArray(_store.RawUserDataValues(_index, _position)))),
                _store.RawUserDataTruncated(_index, _position) || _store.RawUserDataIncomplete(_index)));

    public readonly UserDataFieldEnumerator GetEnumerator() => this;

    public bool MoveNext() => ++_position < _count;
}
