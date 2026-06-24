// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace EventLogExpert.ProviderDatabase.Hashing;

/// <summary>
///     Produces a canonical, deterministic, culture-invariant byte serialization of a <see cref="ProviderDetails" />
///     rendering payload - the input to the content hash that becomes its <see cref="ProviderDetails.VersionKey" />. The
///     encoding is a hand-written length-prefixed BINARY format (NOT JSON) so the bytes are stable across .NET versions
///     and machines: two providers that render identically hash identically and collapse to one database row, while
///     genuinely different payloads coexist. The provider NAME and the VersionKey itself are excluded (the name is the
///     identity key; the VersionKey is the output).
/// </summary>
/// <remarks>
///     Normalization is aligned with <c>ProviderDetailsMerger</c>'s equivalence so the hash and the merge agree on
///     what makes two providers "the same version": dictionary entries are emitted sorted by key (dictionary enumeration
///     order is unspecified); event keyword lists are sorted AND de-duplicated (the merger compares them as a set); event,
///     message, and parameter entries are encoded to self-delimiting blobs that are sorted ordinally with exact duplicates
///     dropped (manifest list order is not a stability contract); ValueMap entries keep their ORIGINAL order (bitmap
///     decoding is order-dependent, so order is content). Strings are preserved EXACTLY - no Unicode or whitespace
///     normalization - so the hash stays injective over the persisted bytes; the database's fail-hard rule requires that
///     identical hashes imply identical content.
/// </remarks>
internal static class ProviderContentCanonicalizer
{
    /// <summary>
    ///     Bumping this re-keys every provider on purpose (e.g. after a canonicalization fix). Pair it with the
    ///     <c>vk1:</c> tag in <see cref="VersionKeyCalculator" /> so providers hashed under different schemes never silently
    ///     share a key.
    /// </summary>
    private const byte SchemeVersion = 1;

    public static byte[] Canonicalize(ProviderDetails provider)
    {
        var buffer = new ArrayBufferWriter<byte>();

        WriteByte(buffer, SchemeVersion);

        // The merger treats a null and an empty ResolvedFromOwningPublisher as the same "no owner" (IsNullOrEmpty), so
        // normalize them to one value here - otherwise null vs "" would split two otherwise-identical providers.
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

        // The merger treats keywords as a SET (KeywordsEqual -> HashSet.SetEquals), so sort + de-duplicate to match:
        // [1, 1, 2] and [2, 1] must hash identically.
        var keywords = model.Keywords.Distinct().Order().ToArray();
        WriteInt32(buffer, keywords.Length);

        foreach (var keyword in keywords) { WriteInt64(buffer, keyword); }

        WriteString(buffer, model.Template);
        WriteString(buffer, model.Description);
        WriteString(buffer, model.LogName);

        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] EncodeMessage(MessageModel model)
    {
        var buffer = new ArrayBufferWriter<byte>();

        WriteUInt16(buffer, (ushort)model.ShortId);
        WriteInt64(buffer, model.RawId);

        // MessageModel.ProviderName is intentionally omitted: it mirrors the owning provider's name (excluded from the
        // hash) and the merger's message equivalence ignores it, so hashing it would re-inject the provider name and
        // stop two recordings of the same provider whose name differs only by case from collapsing to one row.
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

            // ValueMap entry order is SEMANTIC (TryDecodeBitMap iterates in order; the merger compares via
            // SequenceEqual), so preserve it - do NOT sort.
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
        // Encode each item to a self-delimiting blob, then order the blobs ordinally and drop exact duplicates so the
        // hash is independent of the source list order and of duplicate rows.
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
            // A null string is distinct from an empty one (-1 length marker), so the encoding stays injective across
            // the nullable string fields (Template/Description/LogName/LogLink/Tag).
            WriteInt32(buffer, -1);

            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(buffer, byteCount);

        if (byteCount == 0) { return; }

        Encoding.UTF8.GetBytes(value, buffer.GetSpan(byteCount));
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
