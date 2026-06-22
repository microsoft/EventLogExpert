// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Resolution;

public readonly record struct ValueMapEntry(uint Value, string Name);

/// <summary>
///     Decodes a numeric event property to its manifest display text: a valueMap is an exact-match enum, a bitMap is
///     the comma-joined names of every set flag.
/// </summary>
public sealed class ValueMapDefinition(bool isBitMap, IReadOnlyList<ValueMapEntry> entries)
{
    private readonly IReadOnlyList<ValueMapEntry> _entries = entries ?? [];

    public IReadOnlyList<ValueMapEntry> Entries => _entries;

    public bool IsBitMap { get; } = isBitMap;

    public bool TryDecode(object? value, out string decoded)
    {
        decoded = string.Empty;

        return TryGetUnsignedBits(value, out ulong bits) && TryDecodeBits(bits, out decoded);
    }

    public bool TryDecodeBits(ulong bits, out string decoded)
    {
        decoded = string.Empty;

        if (_entries.Count == 0)
        {
            return false;
        }

        return IsBitMap ? TryDecodeBitMap(bits, out decoded) : TryDecodeValueMap(bits, out decoded);
    }

    private static bool TryGetUnsignedBits(object? value, out ulong bits)
    {
        switch (value)
        {
            case byte byteValue: bits = byteValue; return true;
            case sbyte sbyteValue: bits = (byte)sbyteValue; return true;
            case ushort ushortValue: bits = ushortValue; return true;
            case short shortValue: bits = (ushort)shortValue; return true;
            case uint uintValue: bits = uintValue; return true;
            case int intValue: bits = (uint)intValue; return true;
            case ulong ulongValue: bits = ulongValue; return true;
            case long longValue: bits = (ulong)longValue; return true;
            default: bits = 0; return false;
        }
    }

    private bool TryDecodeBitMap(ulong bits, out string decoded)
    {
        if (bits == 0)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Value == 0)
                {
                    decoded = _entries[i].Name;

                    return true;
                }
            }

            decoded = string.Empty;

            return false;
        }

        int matchedCount = 0;
        int totalLength = 0;
        string firstMatch = string.Empty;

        for (int i = 0; i < _entries.Count; i++)
        {
            ValueMapEntry entry = _entries[i];

            // Zero-valued flags only apply when the input itself is zero; skip them in the OR-test.
            if (entry.Value == 0 || (bits & entry.Value) != entry.Value)
            {
                continue;
            }

            if (matchedCount == 0)
            {
                firstMatch = entry.Name;
            }
            else
            {
                totalLength++; // separator before every matched name after the first
            }

            totalLength += entry.Name.Length;
            matchedCount++;
        }

        if (matchedCount == 0)
        {
            decoded = string.Empty;

            return false;
        }

        if (matchedCount == 1)
        {
            decoded = firstMatch;

            return true;
        }

        decoded = string.Create(totalLength, (self: this, bits), static (span, state) =>
        {
            IReadOnlyList<ValueMapEntry> entries = state.self._entries;
            int position = 0;
            int matched = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                ValueMapEntry entry = entries[i];

                if (entry.Value == 0 || (state.bits & entry.Value) != entry.Value)
                {
                    continue;
                }

                // Separate by match index, not buffer position: an empty leading Name leaves position at 0.
                if (matched > 0)
                {
                    span[position++] = ',';
                }

                entry.Name.CopyTo(span[position..]);
                position += entry.Name.Length;
                matched++;
            }
        });

        return true;
    }

    private bool TryDecodeValueMap(ulong bits, out string decoded)
    {
        foreach (ValueMapEntry entry in _entries)
        {
            if (entry.Value == bits)
            {
                decoded = entry.Name;

                return true;
            }
        }

        decoded = string.Empty;

        return false;
    }
}
