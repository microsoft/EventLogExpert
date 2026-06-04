// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

/// <summary>Build-time / static-initialization flag gating the backslash-name-to-tag migration path.</summary>
/// <remarks>
///     <para>
///         When <see cref="Enabled" /> is <see langword="true" /> (default), the real
///         <see cref="BackslashNameMigrator" /> runs at startup, <see cref="LegacyFilterMigrator" /> and
///         <see cref="FilterLibraryExportService" /> auto-convert backslash names to tags during legacy and user-initiated
///         imports, and <see cref="Effects" /> dedup uses migration-aware matching. When <see langword="false" />, the
///         migration path is inert and import boundaries reject entries whose names contain <c>\</c>.
///     </para>
///     <para>
///         Staged-deletion contract: every <c>if (IsEnabled) { ... } else { ... }</c> wrapping introduced for this feature
///         places the deletable migration code in the <c>then</c> branch and the permanent post-deletion behaviour in the
///         <c>else</c> branch. The mechanical removal procedure is:
///         <list type="number">
///             <item>Flip <see cref="Enabled" /> to <see langword="false" /> as a smoke test.</item>
///             <item>
///                 Delete every <c>if (BackslashMigrationFeature.IsEnabled) { ... } else { ... }</c> wrapper, keeping
///                 the <c>else</c> branch contents only.
///             </item>
///             <item>
///                 Delete: <see cref="IBackslashNameMigrator" />, <see cref="BackslashNameMigrator" />,
///                 <see cref="NoOpBackslashNameMigrator" />, <see cref="LibraryEntryTagNormalizer.MigrateBackslashName" />
///                 , the existing-side matching projections that call it (
///                 <see cref="Effects.DedupMigrationEntriesAgainstExisting" /> and
///                 <see cref="FilterLibraryExportService.ComputePreflight" />), the conditional DI selection in
///                 <see cref="FilterLibraryServiceCollectionExtensions" />, and the conditional <c>ILegacyPreferences</c>
///                 branch in <c>MauiProgram</c>.
///             </item>
///             <item>Delete this file.</item>
///         </list>
///     </para>
/// </remarks>
public static class BackslashMigrationFeature
{
    public static readonly bool Enabled = true;

    private static readonly AsyncLocal<bool?> s_testOverride = new();

    internal static bool IsEnabled => s_testOverride.Value ?? Enabled;

    internal static IDisposable Override(bool value)
    {
        s_testOverride.Value = value;

        return new OverrideScope();
    }

    private sealed class OverrideScope : IDisposable
    {
        public void Dispose() => s_testOverride.Value = null;
    }
}
