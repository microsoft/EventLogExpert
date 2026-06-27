// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace EventLogExpert.Provider.Resolution;

/// <summary>
///     Canonical byte encoding of a template's render-relevant fields; the content hash and the merge compare
///     templates by this same encoding (insensitive to whitespace, attribute order, and serialization) so they cannot
///     drift.
/// </summary>
public static class TemplateSignature
{
    private const byte ParsedNode = 0;

    private const byte RawNode = 1;

    public static void AppendTo(IBufferWriter<byte> buffer, ReadOnlySpan<char> template)
    {
        var counter = new TemplateFieldReader(template);
        int count = 0;

        while (counter.MoveNext()) { count++; }

        WriteInt32(buffer, count);

        foreach (TemplateField field in new TemplateFieldReader(template))
        {
            if (field.IsRaw)
            {
                WriteByte(buffer, RawNode);
                WriteString(buffer, field.Raw);

                continue;
            }

            WriteByte(buffer, ParsedNode);
            WriteString(buffer, field.Name);
            WriteString(buffer, field.InType);
            WriteString(buffer, field.OutType);
            WriteString(buffer, field.Length);
            WriteString(buffer, field.Map);
        }
    }

    public static bool Equal(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        // Streaming equivalent of comparing the two AppendTo encodings: the byte encoding writes a node count followed by
        // each node's framed UTF-8 fields, so two templates are equal iff they yield the same nodes in order with the same
        // per-field UTF-8 bytes. Streaming the ref-struct readers in lockstep with an equal-fields fast path avoids the two
        // intermediate byte buffers AppendTo+SequenceEqual allocated on the merge hot path (EventsAreEquivalent).
        var leftReader = new TemplateFieldReader(left);
        var rightReader = new TemplateFieldReader(right);

        while (true)
        {
            bool leftMoved = leftReader.MoveNext();
            bool rightMoved = rightReader.MoveNext();

            // A differing node count (the int32 the buffer encoding writes first) fails fast.
            if (leftMoved != rightMoved) { return false; }

            if (!leftMoved) { return true; }

            if (!FieldsEqual(leftReader.Current, rightReader.Current)) { return false; }
        }
    }

    private static bool FieldsEqual(TemplateField left, TemplateField right)
    {
        if (left.IsRaw != right.IsRaw) { return false; }

        if (left.IsRaw) { return Utf8Equal(left.Raw, right.Raw); }

        return Utf8Equal(left.Name, right.Name) &&
            Utf8Equal(left.InType, right.InType) &&
            Utf8Equal(left.OutType, right.OutType) &&
            Utf8Equal(left.Length, right.Length) &&
            Utf8Equal(left.Map, right.Map);
    }

    // Compares two field spans by the SAME UTF-8 encoding AppendTo's WriteString uses, so merge equality stays byte-exact
    // with the content hash and the two cannot drift - including for malformed UTF-16, which UTF-8 collapses to the
    // replacement character. Identical UTF-16 always encodes identically (the allocation-free hot path); only differing
    // spans of equal encoded length fall through to the byte-level check.
    private static bool Utf8Equal(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        if (left.SequenceEqual(right)) { return true; }

        int byteCount = Encoding.UTF8.GetByteCount(left);

        if (byteCount != Encoding.UTF8.GetByteCount(right)) { return false; }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount * 2);

        try
        {
            Span<byte> leftBytes = buffer.AsSpan(0, byteCount);
            Span<byte> rightBytes = buffer.AsSpan(byteCount, byteCount);
            Encoding.UTF8.GetBytes(left, leftBytes);
            Encoding.UTF8.GetBytes(right, rightBytes);

            return leftBytes.SequenceEqual(rightBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void WriteByte(IBufferWriter<byte> buffer, byte value)
    {
        Span<byte> span = buffer.GetSpan(1);
        span[0] = value;
        buffer.Advance(1);
    }

    private static void WriteInt32(IBufferWriter<byte> buffer, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer.GetSpan(sizeof(int)), value);
        buffer.Advance(sizeof(int));
    }

    private static void WriteString(IBufferWriter<byte> buffer, ReadOnlySpan<char> value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(buffer, byteCount);

        if (byteCount == 0) { return; }

        Encoding.UTF8.GetBytes(value, buffer.GetSpan(byteCount));
        buffer.Advance(byteCount);
    }
}
