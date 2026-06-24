// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Resolution;

/// <summary>
///     Identity of a provider row: its name (case-insensitive, matching the database's NOCASE primary key) paired
///     with its content <see cref="ProviderDetails.VersionKey" /> (ordinal). Two rows share an identity only when both the
///     case-folded name and the version key match, so case-only name duplicates collapse while genuinely different
///     versions of the same provider are kept apart. This is the single identity type used wherever provider rows are
///     deduplicated, skipped, or deleted by identity.
/// </summary>
/// <remarks>
///     Equality is overridden because the default record-struct equality compares <see cref="ProviderName" />
///     ordinally (case-sensitive), which disagrees with the database's NOCASE key. Equality and hashing are null-safe, so
///     a <c>default</c> instance (null members) is inert rather than a crash hazard; construct real identities through the
///     primary constructor or <see cref="Of" />.
/// </remarks>
public readonly record struct ProviderIdentity(string ProviderName, string VersionKey)
{
    public bool Equals(ProviderIdentity other) =>
        string.Equals(ProviderName, other.ProviderName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(VersionKey, other.VersionKey, StringComparison.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(
            ProviderName?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0,
            VersionKey?.GetHashCode(StringComparison.Ordinal) ?? 0);

    /// <summary>Extracts the <see cref="ProviderIdentity" /> of a provider row.</summary>
    public static ProviderIdentity Of(ProviderDetails provider) => new(provider.ProviderName, provider.VersionKey);
}
