// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Events;

/// <summary>
///     A ref-struct enumerator that yields an event's &lt;EventData&gt; fields POSITIONALLY as
///     <see cref="EventDataView.Field" /> pairs - every position, including duplicate names and positional-empty nodes
///     (the wildcard-name filter arms are existential over this iteration, so a dropped duplicate would silently miss a
///     match). It supports two source modes behind one type because <see cref="EventColumnStoreReader" /> reads a pending
///     <see cref="ResolvedEvent" /> or the sealed columns: <em>view mode</em> delegates to the
///     <see cref="EventDataView.Enumerator" /> for a pending event's whole <see cref="ResolvedEvent" />;
///     <em>columns mode</em> reconstructs each field from the store columns. It is a ref struct because columns mode holds
///     a <see cref="ReadOnlySpan{T}" /> of schema name indices and reconstructs through the internal
///     <see cref="RawEventDataField" /> ref struct, so it never escapes to the heap on the all-N filter hot path.
/// </summary>
public ref struct EventDataFieldEnumerator
{
    private readonly bool _isView;
    private readonly EventColumnStore? _store;
    private readonly int _index;
    private readonly ReadOnlySpan<int> _nameIndices;
    private readonly int _count;

    private EventDataView.Enumerator _viewEnumerator;
    private int _position;

    internal EventDataFieldEnumerator(EventDataView view)
    {
        _isView = true;
        _viewEnumerator = view.GetEnumerator();
        _store = null;
        _index = 0;
        _nameIndices = default;
        _count = 0;
        _position = -1;
    }

    internal EventDataFieldEnumerator(EventColumnStore store, int index)
    {
        int schemaId = store.RawEventDataSchemaId(index);

        _isView = false;
        _viewEnumerator = default;
        _store = store;
        _index = index;
        _nameIndices = schemaId < 0 ? default : store.SchemaFieldNameIndices(schemaId);
        _count = schemaId < 0 ? 0 : store.RawEventDataCount(index);
        _position = -1;
    }

    public readonly EventDataView.Field Current => _isView
        ? _viewEnumerator.Current
        : new EventDataView.Field(
            _store!.PoolGet(_nameIndices[_position]) ?? string.Empty,
            EventFieldValue.FromProperty(_store.ReconstructEventProperty(_store.RawEventDataField(_index, _position))));

    public readonly EventDataFieldEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        if (_isView) { return _viewEnumerator.MoveNext(); }

        // Bounded to the aligned name/value pair, mirroring EventDataView.Enumerator's schema-lockstep bound.
        return ++_position < _count && _position < _nameIndices.Length;
    }
}
