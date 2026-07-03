// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;
using System.Buffers;
using System.Buffers.Binary;

namespace EventLogExpert.Provider.Database.Hashing;

// Canonical bytes must match merge equivalence so identical rendered providers share one VersionKey.
internal static class ProviderContentEncoder
{
    // Bump after canonicalization changes to re-key every provider deterministically.
    private const byte SchemeVersion = 1;

    internal static byte[] Encode(ProviderDetails provider)
    {
        var buffer = new ArrayBufferWriter<byte>();

        WriteByte(buffer, SchemeVersion);

        // Merge treats null and empty owners as equivalent, so hash them as one value.
        WriteString(buffer, string.IsNullOrEmpty(provider.ResolvedFromOwningPublisher) ? null : provider.ResolvedFromOwningPublisher);
        WriteSortedBlobs(buffer, provider.Events, EncodeEvent);
        WriteSortedBlobs(buffer, provider.Messages, EncodeMessage);
        WriteSortedBlobs(buffer, provider.Parameters, EncodeMessage);
        WriteInt64Dictionary(buffer, provider.Keywords);
        WriteInt32Dictionary(buffer, provider.Opcodes);
        WriteInt32Dictionary(buffer, provider.Tasks);
        WriteMaps(buffer, provider.Maps);

        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeEvent(EventModel model)
    {
        var buffer = new ArrayBufferWriter<byte>();

        WriteInt64(buffer, model.Id);
        WriteByte(buffer, model.Version);
        WriteInt32(buffer, model.Level);
        WriteInt32(buffer, model.Opcode);
        WriteInt32(buffer, model.Task);

        // Merge treats keywords as a set, so ordering and duplicate rows must not affect the hash.
        var keywords = model.Keywords.Distinct().Order().ToArray();
        WriteInt32(buffer, keywords.Length);

        foreach (var keyword in keywords) { WriteInt64(buffer, keyword); }

        TemplateSignature.AppendTo(buffer, model.Template.AsSpan());
        WriteString(buffer, model.Description);
        WriteString(buffer, model.LogName);

        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeMessage(MessageModel model)
    {
        var buffer = new ArrayBufferWriter<byte>();

        WriteUInt16(buffer, (ushort)model.ShortId);
        WriteInt64(buffer, model.RawId);

        // ProviderName mirrors the excluded owner name; hashing it would split equivalent provider recordings.
        WriteString(buffer, model.LogLink);
        WriteString(buffer, model.Tag);
        WriteString(buffer, model.Template);
        WriteString(buffer, model.Text);

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteByte(ArrayBufferWriter<byte> buffer, byte value)
    {
        var span = buffer.GetSpan(1);
        span[0] = value;
        buffer.Advance(1);
    }

    private static void WriteInt32(ArrayBufferWriter<byte> buffer, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer.GetSpan(sizeof(int)), value);
        buffer.Advance(sizeof(int));
    }

    private static void WriteInt32Dictionary(ArrayBufferWriter<byte> buffer, IDictionary<int, string> dictionary)
    {
        var keys = dictionary.Keys.Order().ToArray();
        WriteInt32(buffer, keys.Length);

        foreach (var key in keys)
        {
            WriteInt32(buffer, key);
            WriteString(buffer, dictionary[key]);
        }
    }

    private static void WriteInt64(ArrayBufferWriter<byte> buffer, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(buffer.GetSpan(sizeof(long)), value);
        buffer.Advance(sizeof(long));
    }

    private static void WriteInt64Dictionary(ArrayBufferWriter<byte> buffer, IDictionary<long, string> dictionary)
    {
        var keys = dictionary.Keys.Order().ToArray();
        WriteInt32(buffer, keys.Length);

        foreach (var key in keys)
        {
            WriteInt64(buffer, key);
            WriteString(buffer, dictionary[key]);
        }
    }

    private static void WriteMaps(ArrayBufferWriter<byte> buffer, IReadOnlyDictionary<string, ValueMapDefinition> maps)
    {
        var keys = maps.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        WriteInt32(buffer, keys.Length);

        foreach (var key in keys)
        {
            var map = maps[key];

            WriteString(buffer, key);
            WriteByte(buffer, map.IsBitMap ? (byte)1 : (byte)0);

            // ValueMap entry order is semantic for bitmap decoding and merge equality, so do not sort it.
            WriteInt32(buffer, map.Entries.Count);

            foreach (var entry in map.Entries)
            {
                WriteUInt32(buffer, entry.Value);
                WriteString(buffer, entry.Name);
            }
        }
    }

    private static void WriteSortedBlobs<T>(ArrayBufferWriter<byte> buffer, IReadOnlyList<T> items, Func<T, byte[]> encode)
    {
        // Framed blobs sort ordinally and de-duplicate so source order and duplicate rows do not affect the hash.
        var blobs = new List<byte[]>(items.Count);

        foreach (var item in items) { blobs.Add(encode(item)); }

        blobs.Sort(static (left, right) => left.AsSpan().SequenceCompareTo(right));

        var distinctCount = 0;

        for (var index = 0; index < blobs.Count; index++)
        {
            if (index > 0 && blobs[index].AsSpan().SequenceEqual(blobs[index - 1])) { continue; }

            distinctCount++;
        }

        WriteInt32(buffer, distinctCount);

        for (var index = 0; index < blobs.Count; index++)
        {
            if (index > 0 && blobs[index].AsSpan().SequenceEqual(blobs[index - 1])) { continue; }

            buffer.Write(blobs[index]);
        }
    }

    private static void WriteString(ArrayBufferWriter<byte> buffer, string? value)
    {
        if (value is null)
        {
            // The -1 marker keeps null distinct from empty for nullable rendered fields.
            WriteInt32(buffer, -1);

            return;
        }

        var byteCount = value.Length * sizeof(char);
        WriteInt32(buffer, byteCount);

        if (byteCount == 0) { return; }

        Span<byte> destination = buffer.GetSpan(byteCount);

        for (var index = 0; index < value.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[(index * sizeof(char))..], value[index]);
        }

        buffer.Advance(byteCount);
    }

    private static void WriteUInt16(ArrayBufferWriter<byte> buffer, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.GetSpan(sizeof(ushort)), value);
        buffer.Advance(sizeof(ushort));
    }

    private static void WriteUInt32(ArrayBufferWriter<byte> buffer, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.GetSpan(sizeof(uint)), value);
        buffer.Advance(sizeof(uint));
    }
}
