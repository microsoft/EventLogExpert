// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Resolution;

/// <summary>
///     Orders two providers by source recency (newest first): OS build, then update revision (UBR), then the newest
///     message-DLL file version. Component-wise lexicographic - at each level a present value outranks a null/unparseable
///     one, and equal/absent values fall through to the next. Shared by <see cref="ProviderVersionSelector" /> (the
///     resolve-time tiebreak) and <c>ProviderDetailsMerger</c> (the deterministic provenance carry when collapsing
///     duplicates) so the two paths can never disagree on which source is newer.
/// </summary>
public static class ProviderSourceRecency
{
    /// <summary>
    ///     Returns &gt; 0 when <paramref name="candidate" /> is the newer source, &lt; 0 when <paramref name="current" />
    ///     is, and 0 when neither can be ordered (all provenance absent or equal - the caller then keeps its own stable
    ///     order).
    /// </summary>
    public static int Compare(ProviderDetails candidate, ProviderDetails current)
    {
        var byBuild = CompareNullableInt(candidate.SourceOsBuild, current.SourceOsBuild);

        if (byBuild != 0) { return byBuild; }

        var byRevision = CompareNullableInt(candidate.SourceOsRevision, current.SourceOsRevision);

        if (byRevision != 0) { return byRevision; }

        return CompareFileVersion(candidate.MessageFileVersion, current.MessageFileVersion);
    }

    private static int CompareFileVersion(string? candidate, string? current)
    {
        var candidateVersion = TryParseVersion(candidate);
        var currentVersion = TryParseVersion(current);

        if (candidateVersion is not null && currentVersion is not null)
        {
            return candidateVersion.CompareTo(currentVersion);
        }

        if (candidateVersion is not null) { return 1; }

        return currentVersion is not null ? -1 : 0;
    }

    private static int CompareNullableInt(int? candidate, int? current)
    {
        if (candidate.HasValue && current.HasValue) { return candidate.Value.CompareTo(current.Value); }

        if (candidate.HasValue) { return 1; }

        return current.HasValue ? -1 : 0;
    }

    private static Version? TryParseVersion(string? value) => Version.TryParse(value, out var version) ? version : null;
}
