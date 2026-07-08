// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Events;

/// <summary>
///     The stored representation of a single &lt;EventData&gt; field value inside an <see cref="EventColumnChunk" />.
///     One combined discriminator replaces the two-part (property-kind + reference-sub-kind) sketch: it names both the
///     packed numeric shapes and the reference shapes so extraction can round-trip every value the reader produces, and
///     never throws on an unexpected shape (which degrades to <see cref="StringForm" />).
/// </summary>
internal enum StoredFieldKind : byte
{
    // Packed: the value is stored in the field's long edBits slot (bit / ToBinary round-trip).
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Boolean,
    DateTime,
    SizeT,

    // Pooled string: the value is a pool index in the field's edRef slot.
    String,
    Sid,

    // Blittable: the value is a byte range in a chunk-local byte column; the element type is implied by the kind.
    Guid,
    Bytes,
    UInt16Array,
    UInt32Array,
    Int32Array,

    // Interned value list: the value is a range into a chunk-local list of pooled-value indices.
    StringArray,

    // Fallback for an unexpected (effectively unreachable) reference shape: the value is the pool index of its string form.
    StringForm,

    Null
}
