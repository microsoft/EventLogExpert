// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace EventLogExpert.Provider.Resolution;

// Hashing and merging use this same render-relevant encoding so template equivalence cannot drift.
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
        // Streaming comparison mirrors AppendTo's framed UTF-8 encoding without allocating hot-path buffers.
        var leftReader = new TemplateFieldReader(left);
        var rightReader = new TemplateFieldReader(right);

        while (true)
        {
            bool leftMoved = leftReader.MoveNext();
            bool rightMoved = rightReader.MoveNext();

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

    // Compares spans by AppendTo's UTF-8 bytes so malformed UTF-16 matches hash semantics.
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
