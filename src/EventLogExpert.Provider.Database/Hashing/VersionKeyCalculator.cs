// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;
using System.Security.Cryptography;
using System.Text;

namespace EventLogExpert.ProviderDatabase.Hashing;

// Version keys hash canonical rendering payloads so identical providers collapse and divergent ones coexist.
public static class VersionKeyCalculator
{
    // Prefix versions the entire canonicalization scheme; bump on incompatible key changes.
    public const string SchemePrefix = "vk1:";

    public static string Compute(ProviderDetails provider)
    {
        var canonical = ProviderContentEncoder.Encode(provider);
        var hash = SHA256.HashData(canonical);

        return SchemePrefix + ToBase32Lower(hash);
    }

    // Lowercase RFC 4648 base32 avoids padding and case-sensitive filename/ordinal surprises.
    private static string ToBase32Lower(ReadOnlySpan<byte> data)
    {
        const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var accumulator = 0;
        var bitsBuffered = 0;

        foreach (var value in data)
        {
            accumulator = (accumulator << 8) | value;
            bitsBuffered += 8;

            while (bitsBuffered >= 5)
            {
                bitsBuffered -= 5;
                output.Append(Alphabet[(accumulator >> bitsBuffered) & 0x1F]);
            }

            accumulator &= (1 << bitsBuffered) - 1;
        }

        if (bitsBuffered > 0)
        {
            output.Append(Alphabet[(accumulator << (5 - bitsBuffered)) & 0x1F]);
        }

        return output.ToString();
    }
}
