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
///         TODO (removal target): once telemetry / release-window confirms the install base has completed legacy migration
///         (typically 2+ release cycles after this commit ships), perform the mechanical removal:
///         <list type="number">
///             <item>Flip <see cref="Enabled" /> to <see langword="false" /> as a smoke test.</item>
///             <item>
///                 Delete: <see cref="ILegacyFilterMigrator" />, <see cref="LegacyFilterMigrator" />,
///                 <see cref="NoOpLegacyFilterMigrator" />, <c>ILegacyPreferences</c>, <c>MauiLegacyPreferencesAdapter</c>
///                 , <c>AddLegacyFilterMigration</c> extension.
///             </item>
///             <item>
///                 Strip the conditional <c>ILegacyPreferences</c> registration in <c>MauiProgram</c> and the
///                 <c>legacyMigrator</c> ctor parameter + <c>HandleLoadLibrary</c> migration branch in
///                 <see cref="Effects" />.
///             </item>
///             <item>Delete this file.</item>
///         </list>
///     </para>
/// </remarks>
public static class LegacyMigrationFeature
{
    // static readonly (NOT const) so the if/else around DI registration does not produce
    // CS0162 "unreachable code" when the value is flipped to false at deletion time.
    public static readonly bool Enabled = true;
}
