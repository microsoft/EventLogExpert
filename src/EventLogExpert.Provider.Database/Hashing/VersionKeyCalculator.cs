// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;
using System.Security.Cryptography;
using System.Text;

namespace EventLogExpert.ProviderDatabase.Hashing;

/// <summary>
///     Computes a provider's content <see cref="ProviderDetails.VersionKey" />: a hash of its canonical rendering
///     payload (<see cref="ProviderContentCanonicalizer" />). Two providers with identical payloads - across machines or
///     OS builds - get the same key and collapse to one database row; genuinely different payloads get different keys and
///     coexist as separate versions of the same provider name. Stamped when a provider is first ingested from a live scan
///     (<c>CreateDatabaseOperation</c>); the merge and diff operations copy already-stamped rows unchanged. The composite
///     <c>(ProviderName, VersionKey)</c> primary key can therefore hold distinct versions of one name.
/// </summary>
public static class VersionKeyCalculator
{
    /// <summary>
    ///     Scheme tag prefixing every computed key. It versions the WHOLE keying scheme (canonicalization + hash +
    ///     encoding); a future change that must re-key providers bumps this to <c>vk2:</c> so old and new keys never silently
    ///     collide. An empty <see cref="ProviderDetails.VersionKey" /> means "not yet stamped" (legacy / pre-hash rows), never
    ///     a real identity.
    /// </summary>
    public const string SchemePrefix = "vk1:";

    public static string Compute(ProviderDetails provider)
    {
        var canonical = ProviderContentCanonicalizer.Canonicalize(provider);
        var hash = SHA256.HashData(canonical);

        return SchemePrefix + ToBase32Lower(hash);
    }

    /// <summary>
    ///     RFC 4648 base32, lowercase, no padding. Chosen over hex (shorter) and base64url (the <c>VersionKey</c> column
    ///     compares ordinally, and an all-lowercase key is case-robust and filename-safe).
    /// </summary>
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
