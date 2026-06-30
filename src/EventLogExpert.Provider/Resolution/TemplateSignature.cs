// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Binary;

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
        // Streaming comparison mirrors AppendTo's framed UTF-16LE encoding without allocating hot-path buffers.
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

        if (left.IsRaw) { return left.Raw.SequenceEqual(right.Raw); }

        return left.Name.SequenceEqual(right.Name) &&
            left.InType.SequenceEqual(right.InType) &&
            left.OutType.SequenceEqual(right.OutType) &&
            left.Length.SequenceEqual(right.Length) &&
            left.Map.SequenceEqual(right.Map);
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
        int byteCount = value.Length * sizeof(char);
        WriteInt32(buffer, byteCount);

        if (byteCount == 0) { return; }

        Span<byte> destination = buffer.GetSpan(byteCount);

        for (var index = 0; index < value.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[(index * sizeof(char))..], value[index]);
        }

        buffer.Advance(byteCount);
    }
}
