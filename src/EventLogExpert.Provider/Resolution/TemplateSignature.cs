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
        var leftBuffer = new ArrayBufferWriter<byte>();
        var rightBuffer = new ArrayBufferWriter<byte>();

        AppendTo(leftBuffer, left);
        AppendTo(rightBuffer, right);

        return leftBuffer.WrittenSpan.SequenceEqual(rightBuffer.WrittenSpan);
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
