// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Resolution;

/// <summary>
///     Identity of a provider row: its name (case-insensitive over ASCII only, matching the database's NOCASE primary
///     key) paired with its content <see cref="ProviderDetails.VersionKey" /> (ordinal). Two rows share an identity only
///     when both the ASCII-case-folded name and the version key match, so case-only ASCII name duplicates collapse while
///     names that differ by non-ASCII case (which SQLite NOCASE keeps distinct) and genuinely different versions are kept
///     apart. This is the single identity type used wherever provider rows are deduplicated, skipped, or deleted by
///     identity.
/// </summary>
/// <remarks>
///     Equality matches the database's <c>NOCASE</c> collation EXACTLY: NOCASE folds only ASCII A-Z/a-z, so an
///     in-memory identity that used full-Unicode case folding (e.g. <see cref="StringComparison.OrdinalIgnoreCase" />)
///     would treat two distinct primary-key rows as one and desynchronize dedup/skip/delete from what the composite key
///     enforces. Equality and hashing are null-safe, so a <c>default</c> instance (null members) is inert rather than a
///     crash hazard; construct real identities through the primary constructor or <see cref="Of" />.
/// </remarks>
public readonly record struct ProviderIdentity(string ProviderName, string VersionKey)
{
    public bool Equals(ProviderIdentity other) =>
        NameEquals(ProviderName, other.ProviderName) &&
        string.Equals(VersionKey, other.VersionKey, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var character in ProviderName ?? string.Empty)
        {
            hash.Add(AsciiToUpper(character));
        }

        hash.Add(VersionKey is null ? 0 : VersionKey.GetHashCode(StringComparison.Ordinal));

        return hash.ToHashCode();
    }

    /// <summary>Extracts the <see cref="ProviderIdentity" /> of a provider row.</summary>
    public static ProviderIdentity Of(ProviderDetails provider) => new(provider.ProviderName, provider.VersionKey);

    // SQLite's NOCASE collation folds ONLY ASCII A-Z/a-z (characters with diacritics stay distinct), so fold the name
    // the same way here. Hashing folds identically, so equal names always produce equal hash codes.
    private static bool NameEquals(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) { return true; }

        if (left is null || right is null || left.Length != right.Length) { return false; }

        for (var index = 0; index < left.Length; index++)
        {
            if (AsciiToUpper(left[index]) != AsciiToUpper(right[index])) { return false; }
        }

        return true;
    }

    private static char AsciiToUpper(char character) =>
        character is >= 'a' and <= 'z' ? (char)(character - ('a' - 'A')) : character;
}
