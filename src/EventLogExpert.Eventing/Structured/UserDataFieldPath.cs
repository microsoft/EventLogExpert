// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Eventing.Structured;

/// <summary>
///     Public boundary for UserData canonical paths (rooted at <c>Event/UserData/</c>). <see cref="TryNormalize" />
///     roots and validates a discovered or user-typed path so a malformed or unrooted path is rejected once, before any
///     lowering or extraction.
/// </summary>
public static class UserDataFieldPath
{
    public const string RootPrefix = "Event/UserData/";

    /// <summary>Validates an already-canonical (Event-rooted) path without re-rooting it.</summary>
    public static bool IsValid(string canonicalPath) => StructuredFieldPath.IsValidCanonical(canonicalPath);

    /// <summary>
    ///     Maps a canonical UserData filter path to its storage key on a resolved event, via the shared
    ///     <see cref="StructuredFieldPath.ToStorageKey" /> so the compiled filter and the stored fields agree on keys.
    /// </summary>
    public static string ToStorageKey(string canonicalPath) => StructuredFieldPath.ToStorageKey(canonicalPath);

    /// <summary>
    ///     Roots <paramref name="input" /> at <see cref="RootPrefix" /> and validates it. Returns the canonical
    ///     Event-rooted path on success, or an error describing why the path is invalid.
    /// </summary>
    public static bool TryNormalize(
        string? input,
        [NotNullWhen(true)] out string? canonical,
        [NotNullWhen(false)] out string? error)
    {
        canonical = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "A UserData path is required.";

            return false;
        }

        string rooted = Root(input.Trim());

        if (!StructuredFieldPath.IsValidCanonical(rooted))
        {
            error = $"'{input.Trim()}' is not a valid UserData path.";

            return false;
        }

        canonical = rooted;

        return true;
    }

    private static string Root(string path)
    {
        // Strip only a full Event/UserData/ envelope so Root is the exact inverse of StructuredFieldPath.ToStorageKey
        // and store keys round-trip; a bare Event/ or UserData/ is content, not the root, and is re-rooted verbatim.
        string rest = path.StartsWith(RootPrefix, StringComparison.Ordinal) ? path[RootPrefix.Length..] : path;

        return RootPrefix + rest;
    }
}
