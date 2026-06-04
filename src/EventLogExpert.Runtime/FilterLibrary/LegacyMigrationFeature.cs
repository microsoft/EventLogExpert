// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

/// <summary>Build-time / static-initialization flag gating the legacy preferences-to-FilterLibrary migration path.</summary>
/// <remarks>
///     <para>
///         When <see cref="Enabled" /> is <see langword="true" /> (default), <c>AddLegacyFilterMigration</c> wires up
///         the real <see cref="LegacyFilterMigrator" /> and the host registers <see cref="ILegacyPreferences" />. When
///         <see langword="false" />, the migrator slot is filled by <see cref="NoOpLegacyFilterMigrator" /> and the host
///         skips the <see cref="ILegacyPreferences" /> registration, leaving the migrator as a structural no-op for
///         existing <c>FilterLibrary.Effects</c> ctor injection.
///     </para>
///     <para>
///         Staged-deletion contract: every <c>if (IsEnabled) { ... } else { ... }</c> wrapping introduced for this feature
///         places the deletable migration code in the <c>then</c> branch and the permanent post-deletion behaviour in the
///         <c>else</c> branch. The mechanical removal procedure is:
///         <list type="number">
///             <item>Flip <see cref="Enabled" /> to <see langword="false" /> as a smoke test.</item>
///             <item>
///                 Delete every <c>if (LegacyMigrationFeature.IsEnabled) { ... } else { ... }</c> wrapper, keeping the
///                 <c>else</c> branch contents only (NoOp registration becomes the sole DI shape).
///             </item>
///             <item>
///                 Delete: <see cref="ILegacyFilterMigrator" />, <see cref="LegacyFilterMigrator" />,
///                 <see cref="NoOpLegacyFilterMigrator" />, <c>ILegacyPreferences</c>, <c>MauiLegacyPreferencesAdapter</c>
///                 , the <c>AddLegacyFilterMigration</c> DI extension, the conditional <c>ILegacyPreferences</c> branch in
///                 <c>MauiProgram</c>, and the <c>legacyMigrator</c> ctor parameter + <c>HandleLoadLibrary</c> migration
///                 branch in <see cref="Effects" />.
///             </item>
///             <item>Delete this file.</item>
///         </list>
///     </para>
/// </remarks>
public static class LegacyMigrationFeature
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
